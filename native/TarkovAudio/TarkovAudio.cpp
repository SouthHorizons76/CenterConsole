// TarkovAudio: a tiny VST2 effect meant to be hosted by Equalizer APO on the user's existing headset
// endpoint (no virtual audio devices). On/off and tuning are read from the registry, written by
// CenterConsole:
//
//   HKLM\SOFTWARE\CenterConsole\TarkovAudio
//     Enabled          DWORD 0/1
//     BoostDb          DWORD 0..24      max boost for quiet sounds
//     LoudThresholdDb  DWORD 0..60      loud sounds above -N dBFS are compressed
//     PresenceDb       DWORD 0..12      EQ boost around 3 kHz
//
// A background thread polls the key every 100 ms; the audio thread only reads atomics.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstring>
#include <mutex>
#include <thread>

#include "TarkovDsp.h"
#include "vst2_min.h"

namespace
{

constexpr wchar_t kRegKey[] = L"SOFTWARE\\CenterConsole\\TarkovAudio";
constexpr int32_t kUniqueId = ('C' << 24) | ('C' << 16) | ('T' << 8) | 'A';

bool ReadDword(const wchar_t* name, DWORD& value)
{
    DWORD size = sizeof(value);
    return RegGetValueW(HKEY_LOCAL_MACHINE, kRegKey, name, RRF_RT_REG_DWORD, nullptr, &value, &size) ==
           ERROR_SUCCESS;
}

class Plugin
{
public:
    explicit Plugin(audioMasterCallback master) : master_(master)
    {
        std::memset(&effect_, 0, sizeof(effect_));
        effect_.magic = kEffectMagic;
        effect_.dispatcher = &Plugin::Dispatcher;
        effect_.process = &Plugin::ProcessAccumulating;
        effect_.setParameter = &Plugin::SetParameter;
        effect_.getParameter = &Plugin::GetParameter;
        effect_.numPrograms = 0;
        effect_.numParams = 0;
        effect_.numInputs = 2;
        effect_.numOutputs = 2;
        effect_.flags = effFlagsCanReplacing;
        effect_.ioRatio = 1.0f;
        effect_.object = this;
        effect_.uniqueID = kUniqueId;
        effect_.version = 100;
        effect_.processReplacing = &Plugin::ProcessReplacing;
        effect_.initialDelay = dsp_.latencySamples();

        PollRegistry();
        dsp_.snapMix(enabled_.load(std::memory_order_relaxed));
        poller_ = std::thread([this] { PollLoop(); });
    }

    ~Plugin()
    {
        {
            std::lock_guard<std::mutex> lock(stopMutex_);
            stop_ = true;
        }
        stopCv_.notify_all();
        if (poller_.joinable())
            poller_.join();
    }

    AEffect* effect() { return &effect_; }

private:
    static Plugin* From(AEffect* e) { return static_cast<Plugin*>(e->object); }

    static intptr_t VSTCALLBACK Dispatcher(AEffect* e, int32_t opcode, int32_t index, intptr_t value, void* ptr,
                                           float opt)
    {
        Plugin* self = From(e);
        switch (opcode)
        {
        case effClose:
            delete self;
            return 1;
        case effSetSampleRate:
            self->dsp_.setSampleRate(opt);
            self->dsp_.snapMix(self->enabled_.load(std::memory_order_relaxed));
            e->initialDelay = self->dsp_.latencySamples();
            return 1;
        case effMainsChanged:
            if (value)
                self->dsp_.reset();
            return 1;
        case effGetPlugCategory:
            return kPlugCategEffect;
        case effGetEffectName:
        case effGetProductString:
            CopyString(ptr, "TarkovAudio", 32);
            return 1;
        case effGetVendorString:
            CopyString(ptr, "CenterConsole", 64);
            return 1;
        case effGetVendorVersion:
            return 100;
        case effGetVstVersion:
            return 2400;
        case effGetTailSize:
            return self->dsp_.latencySamples();
        case effCanDo:
            return 0;
        default:
            (void)index;
            return 0;
        }
    }

    static void CopyString(void* dst, const char* src, size_t max)
    {
        if (!dst)
            return;
        strncpy_s(static_cast<char*>(dst), max, src, _TRUNCATE);
    }

    static void VSTCALLBACK SetParameter(AEffect*, int32_t, float) {}
    static float VSTCALLBACK GetParameter(AEffect*, int32_t) { return 0.0f; }

    static void VSTCALLBACK ProcessReplacing(AEffect* e, float** inputs, float** outputs, int32_t frames)
    {
        From(e)->Process(inputs, outputs, frames);
    }

    static void VSTCALLBACK ProcessAccumulating(AEffect* e, float** inputs, float** outputs, int32_t frames)
    {
        // Legacy hosts only. Rarely used, so a heap scratch buffer is acceptable here.
        std::vector<float> l(frames), r(frames);
        float* tmp[2] = {l.data(), r.data()};
        From(e)->Process(inputs, tmp, frames);
        for (int32_t i = 0; i < frames; ++i)
        {
            outputs[0][i] += l[i];
            outputs[1][i] += r[i];
        }
    }

    void Process(float** inputs, float** outputs, int32_t frames)
    {
        tarkovaudio::Settings s;
        s.enabled = enabled_.load(std::memory_order_relaxed);
        s.boostDb = boostDb_.load(std::memory_order_relaxed);
        s.thresholdDb = thresholdDb_.load(std::memory_order_relaxed);
        s.presenceDb = presenceDb_.load(std::memory_order_relaxed);
        dsp_.process(s, inputs[0], inputs[1], outputs[0], outputs[1], frames);
    }

    void PollRegistry()
    {
        DWORD v = 0;
        enabled_.store(ReadDword(L"Enabled", v) && v != 0, std::memory_order_relaxed);
        if (ReadDword(L"BoostDb", v))
            boostDb_.store(static_cast<float>(std::min<DWORD>(v, 24)), std::memory_order_relaxed);
        if (ReadDword(L"LoudThresholdDb", v))
            thresholdDb_.store(-static_cast<float>(std::min<DWORD>(v, 60)), std::memory_order_relaxed);
        if (ReadDword(L"PresenceDb", v))
            presenceDb_.store(static_cast<float>(std::min<DWORD>(v, 12)), std::memory_order_relaxed);
    }

    void PollLoop()
    {
        std::unique_lock<std::mutex> lock(stopMutex_);
        while (!stopCv_.wait_for(lock, std::chrono::milliseconds(100), [this] { return stop_; }))
        {
            lock.unlock();
            PollRegistry();
            lock.lock();
        }
    }

    AEffect effect_;
    audioMasterCallback master_;
    tarkovaudio::Processor dsp_;

    std::atomic<bool> enabled_{false};
    std::atomic<float> boostDb_{10.0f};
    std::atomic<float> thresholdDb_{-18.0f};
    std::atomic<float> presenceDb_{3.0f};

    std::thread poller_;
    std::mutex stopMutex_;
    std::condition_variable stopCv_;
    bool stop_ = false;
};

} // namespace

extern "C" __declspec(dllexport) AEffect* VSTPluginMain(audioMasterCallback master)
{
    // The host's audioMasterVersion answer isn't checked: nothing here calls back into the host.
    return (new Plugin(master))->effect();
}
