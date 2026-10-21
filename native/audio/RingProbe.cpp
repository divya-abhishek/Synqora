#include "PcmRing.h"
#include <cstdio>
#include <vector>
#include <stdexcept>
void Check(bool ok, const char* step) { if (!ok) throw std::runtime_error(step); std::printf("PASS: %s\n", step); }
int main() {
    try {
        PcmRing ring; uint64_t a = PcmRing::Uninitialized, b = PcmRing::Uninitialized;
        int16_t output[6]{1,1,1,1,1,1};
        ring.Read(output, 6, a, 100);
        Check(output[0] == 0 && output[5] == 0, "unconnected capture is silent");
        const int16_t input[]{-32768, -7, 123, 32767}; ring.Write(input, 4, 200);
        ring.Read(output, 6, a, 300);
        Check(output[0] == input[0] && output[3] == input[3] && output[4] == 0, "PCM fidelity and silent underrun");
        ring.Read(output, 4, b, 300);
        Check(output[0] == input[0] && output[3] == input[3], "independent recording clients receive the same samples");
        ring.Read(output, 4, b, 2'000'201);
        Check(output[0] == 0 && output[3] == 0, "stale capture expires within 200 ms");
        ring.Write(input, 4, 3'000'000); ring.Clear(); ring.Read(output, 4, a, 3'000'100);
        Check(output[0] == 0 && output[3] == 0, "disconnect immediately clears old audio");
        std::vector<int16_t> burst(PcmRing::Capacity + 100, 77); ring.Write(burst.data(), burst.size(), 4'000'000);
        ring.Read(output, 4, a, 4'000'100);
        Check(output[0] == 77 && a == 4 + 4 + burst.size() - PcmRing::Capacity + 4, "overflow drops the oldest samples within fixed capacity");
        std::puts("Audio ring checks passed. These are user-mode checks, not kernel endpoint acceptance."); return 0;
    } catch (const std::exception& error) { std::printf("FAIL: %s\n", error.what()); return 1; }
}
