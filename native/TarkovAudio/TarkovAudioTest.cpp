// Offline checks for TarkovAudio: DSP behaviour, the DLL's VST2 entry point, and CPU cost.
// Usage: TarkovAudioTest.exe [path\to\TarkovAudio.dll]
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <chrono>
#include <cmath>
#include <cstdio>
#include <random>
#include <vector>

#include "TarkovDsp.h"
#include "vst2_min.h"

using tarkovaudio::Processor;
using tarkovaudio::Settings;

static int g_failures = 0;
#define CHECK(cond, ...)                                                                                       \
    do                                                                                                         \
    {                                                                                                          \
        if (!(cond))                                                                                           \
        {                                                                                                      \
            ++g_failures;                                                                                      \
            std::printf("  FAIL: " __VA_ARGS__);                                                               \
            std::printf("\n");                                                                                 \
        }                                                                                                      \
    } while (0)

static float Db(float lin) { return 20.0f * std::log10(std::max(lin, 1e-9f)); }

struct Burst
{
    int start, length;
    float amplitude;
};

// Ambience bed at about -60 dBFS peak: in game there's always wind/room tone under the footsteps, and
// testing over digital silence would hide how the upward boost actually behaves.
constexpr float kAmbience = 0.001f;

// Stereo noise bursts; the right channel is 6 dB quieter so we can check panning is preserved.
static void Render(const std::vector<Burst>& bursts, int total, std::vector<float>& l, std::vector<float>& r)
{
    std::mt19937 rng(1234);
    std::uniform_real_distribution<float> dist(-1.0f, 1.0f);
    l.assign(total, 0.0f);
    r.assign(total, 0.0f);
    for (int i = 0; i < total; ++i)
    {
        float v = dist(rng) * kAmbience;
        l[i] = v;
        r[i] = v * 0.5f;
    }
    for (const Burst& b : bursts)
        for (int i = 0; i < b.length && b.start + i < total; ++i)
        {
            float v = dist(rng) * b.amplitude;
            l[b.start + i] = v;
            r[b.start + i] = v * 0.5f;
        }
}

static float Rms(const std::vector<float>& x, int start, int len)
{
    double sum = 0;
    for (int i = start; i < start + len; ++i)
        sum += double(x[i]) * x[i];
    return float(std::sqrt(sum / len));
}

static void TestDsp()
{
    std::printf("[dsp] footsteps vs gunshots\n");
    const int sr = 48000;
    const int step = sr * 30 / 1000, shot = sr * 60 / 1000;
    std::vector<Burst> bursts;
    // 2 s of quiet "footsteps" (-45 dBFS peak), then a gunshot (-1 dBFS) every 250 ms with footsteps in between.
    for (int t = 0; t < 2 * sr; t += sr * 2 / 5)
        bursts.push_back({t, step, 0.0056f});
    for (int t = 2 * sr; t < 4 * sr; t += sr / 4)
        bursts.push_back({t, shot, 0.89f});
    int total = 4 * sr + sr / 2;
    std::vector<float> l, r;
    Render(bursts, total, l, r);

    Processor p;
    p.setSampleRate(sr);
    Settings s;
    s.enabled = true;
    p.snapMix(true);
    std::vector<float> ol(total), orr(total);
    p.process(s, l.data(), r.data(), ol.data(), orr.data(), total);

    int lat = p.latencySamples();
    float peakOut = 0;
    for (int i = 0; i < total; ++i)
        peakOut = std::max({peakOut, std::fabs(ol[i]), std::fabs(orr[i])});

    // Measure a footstep late in the quiet section (gain has settled) and a gunshot late in the loud one.
    const Burst& foot = bursts[3];
    const Burst& gun = bursts.back();
    float footIn = Rms(l, foot.start, foot.length), footOut = Rms(ol, foot.start + lat, foot.length);
    float gunIn = Rms(l, gun.start, gun.length), gunOut = Rms(ol, gun.start + lat, gun.length);
    float footChange = Db(footOut) - Db(footIn);
    float gunChange = Db(gunOut) - Db(gunIn);
    float gapBefore = Db(gunIn) - Db(footIn), gapAfter = Db(gunOut) - Db(footOut);
    float panIn = Db(Rms(r, foot.start, foot.length)) - Db(footIn);
    float panOut = Db(Rms(orr, foot.start + lat, foot.length)) - Db(footOut);

    std::printf("  footstep change: %+.1f dB\n", footChange);
    std::printf("  gunshot change:  %+.1f dB\n", gunChange);
    std::printf("  gun-to-footstep gap: %.1f dB -> %.1f dB\n", gapBefore, gapAfter);
    std::printf("  L/R balance on footstep: %.2f dB -> %.2f dB\n", panIn, panOut);
    std::printf("  output peak: %.1f dBFS\n", Db(peakOut));

    CHECK(footChange > 5.0f, "footsteps should be boosted by > 5 dB (got %+.1f)", footChange);
    CHECK(gunChange < -10.0f, "full-scale gunshots should be reduced by > 10 dB (got %+.1f)", gunChange);
    CHECK(gapAfter < gapBefore - 15.0f, "gap should shrink by > 15 dB");
    CHECK(std::fabs(panIn - panOut) < 0.5f, "stereo balance should be preserved");
    CHECK(peakOut <= 1.0f, "output must not exceed full scale");

    std::printf("[dsp] bypass is bit-exact delayed input\n");
    Processor off;
    off.setSampleRate(sr);
    Settings so;
    so.enabled = false;
    off.snapMix(false);
    off.process(so, l.data(), r.data(), ol.data(), orr.data(), total);
    bool exact = true;
    for (int i = lat; i < total && exact; ++i)
        exact = ol[i] == l[i - lat] && orr[i] == r[i - lat];
    CHECK(exact, "bypass output differs from delayed input");

    std::printf("[dsp] on/off crossfade stays finite and bounded\n");
    Processor x;
    x.setSampleRate(sr);
    bool ok = true;
    for (int block = 0; block * 480 + 480 <= total; ++block)
    {
        Settings sx;
        sx.enabled = (block / 20) % 2 == 0; // toggle every 200 ms
        x.process(sx, l.data() + block * 480, r.data() + block * 480, ol.data(), orr.data(), 480);
        for (int i = 0; i < 480; ++i)
            ok &= std::isfinite(ol[i]) && std::fabs(ol[i]) <= 1.0f;
    }
    CHECK(ok, "crossfade produced non-finite or clipped samples");
}

// Regression: with the old automatic make-up gain, gunfire arriving below full scale came out as loud
// as it went in (or louder). Loud sounds must be turned down whatever level the game delivers them at.
static void TestGunshotLevels()
{
    std::printf("[dsp] gunshots are reduced at any input level\n");
    const int sr = 48000, shot = sr * 60 / 1000;
    for (float inDb : {-1.0f, -6.0f, -12.0f})
    {
        std::vector<Burst> bursts;
        for (int t = 0; t < 2 * sr; t += sr / 4)
            bursts.push_back({t, shot, std::pow(10.0f, inDb / 20.0f)});
        int total = 2 * sr + sr / 4;
        std::vector<float> l, r, ol(total), orr(total);
        Render(bursts, total, l, r);

        Processor p;
        p.setSampleRate(sr);
        Settings s;
        s.enabled = true;
        p.snapMix(true);
        p.process(s, l.data(), r.data(), ol.data(), orr.data(), total);

        const Burst& gun = bursts.back();
        float change = Db(Rms(ol, gun.start + p.latencySamples(), gun.length)) - Db(Rms(l, gun.start, gun.length));
        std::printf("  %+.0f dBFS gunshot: %+.1f dB\n", inDb, change);
        CHECK(change < -2.0f, "gunshot at %+.0f dBFS should be reduced (got %+.1f)", inDb, change);
    }
}

// Footsteps around -35 dBFS (typical in game) must get a real lift, and the boost setting must
// actually change it (it used to only cap the boost, so the slider did nearly nothing at this level).
static void TestBoostSlider()
{
    std::printf("[dsp] quiet boost setting lifts -35 dBFS footsteps\n");
    const int sr = 48000, step = sr * 30 / 1000;
    std::vector<Burst> bursts;
    for (int t = 0; t < 2 * sr; t += sr * 2 / 5)
        bursts.push_back({t, step, std::pow(10.0f, -35.0f / 20.0f)});
    int total = 2 * sr + sr / 4;
    std::vector<float> l, r, ol(total), orr(total);
    Render(bursts, total, l, r);

    float previous = -100.0f;
    for (float boost : {0.0f, 6.0f, 10.0f, 14.0f, 18.0f})
    {
        Processor p;
        p.setSampleRate(sr);
        Settings s;
        s.enabled = true;
        s.boostDb = boost;
        s.presenceDb = 0.0f; // isolate the dynamics
        p.snapMix(true);
        p.process(s, l.data(), r.data(), ol.data(), orr.data(), total);

        const Burst& foot = bursts.back();
        float change = Db(Rms(ol, foot.start + p.latencySamples(), foot.length)) - Db(Rms(l, foot.start, foot.length));
        std::printf("  boost %2.0f: footstep %+.1f dB\n", boost, change);
        CHECK(change > previous + 1.0f, "each step up in boost should lift footsteps further");
        previous = change;
    }
    CHECK(previous > 8.0f, "max boost should lift -35 dBFS footsteps by > 8 dB");
}

static void TestCpu()
{
    std::printf("[cpu] 10 minutes of 48 kHz stereo audio\n");
    const int sr = 48000, block = 480;
    std::vector<float> l(block), r(block), ol(block), orr(block);
    std::mt19937 rng(7);
    std::uniform_real_distribution<float> dist(-0.5f, 0.5f);
    for (int i = 0; i < block; ++i)
        l[i] = r[i] = dist(rng);
    Processor p;
    p.setSampleRate(sr);
    Settings s;
    s.enabled = true;
    int blocks = sr * 600 / block;
    auto t0 = std::chrono::steady_clock::now();
    for (int b = 0; b < blocks; ++b)
        p.process(s, l.data(), r.data(), ol.data(), orr.data(), block);
    double secs = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
    std::printf("  %.3f s for 600 s of audio = %.3f%% of one core\n", secs, secs / 600.0 * 100.0);
    CHECK(secs / 600.0 < 0.01, "should use < 1%% of a core");
}

static intptr_t VSTCALLBACK HostCallback(AEffect*, int32_t opcode, int32_t, intptr_t, void*, float)
{
    return opcode == audioMasterVersion ? 2400 : 0;
}

static void TestDll(const wchar_t* path)
{
    std::printf("[dll] loading via VSTPluginMain\n");
    HMODULE mod = LoadLibraryW(path);
    CHECK(mod != nullptr, "LoadLibrary failed (%lu)", GetLastError());
    if (!mod)
        return;
    using MainFn = AEffect* (*)(audioMasterCallback);
    auto main = reinterpret_cast<MainFn>(GetProcAddress(mod, "VSTPluginMain"));
    CHECK(main != nullptr, "VSTPluginMain export missing");
    if (!main)
        return;

    AEffect* e = main(&HostCallback);
    CHECK(e && e->magic == kEffectMagic, "bad AEffect");
    if (!e)
        return;
    CHECK(e->numInputs == 2 && e->numOutputs == 2, "expected 2 in / 2 out");
    CHECK((e->flags & effFlagsCanReplacing) != 0, "processReplacing flag missing");

    char name[64] = {};
    e->dispatcher(e, effOpen, 0, 0, nullptr, 0);
    e->dispatcher(e, effGetEffectName, 0, 0, name, 0);
    e->dispatcher(e, effSetSampleRate, 0, 0, nullptr, 48000.0f);
    e->dispatcher(e, effSetBlockSize, 0, 480, nullptr, 0);
    e->dispatcher(e, effMainsChanged, 0, 1, nullptr, 0);
    std::printf("  name=%s latency=%d samples\n", name, e->initialDelay);

    std::vector<float> l(480, 0.25f), r(480, -0.25f), ol(480), orr(480);
    float* in[2] = {l.data(), r.data()};
    float* out[2] = {ol.data(), orr.data()};
    for (int i = 0; i < 50; ++i)
        e->processReplacing(e, in, out, 480);
    CHECK(std::isfinite(ol[479]) && std::isfinite(orr[479]), "non-finite output from DLL");

    e->dispatcher(e, effMainsChanged, 0, 0, nullptr, 0);
    e->dispatcher(e, effClose, 0, 0, nullptr, 0);
    FreeLibrary(mod);
}

int wmain(int argc, wchar_t** argv)
{
    TestDsp();
    TestGunshotLevels();
    TestBoostSlider();
    TestCpu();
    if (argc > 1)
        TestDll(argv[1]);
    std::printf(g_failures == 0 ? "ALL PASSED\n" : "%d FAILURE(S)\n", g_failures);
    return g_failures == 0 ? 0 : 1;
}
