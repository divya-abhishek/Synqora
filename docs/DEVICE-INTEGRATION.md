# Windows device integration — 0.2

The phone stream and a selectable Windows device are separate parts. Speaker monitoring does not create a microphone endpoint; preview does not establish Windows camera enumeration.

## Microphone

AudioRecord sends 48 kHz / 16-bit mono PCM through authenticated TLS. Windows gain/mute and metering run independently.

The app supports the original research endpoint if present, otherwise a positively identified **VB-CABLE** render/recording pair. WASAPI shared playback, bounded buffering and stale/disconnect clearing keep work off the UI. It does not use arbitrary drivers or change defaults. Speaker monitoring refuses the cable as default output to prevent duplicate routing/feedback.

Open **Microphone → Set up Windows microphone**. Synqora verifies/extracts the unchanged vendor archive and opens its folder. Run the separate x64 setup as administrator, restart, then select **CABLE Output (VB-Audio Virtual Cable)** in Meet/recording apps. Keep normal speakers/headphones as Windows output if vendor setup changes it. The bundled package needs no setup-time download.

[VB-CABLE](https://vb-audio.com/Cable/) is VB-Audio donationware. Origin, license and donation opportunity are preserved. The standard package's Win10 x64 catalogue verified as Microsoft Windows Hardware Compatibility Publisher; setup/SYS signatures verified. This does not establish actual installation or capture. Professional/enterprise redistribution has separate [licensing considerations](https://vb-audio.com/Services/licensing.htm). No silent/custom vendor installation procedure is integrated.

No VB-CABLE endpoint is installed in this development environment; independent WASAPI/Meet capture is pending. The original SysVAD research driver compiles but is unsigned, unloaded and excluded. It requires Microsoft production signing and kernel/HLK validation for normal distribution; see native/audio/README.md.

## Camera

CameraX encodes JPEG from reusable YUV/NV21 buffers and sends orientation metadata on bounded TLS. Windows decodes/rotates off the UI thread and publishes real pixels through the original native Media Foundation source. It offers 1280×720 NV12/RGB32 at 30/60 FPS, letterboxes input and clears stale/disconnected pixels. A private ACL-protected file bridges processes without pairing secrets.

The MSI registers Synqora's original CLSID. Windows 11 session-camera publication starts with phone capture. **Synqora Camera** is only reported available after Synqora.CameraCheck.exe independently enumerates it and reads a sample. Errors are visible. Windows 10 supports receiving/preview; camera compatibility remains unfinished.

The activator destructor formerly called ShutdownObject, so Frame Server could receive a shut-down source. A regression reproduces MF_E_SHUTDOWN on the old implementation and passes with the repair. The installed old DLL still fails publication; tools have not replaced it. Upgrade with the prepared MSI before accepting Meet/Camera behavior.

Native 60 FPS output can repeat older pixels; it is not a unique-frame sensor-rate claim. Phone modes negotiate supported AE ranges and display target/encoded/received rates. Physical high-rate capture and external consumers remain unverified.

## Input and acceptance

Raw MotionEvent history and fractional accumulation feed control TLS immediately. Native IME edits, keys and modifiers are live. PC permission gates all input; disconnect revokes it. Tests use an isolated sink without touching the desktop.

Remaining: user/admin installation/reboot, external camera/mic enumeration/capture, multiple consumers, Frame Server access, privacy denial, audible fidelity, physical quality, Wi-Fi/A/V behavior, power/thermal transitions and install/upgrade/uninstall.

Earlier temporary registration testing was rejected by automatic approval review. No workaround, unsigned driver installation, test-signing, Secure Boot change, purchase or silent vendor setup occurred.

References: [MFCreateVirtualCamera](https://learn.microsoft.com/en-us/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera), [Microsoft camera sample](https://github.com/microsoft/Windows-Camera/tree/master/Samples/VirtualCamera), [SysVAD](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/sysvad-virtual-audio-device-driver-sample/), [driver signing](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-signing-offerings). WO Mic/DroidCam are behavior references; none of their binaries, protocols or assets are copied.

