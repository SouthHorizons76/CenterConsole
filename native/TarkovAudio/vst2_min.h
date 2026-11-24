// Minimal VST 2.4 ABI declarations: only what TarkovAudio needs to be loaded by Equalizer APO.
// Written from the publicly documented binary layout (as used by many open-source hosts); the
// Steinberg VST2 SDK itself is not required.
#pragma once

#include <cstdint>

#if defined(_WIN32)
#define VSTCALLBACK __cdecl
#else
#define VSTCALLBACK
#endif

struct AEffect;

typedef intptr_t(VSTCALLBACK* audioMasterCallback)(AEffect* effect, int32_t opcode, int32_t index,
                                                   intptr_t value, void* ptr, float opt);
typedef intptr_t(VSTCALLBACK* AEffectDispatcherProc)(AEffect* effect, int32_t opcode, int32_t index,
                                                     intptr_t value, void* ptr, float opt);
typedef void(VSTCALLBACK* AEffectProcessProc)(AEffect* effect, float** inputs, float** outputs,
                                              int32_t sampleFrames);
typedef void(VSTCALLBACK* AEffectProcessDoubleProc)(AEffect* effect, double** inputs, double** outputs,
                                                    int32_t sampleFrames);
typedef void(VSTCALLBACK* AEffectSetParameterProc)(AEffect* effect, int32_t index, float parameter);
typedef float(VSTCALLBACK* AEffectGetParameterProc)(AEffect* effect, int32_t index);

#pragma pack(push, 8)
struct AEffect
{
    int32_t magic;
    AEffectDispatcherProc dispatcher;
    AEffectProcessProc process; // deprecated accumulating process
    AEffectSetParameterProc setParameter;
    AEffectGetParameterProc getParameter;
    int32_t numPrograms;
    int32_t numParams;
    int32_t numInputs;
    int32_t numOutputs;
    int32_t flags;
    intptr_t resvd1;
    intptr_t resvd2;
    int32_t initialDelay;
    int32_t realQualities;
    int32_t offQualities;
    float ioRatio;
    void* object;
    void* user;
    int32_t uniqueID;
    int32_t version;
    AEffectProcessProc processReplacing;
    AEffectProcessDoubleProc processDoubleReplacing;
    char future[56];
};
#pragma pack(pop)

constexpr int32_t kEffectMagic = ('V' << 24) | ('s' << 16) | ('t' << 8) | 'P';

enum : int32_t
{
    effFlagsCanReplacing = 1 << 4,
    effFlagsNoSoundInStop = 1 << 9,
};

enum : int32_t
{
    effOpen = 0,
    effClose = 1,
    effGetParamName = 8,
    effSetSampleRate = 10,
    effSetBlockSize = 11,
    effMainsChanged = 12,
    effGetPlugCategory = 35,
    effGetEffectName = 45,
    effGetVendorString = 47,
    effGetProductString = 48,
    effGetVendorVersion = 49,
    effCanDo = 51,
    effGetTailSize = 52,
    effGetVstVersion = 58,
};

enum : int32_t
{
    audioMasterVersion = 1,
};

enum : int32_t
{
    kPlugCategEffect = 1,
};
