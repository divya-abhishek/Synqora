# Synqora camera source

Original C++ Media Foundation source and Windows 11 session-camera publisher. It uses Windows SDK interfaces and WRL; no WO Mic, DroidCam or Apple implementation/assets are included. Microsoft’s camera sample was read as an interface-contract reference.

Build with tools/build_camera.cmd using the local portable MSVC/SDK. The DLL uses the static C runtime. tools/verify_native.ps1 runs isolated COM activation, DLL lifetime, source-reader negotiation, timestamps, aspect ratio, stale/clear and file-lifetime checks. These tests do not register a camera or establish external recording-app compatibility.

The writer publishes BGRA into a private, bounded pixel file, with user/SYSTEM access and Local Service read access. A sequence guard rejects torn frames; input expires after 750 ms. The source emits 1280×720 NV12 or RGB32 at up to 15 fps and letterboxes source frames. It outputs black when the producer is absent. Media requests and buffers are bounded, and production does not depend on the XAML preview.

Stable original source CLSID: {57FD6F94-9413-4E47-A4CA-C0AFD738D152}. The development MSI registers only this identity. Share with Windows apps creates a current-user, session-lifetime software camera on a dedicated COM/MF worker. Windows 10 still supports receiving/preview; the publishing API requires Windows 11.

CameraDeviceProbe.exe is prepared to check actual OS enumeration and a separate capture process after controlled installation. Temporary COM registration testing was rejected by automatic approval review (“blocked by policy”); no registration workaround was performed. External activation, Frame Server file access, privacy denial, multiple clients and installer lifecycle remain acceptance gates.
