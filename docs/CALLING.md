# Calling in Synqora 0.5

The Calls screen combines Android call-log sync, explicit dial requests and an experimental Windows Bluetooth Hands-Free Profile (HFP) implementation. They have different requirements.

| Path | Requirements | Current acceptance |
| --- | --- | --- |
| Recent cellular call history | Android Inbox → Call settings → Share call history; `READ_CALL_LOG` allowed by Android and installer policy | Synthetic emulator call log reached the PC receiver |
| Direct cellular dial request | Phone opt-in and `CALL_PHONE`; configured SIM/dialer | API and permission checks implemented; no real call was placed during automated tests |
| Bluetooth PC audio and call controls | Windows 11, paired HFP-capable phone, experimental MSIX, Windows calling consent | Implemented and compiled; package installation, actual call/audio routing and hardware compatibility are **not verified** |
| WhatsApp | Source notification actions, or a call exposed by the phone/Windows HFP stack | App/device dependent; no direct WhatsApp dialing or private call-history API |

## Standard MSI / portable build

Enable call sharing on the phone. The PC can show recent cellular calls and request a call without opening the phone dialer. Select a recent call to populate the number field, then select **Call**. The phone's configured call-audio route is used. A dual-SIM device may require a default calling SIM or its own selection.

Call-log access can be restricted by the Android installer, OS or distribution policy. The app checks the actual grant; enabling the toggle alone does not give access. Synqora never requests becoming your default dialer just to read history.

## Experimental Bluetooth MSIX

Windows requires package identity and the `phoneLineTransportManagement` capability for its Bluetooth phone transport API, plus `phoneCall` for calling. The MSI/portable process cannot declare those package capabilities. The separate MSIX contains the same application with the needed manifest declarations.

1. Quit other Synqora copies through their tray menu so only one PC listener runs.
2. Review and install the **experimental calling MSIX** through Windows' normal package installation flow. The preview is signed by the accompanying development certificate; it is not publicly trusted. If you choose to test it, follow Microsoft's [test-package signing and trust instructions](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide). This project does not import a certificate, alter trust settings or install the package automatically.
3. Pair the phone in Windows Bluetooth settings. Allow calling/audio access on the devices when they ask.
4. Open **Calls → Connect Bluetooth calling**, select the paired phone and respond to Windows' calling-access prompt yourself.
5. Dial on the PC. An exposed incoming call has Answer/Decline controls; an active call has End, Mute and PC/phone audio controls. The UI reads the actual Windows call state.

The MSIX does not register the virtual-camera COM component; that component still comes from the MSI. The MSIX is a separate experimental identity, not a validated upgrade path for the MSI. Its capability declarations, preview signature and successful build are **not proof** that Windows or a specific phone will grant working HFP access.

The implementation uses `PhoneLineTransportDevice`, line watching, `PhoneCall` control and `PhoneCallAudioDevice.LocalDevice`. It does not use a fake meter, general audio-playback capture or a custom unsigned Bluetooth driver. It unregisters its phone transport when explicitly stopped/quitting; a tray-resident app can keep receiving call events.

## WhatsApp boundaries

Synqora can relay the reply/answer/decline actions WhatsApp actually places on a notification. Some devices expose VoIP calls through their hands-free stack; that must be tested on the particular phone. WhatsApp's private history is not Android's public cellular call log, and a cellular dial request does not create a WhatsApp call. Direct outgoing WhatsApp calling and its private call history are not implemented.

## References

- [Windows package capabilities](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- [Bluetooth phone transport](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonelinetransportdevice)
- [Windows phone call control](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonecall)
- [PC versus phone HFP audio routing](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.calls.phonecallaudiodevice)
- [Android Telecom call placement](https://developer.android.com/reference/android/telecom/TelecomManager#placeCall(android.net.Uri,%20android.os.Bundle))
- [Android playback-capture restrictions](https://developer.android.com/media/platform/av-capture)
