# Synqora microphone development

The capture endpoint uses Microsoft's SysVAD PortCls/WaveRT foundation under the **Microsoft Public License (MS-PL)**, with an original Synqora producer interface and bounded PCM broadcast buffer. This is source integration; it does not redistribute WO Mic or a purchased audio driver.

Pinned base: `microsoft/Windows-driver-samples` commit `4d4618400525f9b2ce66fa2e267f58a55043be40`, directory `audio/sysvad`. `tools/prepare_audio.py` requires that exact checkout in `.local/windows-driver-samples`, stages the headers and required sources under ignored `build/source`, and applies explicit checked edits. `LICENSE.SysVAD.txt` is preserved for distribution. The preparation recipe and staged derivative sources are under MS-PL; original Synqora files in this folder are also offered under MS-PL.

Build: `tools/build_audio.cmd` with the local MSVC toolchain and Microsoft WDK NuGet 10.0.26100.1882. This compiles only. It does not install a driver, enable test-signing, change Secure Boot, or create Microsoft production trust.

The endpoint exposes 48 kHz, 16-bit mono. An exclusive buffered control handle accepts at most 8192 bytes per write; each recording stream has its own cursor. A 160 ms ring drops old input on overflow, zeros underruns, expires stale data after 200 ms and clears on producer close. Capture reads allocate nothing and never wait on the UI/network. The interactive-user ACL and device lifecycle still need dedicated kernel/VM security and concurrency validation before release.

The ring probe checks silence, byte fidelity, multiple readers, expiry, disconnect and overflow in user mode. A successful compile or ring probe is **not** evidence that Windows has loaded this driver or that recording apps can capture from it. Required next gates: signed isolated-VM installation, Driver Verifier/HLK, WASAPI multi-client recording, sleep/removal, security, and Microsoft production signing. Keep this development driver out of consumer installers until those pass.
