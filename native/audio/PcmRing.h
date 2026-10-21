#pragma once
#ifdef _KERNEL_MODE
using int16_t = short;
using uint64_t = unsigned long long;
#else
#include <stddef.h>
#include <stdint.h>
#endif

// Caller supplies synchronization. No allocation, callbacks or waits on the capture path.
// Each capture client owns a cursor, so two recording apps hear the same input.
class PcmRing {
public:
    static constexpr size_t Capacity = 7680; // 160 ms at 48 kHz mono.
    static constexpr uint64_t Uninitialized = ~uint64_t(0);
private:
    int16_t samples_[Capacity]{};
    uint64_t written_ = 0, clearedAt_ = 0, lastWrite_ = 0;
public:
    void Clear() {
        for (auto& sample : samples_) sample = 0;
        clearedAt_ = written_; lastWrite_ = 0;
    }
    void Write(const int16_t* samples, size_t count, uint64_t now100ns) {
        if (lastWrite_ && now100ns - lastWrite_ > 2'000'000) Clear();
        for (size_t i = 0; i < count; ++i) samples_[written_++ % Capacity] = samples[i];
        lastWrite_ = now100ns;
    }
    void Read(int16_t* output, size_t count, uint64_t& cursor, uint64_t now100ns) const {
        const uint64_t oldest = written_ > Capacity ? written_ - Capacity : 0;
        const uint64_t start = oldest > clearedAt_ ? oldest : clearedAt_;
        if (cursor == Uninitialized || cursor < start || cursor > written_) cursor = start;
        if (!lastWrite_ || now100ns - lastWrite_ > 2'000'000) {
            cursor = written_;
            for (size_t i = 0; i < count; ++i) output[i] = 0;
            return;
        }
        for (size_t i = 0; i < count; ++i) output[i] = cursor < written_ ? samples_[cursor++ % Capacity] : 0;
    }
};
