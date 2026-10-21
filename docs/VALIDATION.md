# Validation — Synqora 0.5.0 / 22 October 2026

This is a development preview. The checks below establish specific emulator, protocol and packaging behavior; they do not establish a production-ready Phone Link replacement or accepted physical Bluetooth calling.

## Current results

- **Builds:** Windows Release/publish passes with zero warnings/errors. Android debug, unit tests and lint pass; lint reports zero errors and 33 warnings. Shared-token/contrast checks pass. The apps expose dark mode only; historical light tokens are unused library values.
- **Protocol/files:** pinned identity rejection, pairing-key rejection, input permission, live text edits/keys, fractional pointer movement, exact PCM, oriented JPEG validation and disconnect revocation pass. Unaccepted file lanes are rejected. Approved transfers verify SHA-256, corrupt data/unsafe filenames are rejected and partial files are removed. Metadata above 1 GB is preserved without allocating a large file; a multi-gigabyte physical transfer has not been measured.
- **Companion integration:** actual synthetic source notification and original RemoteInput PendingIntent reply pass. A 1,048,737-byte payload transfers PC → phone → PC with matching hash. A test MediaStore image produces metadata, a thumbnail and a verified original download. A synthetic cellular call-log item reaches the PC. Fixtures run only on a disposable emulator and are cleaned up; no real call or personal message was sent. [Receiver evidence](validation/0.5/companion.json).
- **Capture/power:** actual AudioRecord and CameraX stop when the PC disconnects, and the camera is closed at the OS level. Idle mode holds no capture wake lock. An isolated 13-second idle emulator sample used 9 ms of process CPU; this is not a multi-hour battery benchmark. The reported physical-phone battery drain needs a fresh overnight/idle test with the new build.
- **Functional backend:** actual camera/audio over pinned TLS, display-off capture, live keyboard edits/Enter/Tab, pointer/click/scroll and reconnect input-permission revocation pass. A separate navigation test verifies all three root pages, disabled disconnected controls, microphone mute/gain and reduced motion.
- **Native Windows receiver:** 182 frames decoded into 720×1280, with a non-flat independent 1280×720 native camera sample and no media/source errors. During 24 immediate page changes, rendering-callback p95 was 7.98 ms across 785 callbacks, with two over 33 ms. These are callback timings, not GPU presentation timings. Capture stopped when this receiver exited. [Metrics](validation/0.5/native-receiver.json).
- **Final streaming performance:** 539 frames; 111 janky (20.59%); p50 36 ms, p95 81 ms, p99 113 ms in the API 35 software-GPU emulator at an explicitly set 1080×2400 / density 420. The receiver saw 318 camera frames, averaging 18.13 FPS. There were 109 slow draw-command frames versus 33 slow UI-thread frames; software rendering and camera JPEG processing remain material costs. GPU percentile overflow is unreliable. Compilers were stopped before prebuilt measurement. Navigation has no page animation and the new shell does not record or refract a background layer. **The 60 FPS target remains unaccepted.** An earlier candidate measured 24.36% jank / p95 85 ms at the emulator's default landscape 1920×1080 / density 320; these runs are not a controlled causal comparison. [Final summary](validation/0.5/streaming-final-summary.json) · [Earlier sample](validation/0.5/streaming-summary.json).
- **Visual/layout:** ten native screens per platform are captured in dark mode. Windows is checked at 720, 1080, 1920 and 2560 requested pixels with 200 live-resize, horizontal-bounds, single-selection and page-retention checks. The phone navigation has its own layout space so it cannot cover page controls. Android captures cover 1080×2400, 720×1600, landscape 1920×1080 and Settings at 1.6 font scale. See the [screen gallery](SCREENSHOTS.md).

## Calling acceptance

Recent cellular history sync is tested with a synthetic call-log item. Direct dialing uses TelecomManager behind separate phone opt-in and CALL_PHONE permission; no real call was placed. The optional MSIX declares Windows phone capabilities and implements actual HFP line discovery, dialing, active-call events, answer/end/mute and PC/phone audio routing. Package installation, capability approval, actual Bluetooth microphone/speaker audio, phone compatibility and simultaneous call/streaming behavior remain unverified. Ordinary MSI/portable builds cannot enable that packaged capability path. WhatsApp direct outgoing calls and its private call history are unavailable; source notification actions and HFP exposure depend on the app/device. See [calling setup](CALLING.md).

## Remaining acceptance

Physical-phone multi-hour idle battery/thermal behavior; physical high-rate camera quality; external Meet/Camera enumeration and microphone capture; physical Bluetooth calling; screen reader/hardware keyboard/high-DPI behavior; tray interaction; clean offline installation, upgrade and uninstall. Computer Use was stopped by the user's Escape key; no subsequent desktop interaction was automated. No driver or experimental MSIX was installed and no certificate trust was changed during these checks. Production gates remain false in release-gates.json.

The Windows review packaging flow requires published and extracted MSI payloads to render all ten dark screens, and compares every MSI cabinet file with the published payload. It extracts files without executing installer actions. Android release checks verify the signature and non-debuggable startup/navigation. The optional MSIX is manifest-validated and signed with a file-only development certificate; installation and OS capability acceptance remain open. Historical results below do not describe the 0.5 UI.

---

# Historical validation — Synqora 0.4.0 / 21 October 2026

## 0.4 development preview

- New Windows sidebar/workspace, per-button selection, finite content columns and cached pages passed 144 horizontal-bounds/selection checks across requested 720, 1080, 1920 and 2560-pixel sizes and live resizing. Native screenshots cover all six screens in both themes. The visible source build was also maximized and navigated through the Windows UI; settled layout and selection were correct. The installed 0.3 copy remains unchanged until the user upgrades.
- Android navigation, disconnected key states, mute, gain and reduced-motion controls passed the actual Compose interaction test. AudioRecord/CameraX, pinned TLS, screen-off streaming, raw pointer/scroll/click, live IME edits/keys and reconnect permission revocation passed against an isolated receiver.
- Android streaming navigation before bounding the optical sample: 541 frames, 15.16% jank, p50 24 ms, p95 77 ms. Sampling only a 136-dp bottom strip and skipping recording on feature/fallback screens measured 585 frames, 7.18% jank, p50 18 ms, p95 36 ms. Both used the API 35 emulator with simultaneous audio/video and actual navigation touches. This is development evidence, not a controlled physical-device benchmark or a passed 60 FPS gate.
- Final Balanced-quality check after a fresh emulator boot, with build/compiler processes stopped and prebuilt instrumentation: 566 frames, 18.55% jank, p50 23 ms, p95 57 ms. Audio/video and actual navigation touches reached the isolated receiver. This is the final reported result; the earlier 7.18% result is not a guaranteed improvement or the final build's acceptance result. Late optical experiments measured 77–86% jank in the loaded emulator; chained/multi-sample blur was removed, and the remaining single-sample refractive lens is restricted to optional High quality. Balanced does not record a backdrop layer. GPU duration now contributes to decorative-effects fallback. Sustained 60 FPS is not accepted.
- The native Windows receiver decoded 209 frames into 720×1280 and independently sampled a non-flat 1280×720 camera source, with no media errors. UI rendering-callback p95 was 9.20 ms during streaming; this is not GPU presentation timing.
- Android screenshots cover all six screens in both themes, 720×1600 and 1920×1080 viewports, and Settings at 1.6 font scale. Public app-only Windows screenshots use a generic machine label. No private desktop screenshots or pairing invitations are published.
- Windows Release build passed with zero warnings/errors. Android debug/unit/lint checks passed; lint reported zero errors and 28 warnings. Shared tokens and semantic palette checks pass (dark minimum 6.64:1; light 4.89:1 against the tested endpoints).
- The initial Windows optical-transform experiment crashed the native compositor. That transform was removed; the control effect uses blur and tint only. Windows does not provide exact refractive optics. Android 13+ has an original app-content lens shader, with fallback on older or constrained devices.
- Physical-phone acceptance, external-app audio capture, installation lifecycle and frame-time targets remain open. No original unsigned driver was installed or packaged. Production release gates remain false.
- Final distribution checks: all 854 MSI files matched the published payload; both publish and extracted MSI payload rendered 12 screen/theme combinations from an unrelated working directory. No installer actions were executed. The minified Android release APK passed v2 signature verification, non-debuggable startup, Settings navigation and return to Home on the emulator.

## 0.3 development revision

- Windows publish and MSI cabinet verification: all 821 files match; 12 screens render from both the published and extracted payload; no installer actions executed. Android minified release v2 signature verified. A final release-emulator launch was unavailable because the emulator had stopped.
- Actual emulator audio/video, TLS identity rejection/retry, raw pointer/scroll/click, live IME edits/keys and reconnect permission revocation passed. The foreground session also delivered audio and video while the Activity was backgrounded and its display slept.
- Native camera contention, clear, lifetime, timestamps, NV12/RGB32 and Fit/Fill center-crop tests passed. Native Windows receiver decoded 193 frames into 720×1280 and independently sampled a non-flat 1280×720 source, with no media errors.
- Android simultaneous-streaming navigation measured 562 frames, 17.44% jank, p50 25ms and p95 61ms in the host emulator. This does not meet the 60 FPS target. Windows UI callback p95 9.48ms is a rendering-callback proxy, not GPU presentation timing.
- Final visual-only changes remove Windows page/selection movement, increase desktop transparency, simplify feature rows and shorten Android fades. Exact optical bubble refraction and visual acceptance remain open.
- Physical-phone acceptance of this revision, original microphone driver kernel validation/signing, and installation lifecycle acceptance remain unfinished. Production release gates are false.

## Historical 0.2 evidence

Development evidence only. Protocol tests and native screen rendering do not establish selectable devices in Google Meet or Windows Camera.

## Historical 0.2 verification matrix

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

## Historical 0.2 performance

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
