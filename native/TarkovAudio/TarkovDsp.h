// TarkovAudio DSP: stereo-linked look-ahead dynamics tuned for "hear footsteps, don't get deafened by
// gunfire".
//
//   input -> presence EQ (peaking, ~3 kHz) -> look-ahead delay ---------------> x gain -> soft clip -> wet
//                     \-> linked peak detector -> gain computer -> smoothing --/
//
// Gain computer (static curve, in dB relative to the detector level):
//   above  Threshold            : downward compression, 8:1 (gunshots, explosions)
//   below  Threshold - 3 dB     : upward compression capped at BoostDb; its ratio also scales with
//                                 BoostDb (10 dB -> 2:1, 16+ dB -> 5:1)  (footsteps, movement)
//   below  -65 dBFS             : boost fades out by -80 dBFS so silence / noise floor isn't amplified
//   No make-up gain: loud sounds are only ever turned down, whatever level the game delivers them at.
//   (An automatic make-up gain was tried first; when gunfire arrived well below full scale it cancelled
//   out the reduction and left gunshots as loud as before.)
//
// One gain is computed from max(|L|,|R|) and applied to both channels, so left/right balance (and
// therefore footstep direction) is preserved. The bypass path is the same delayed dry signal, so turning
// the effect on/off is a click-free crossfade with constant latency.
//
// (A four-zone design with a background floor, foreground detection and program-dependent recovery was
// tried after this and reverted: indoor gunshot echo could get boosted as it decayed into the boost zone.)
#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>

namespace tarkovaudio
{

struct Settings
{
    bool enabled = false;
    float boostDb = 10.0f;     // max upward gain for quiet sounds
    float thresholdDb = -18.0f; // level (dBFS) above which loud sounds are squashed
    float presenceDb = 3.0f;    // peaking EQ gain at kPresenceHz
};

class Processor
{
public:
    static constexpr float kLookaheadMs = 2.0f;
    static constexpr float kDetectorReleaseMs = 40.0f;
    static constexpr float kGainReleaseMs = 150.0f;
    static constexpr float kMixFadeMs = 30.0f;
    static constexpr float kDownRatio = 8.0f;
    static constexpr float kUpGapDb = 3.0f;      // upward region starts this far below threshold
    static constexpr float kMaxUpSlope = 0.8f;   // keeps the curve monotonic: louder in -> louder out
    static constexpr float kFloorDb = -65.0f;    // boost starts fading below this
    static constexpr float kFloorRangeDb = 15.0f;
    static constexpr float kPresenceHz = 3000.0f;
    static constexpr float kPresenceQ = 0.9f;
    static constexpr float kSoftClipKnee = 0.9f;

    Processor() { setSampleRate(48000.0); }

    void setSampleRate(double sampleRate)
    {
        sampleRate_ = sampleRate > 1000.0 ? sampleRate : 48000.0;
        lookahead_ = std::max(1, static_cast<int>(std::lround(sampleRate_ * kLookaheadMs / 1000.0)));
        size_t size = 1;
        while (size < static_cast<size_t>(lookahead_) + 2)
            size <<= 1;
        for (int c = 0; c < 2; ++c)
        {
            dry_[c].assign(size, 0.0f);
            wet_[c].assign(size, 0.0f);
        }
        mask_ = size - 1;

        detRel_ = coef(kDetectorReleaseMs);
        gainAtt_ = coef(kLookaheadMs / 2.0f);
        gainRel_ = coef(kGainReleaseMs);
        mixCoef_ = coef(kMixFadeMs);
        eqValid_ = false; // force EQ recompute for the new rate
        reset();
    }

    void reset()
    {
        for (int c = 0; c < 2; ++c)
        {
            std::fill(dry_[c].begin(), dry_[c].end(), 0.0f);
            std::fill(wet_[c].begin(), wet_[c].end(), 0.0f);
            z1_[c] = z2_[c] = 0.0f;
        }
        writePos_ = 0;
        env_ = 1e-6f;
        gainDb_ = 0.0f;
    }

    int latencySamples() const { return lookahead_; }
    float currentGainDb() const { return gainDb_; }
    float currentMix() const { return mix_; }

    /// Applies the settings at once instead of crossfading (used on startup so audio doesn't fade in).
    void snapMix(bool enabled) { mix_ = enabled ? 1.0f : 0.0f; }

    void process(const Settings& s, const float* inL, const float* inR, float* outL, float* outR, int frames)
    {
        updateEq(s.presenceDb);

        const float threshold = std::clamp(s.thresholdDb, -60.0f, 0.0f);
        const float boostMax = std::clamp(s.boostDb, 0.0f, 24.0f);
        const float upThreshold = threshold - kUpGapDb;
        // The boost setting sets both the cap and how quickly the boost builds below upThreshold, so
        // raising it lifts typical footstep levels (-30..-40 dBFS) too, not just the very quietest sounds.
        // 10 dB -> 2:1 upward, 16+ dB -> 5:1.
        const float upSlope = std::min(kMaxUpSlope, boostMax / 20.0f);
        const float downSlope = 1.0f - 1.0f / kDownRatio;
        const float mixTarget = s.enabled ? 1.0f : 0.0f;

        const float* in[2] = {inL, inR};
        float* out[2] = {outL, outR};

        for (int i = 0; i < frames; ++i)
        {
            float x[2] = {in[0][i], in[1][i]};
            float e[2];
            for (int c = 0; c < 2; ++c)
            {
                // Transposed direct form II biquad.
                float y = b0_ * x[c] + z1_[c];
                z1_[c] = b1_ * x[c] - a1_ * y + z2_[c];
                z2_[c] = b2_ * x[c] - a2_ * y;
                e[c] = y;
                dry_[c][writePos_] = x[c];
                wet_[c][writePos_] = y;
            }

            // Linked peak detector: instant attack, exponential release.
            float peak = std::max(std::fabs(e[0]), std::fabs(e[1]));
            env_ = peak > env_ ? peak : peak + (env_ - peak) * detRel_;
            env_ = std::max(env_, 1e-6f); // also keeps the state out of denormal range
            float levelDb = 8.6858896f * std::log(env_);

            float targetDb = 0.0f;
            if (levelDb > threshold)
            {
                targetDb = -(levelDb - threshold) * downSlope;
            }
            else if (levelDb < upThreshold)
            {
                float boost = std::min(boostMax, (upThreshold - levelDb) * upSlope);
                if (levelDb < kFloorDb)
                    boost *= std::clamp(1.0f - (kFloorDb - levelDb) / kFloorRangeDb, 0.0f, 1.0f);
                targetDb = boost;
            }

            // Fast when gain must drop (the look-ahead lets it land before the transient), slow to recover.
            float k = targetDb < gainDb_ ? gainAtt_ : gainRel_;
            gainDb_ = targetDb + (gainDb_ - targetDb) * k;
            float gain = std::exp(gainDb_ * 0.11512925f); // 10^(dB/20)

            mix_ = mixTarget + (mix_ - mixTarget) * mixCoef_;

            size_t readPos = (writePos_ - static_cast<size_t>(lookahead_)) & mask_;
            for (int c = 0; c < 2; ++c)
            {
                float d = dry_[c][readPos];
                float w = softClip(wet_[c][readPos] * gain);
                out[c][i] = d + mix_ * (w - d);
            }

            writePos_ = (writePos_ + 1) & mask_;
        }
    }

private:
    float coef(float ms) const
    {
        return std::exp(-1.0f / (static_cast<float>(sampleRate_) * ms / 1000.0f));
    }

    static float softClip(float v)
    {
        float a = std::fabs(v);
        if (a <= kSoftClipKnee)
            return v;
        const float range = 1.0f - kSoftClipKnee;
        float shaped = kSoftClipKnee + range * std::tanh((a - kSoftClipKnee) / range);
        return v < 0.0f ? -shaped : shaped;
    }

    void updateEq(float presenceDb)
    {
        presenceDb = std::clamp(presenceDb, 0.0f, 12.0f);
        if (eqValid_ && presenceDb == eqGainDb_)
            return;
        eqValid_ = true;
        eqGainDb_ = presenceDb;

        // RBJ cookbook peaking EQ.
        const double pi = 3.14159265358979323846;
        double a = std::pow(10.0, presenceDb / 40.0);
        double w0 = 2.0 * pi * std::min<double>(kPresenceHz, sampleRate_ * 0.45) / sampleRate_;
        double alpha = std::sin(w0) / (2.0 * kPresenceQ);
        double cosw = std::cos(w0);
        double a0 = 1.0 + alpha / a;
        b0_ = static_cast<float>((1.0 + alpha * a) / a0);
        b1_ = static_cast<float>((-2.0 * cosw) / a0);
        b2_ = static_cast<float>((1.0 - alpha * a) / a0);
        a1_ = static_cast<float>((-2.0 * cosw) / a0);
        a2_ = static_cast<float>((1.0 - alpha / a) / a0);
    }

    double sampleRate_ = 48000.0;
    int lookahead_ = 96;
    std::vector<float> dry_[2];
    std::vector<float> wet_[2];
    size_t mask_ = 0;
    size_t writePos_ = 0;

    float detRel_ = 0, gainAtt_ = 0, gainRel_ = 0, mixCoef_ = 0;
    float env_ = 1e-6f;
    float gainDb_ = 0.0f;
    float mix_ = 0.0f;

    bool eqValid_ = false;
    float eqGainDb_ = 0.0f;
    float b0_ = 1, b1_ = 0, b2_ = 0, a1_ = 0, a2_ = 0;
    float z1_[2] = {0, 0};
    float z2_[2] = {0, 0};
};

} // namespace tarkovaudio
