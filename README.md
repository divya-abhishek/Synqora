# Synqora 0.3 — Veil

The application source is [MIT licensed](LICENSE). The separate `native/audio/` research component and `tools/prepare_audio.py` are under [MS-PL](native/audio/LICENSE.SysVAD.txt), as stated in that component's README. Third-party libraries and the optional VB-CABLE archive retain their own licenses; see [third-party notices](THIRD-PARTY-NOTICES.md).

Native WinUI 3 and Jetpack Compose apps for using a phone as a PC microphone, camera, touchpad and live keyboard. Neutral charcoal/pearl materials, lighter desktop acrylic, immediate Windows navigation, spring press feedback, light/dark themes and an original padded silver mark. Exact optical Apple refraction is not implemented.

These are **development review builds**. Actual emulator capture, screen-off streaming, live IME edits, raw gestures, pinned TLS and the native Windows receiver pass integration tests. The original microphone driver still needs validation/signing; physical-phone acceptance of this revision, optical glass rendering and frame-time targets remain unfinished. See [validation](docs/VALIDATION.md).

## Install and connect

1. Download the 0.3.0 MSI and signed APK from [GitHub Releases](https://github.com/divya-abhishek/Synqora/releases). Close Synqora and camera-consuming apps before upgrading. The MSI bundles app runtimes and the original camera component. Windows 10 build 19041+ x64 runs the client; camera sharing requires Windows 11.
2. On the PC, open **Connection details**. On the phone, open the devices icon and **Scan PC code**, or paste the pairing link. Use a reachable local network. Invitations expire after ten minutes.
3. For microphone use in Meet, open **Microphone → Setup** once. Run the bundled vendor's separate x64 setup as administrator, restart Windows, and select **CABLE Output (VB-Audio Virtual Cable)** in recording apps. Keep normal speakers/headphones as Windows output if vendor setup changes it. [VB-CABLE](https://vb-audio.com/Cable/) is VB-Audio donationware; original origin, license and donation opportunity are preserved. Synqora does not silently install it or change defaults.
4. Start the phone camera. The PC attempts publication as **Synqora Camera** and reports availability only after another process enumerates and captures a sample through Windows. Upgrade the old installed DLL with the MSI before accepting Meet/Camera behavior. The portable ZIP does not register it.
5. Enable remote input on the PC. Keyboard edits and keys go live to the focused PC app; raw touch samples are sent immediately. Disconnect revokes permission. Windows can block input into elevated applications.

The signed microphone component has not been installed in this development environment. The previous build was observed displaying video in Meet and Windows Camera; the current flicker/orientation fixes require fresh physical testing. External-app microphone capture remains unverified. The original unsigned SysVAD research driver is excluded. See [device integration](docs/DEVICE-INTEGRATION.md).

## Behavior

No cloud account or internet service is in the connection path. A random invitation and SHA-256 certificate pin authenticate TLS 1.2/1.3. Separate bounded control, PCM and JPEG lanes prevent video backpressure from holding input/audio.

A native InputConnection handles live composition, replacement, deletion, Enter/Tab, navigation and shortcuts, without a Send action. Native touch history and fractional pointer accumulation preserve small high-rate movement.

Reusable YUV/NV21 buffers and physical orientation metadata replace per-frame rotated RGB bitmaps. Camera modes request up to 60 FPS when supported, showing actual phone/received/decoded rates. The emulator measured about 20–23 FPS; sustained 60 FPS is not accepted. A foreground session service keeps capture and connection alive when the Activity backgrounds or the display sleeps; its notification provides Disconnect. Webcam also provides a dim-display control. Windows camera preferences offer Fit/Fill framing, and the native reader retains a complete frame during producer contention to prevent black flashes. Consumer USB setup and automatic reconnect remain unfinished.

## Build and verify

Windows uses .NET 8 / Windows App SDK 1.8. Build native camera code from an x64 Visual Studio developer prompt with the Windows SDK installed, or provide the local setup script under .local/native-toolchain/msvc/setup_x64.bat. Android needs JDK 17+ and SDK 35; set ANDROID_HOME or ignored android/local.properties. Debug builds need no signing secret. For a signed release, provide your own ignored android/signing.properties and keystore; the project's private signing key is not distributed.

```powershell
tools/build_camera.cmd
dotnet build windows/Synqora/Synqora.csproj -c Release
dotnet run --project tests/BackendProbe -c Release
android/gradlew.bat -p android assembleDebug lintDebug testDebugUnitTest
android/gradlew.bat -p android connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=com.synqora.NavigationTest
tools/test_backend.ps1 -Adb PATH_TO_ADB
tools/test_windows_receiver.ps1 -Adb PATH_TO_ADB
python tools/generate_tokens.py --check
python tools/check_design.py
```

Input tests use an isolated sink; native receiver testing denies desktop input. Pairing secrets, signing material and camera test pixels stay in ignored .local/. Preserve the private signing key for updates; never distribute it.

## Design, previews and packaging

- [Design system](docs/DESIGN.md), from design/tokens.json with generated C# and Kotlin tokens.
- [Original vector logo](brand/README.md), Windows ICO and Android launchers.
- Native captures can be generated locally with the commands below; screenshots and local test telemetry are excluded from source control.
- [Measured validation](docs/VALIDATION.md), including remaining jank and hardware limits.

```powershell
python tools/capture_android.py --adb PATH_TO_ADB --matrix
tools/capture_windows.ps1 -Exe ABSOLUTE_EXE_PATH -Matrix
python tools/make_gallery.py
tools/package_windows.ps1 -Development
tools/package_android.ps1 -Development
```

Android capture temporarily changes emulator viewport/fonts and resets test preferences. Windows captures use RenderTargetBitmap without desktop input. Packaging checks the WinUI resource index and all six screens in both themes from the publish and extracted MSI. Startup logs use %LOCALAPPDATA%\Synqora\Logs\startup.log.

Production packaging is gated by release-gates.json. Review builds are installable, but the signed microphone component still requires its separate vendor setup and restart. Clean-machine offline installation, upgrade/uninstall, physical streaming, external device capture and frame-time targets must pass before final release.
