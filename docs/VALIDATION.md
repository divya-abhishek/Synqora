# Validation — Synqora 0.3.0 / 21 October 2026

## 0.3 development revision

- Windows publish and MSI cabinet verification: all 821 files match; 12 screens render from both the published and extracted payload; no installer actions executed. Android minified release v2 signature verified. A final release-emulator launch was unavailable because the emulator had stopped.
- Actual emulator audio/video, TLS identity rejection/retry, raw pointer/scroll/click, live IME edits/keys and reconnect permission revocation passed. The foreground session also delivered audio and video while the Activity was backgrounded and its display slept.
- Native camera contention, clear, lifetime, timestamps, NV12/RGB32 and Fit/Fill center-crop tests passed. Native Windows receiver decoded 193 frames into 720×1280 and independently sampled a non-flat 1280×720 source, with no media errors.
- Android simultaneous-streaming navigation measured 562 frames, 17.44% jank, p50 25ms and p95 61ms in the host emulator. This does not meet the 60 FPS target. Windows UI callback p95 9.48ms is a rendering-callback proxy, not GPU presentation timing.
- Final visual-only changes remove Windows page/selection movement, increase desktop transparency, simplify feature rows and shorten Android fades. Exact optical bubble refraction and visual acceptance remain open.
- Physical-phone acceptance of this revision, original microphone driver kernel validation/signing, and installation lifecycle acceptance remain unfinished. Production release gates are false.

## Historical 0.2 evidence

Development evidence only. Protocol tests and native screen rendering do not establish selectable devices in Google Meet or Windows Camera.

## Current checks

| Check | Result and scope |
| --- | --- |
| Windows Release | Passed, zero warnings/errors |
| Android debug / lint / unit | Passed; lint errors corrected, dependency/style warnings remain; six live-edit cases pass |
| Tokens / semantic contrast | Passed; dark minimum 6.24:1, light 4.59:1 |
| Protocol | Passed pin/key rejection, permission, fractional motion, edit replacement, real keys/modifiers, exact PCM, oriented JPEG validation, oversize rejection and disconnect reset |
| Android integration | Actual AudioRecord + CameraX → pinned TLS; native touch history, click/scroll, per-character InputConnection commits, delete, Enter/Tab and reconnect permission revocation pass |
| Native Windows receiver | Actual emulator media → WinUI decode/meter/preview → in-process native source reader; desktop input denied |
| Camera regression | Releasing an activator no longer shuts down a live source; NV12/RGB32, timestamps, aspect, clear and cleanup pass |
| Camera device acceptance | Separate Windows enumeration/sample check required before reporting availability. Installed old DLL still fails MF_E_SHUTDOWN; updated external-app capture is unaccepted |
| Microphone | Identified VB-CABLE WASAPI route implemented, unchanged signed vendor package bundled. Not installed or independently captured here |
| Original audio research driver | Compiled, INF and user-mode ring checks pass; unsigned, unloaded and excluded |
| Navigation | No Tools/Media/Send; disconnected keys, mute, gain and reduced motion pass |
| Packaging | Passed: zero-warning MSI, all 821 extracted files match publish, 12 screen/theme renders from publish and extracted app; ZIP critical hashes match. Minified APK v2 signature and non-debuggable emulator dashboard smoke pass |
| Physical phone / external targets / install lifecycle | Not accepted |

Evidence: [backend](../previews/backend-integration.json), [Windows receiver](../previews/windows-receiver-metrics.json). A receiver run decoded 105 of 106 frames into 600×800; the native reader received 1280×720 NV12 with luma 16–232. This is an in-process source-reader check, not Windows Frame Server acceptance.

The isolated input sink records per-character edits and keys producing “Synqora backend test” then Enter/Tab. No tests move the user's desktop pointer or type into another app. Desktop computer use was stopped by Escape earlier; no further desktop input automation was performed.

Emulator microphone PCM can be silent. Delivery does not establish audible fidelity, camera quality, Wi-Fi jitter, A/V sync or selectable Windows endpoints.

## Performance

API 35 emulator, host OpenGL on RTX 2060, 1080×2400 / density 420. The current real-clock workflow performs 24 actual Home/Settings touches while simultaneous audio/camera streams reach the receiver.

- 519 UI frames; 93 janky (17.92%); median 25 ms, p95 73 ms, p99 105 ms.
- 308 received camera frames, averaging 20.38 FPS.
- Camera diagnostic: supported target 30 FPS, encoded about 23 FPS, 800×600, average JPEG encoding about 17 ms.
- GPU percentile overflow (4950 ms) is not reliable GPU timing.

Raw [frame times](../previews/android-streaming-gfxinfo-realtime.txt) and [transport measurements](../previews/backend-realtime-perf.json) are retained. Historical -0.1 reports show 90.60% jank, median 77 ms and p95 150 ms. Layout, navigation path and emulator state changed; this is not a controlled causal comparison.

Reusable YUV/NV21 buffers avoid per-frame RGB allocation/rotation; AE ranges reflect camera capability and actual throughput is shown. Raw touch bypasses Compose frame pacing. UI/transport/media are separate; Windows preview upload pauses when minimized. The 60 FPS target remains unmet.

Remaining: release/steady-state CPU/GPU profiling, physical-phone streaming and correction of measured bottlenecks. Native 30/60 FPS output can repeat older input pixels; it does not prove unique sensor frames. Hardware video encoding is unimplemented.

## Screens and accessibility

Six screens: Home, Microphone, Webcam, Touchpad, Keyboard, Settings. Windows light/dark covers 1000×760, 960×760 and 720×720 requests. Android covers both themes at 1080×2400, dark at 720×1600 and 1920×1080, 1.6× text and solid fallback.

The [gallery](../previews/index.html) and [dimensions](../previews/screenshots.json) contain 65 actual PNGs. RenderTargetBitmap captures Windows app content, not caption buttons. Most images are disconnected; live captures are labeled. Android capture checks unique page content and app semantics. [Package evidence](../previews/package-validation-0.2.json) records exact artifact hashes and check scope; [APK smoke evidence](../previews/android-release-smoke.json) records the release dashboard test.

Both platforms share Graphite tokens and theme-aware vector marks. Windows acrylic has opaque fallbacks. Android uses layered material rather than claiming arbitrary backdrop blur. Reduced motion/transparency/effects controls are available.

Physical IMEs, screen readers, keyboard focus, cutouts, high-DPI monitors, live rotation, minimize/restore, sleep and sustained thermal behavior need acceptance.

## Distribution and installed state

MSI bundles .NET/WinUI/VC runtimes, Synqora.Camera.dll, its helper and unchanged VB-CABLE Pack45 archive. Guards require Synqora.pri and critical native/setup files. MSI cabinets are extracted read-only, every file is hashed against the publish, and the extracted app must render all 12 screen/theme combinations.

Signed APK smoke validation uses the API 35 emulator. The MSI itself is unsigned. No private keys or invitations are distributed. Production remains gated by release-gates.json.

The earlier startup crash was the missing merged resource index; 0.1.1 fixed it and 0.2 retains publish checks and bounded per-user logging.

Previously rejected registration/installation actions were not retried through another mechanism. User/admin upgrade must replace the old camera DLL. Microphone setup is a separate visible vendor installer and reboot. External capture, clean offline install, firewall behavior, rollback/uninstall and physical streaming remain gates.
