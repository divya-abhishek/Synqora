# Installing Synqora

Download the MSI and APK from the [0.5.0 development preview](https://github.com/divya-abhishek/Synqora/releases/tag/v0.5.0-dev). Installers are preview builds; Windows signing, physical-device acceptance and the installation lifecycle are not production-validated.

## Windows

Use Windows 10 build 19041+ x64 for the client. Windows camera sharing requires Windows 11. Close Synqora and apps consuming its camera before upgrading. Run the MSI through Windows Installer. It includes the app runtimes and registers the original user-mode camera source. The portable ZIP runs the client but does not register that component.

## Android

Use Android 10 or later. Install the APK with the device's package installer and grant microphone/camera permission only for features you start. Notifications expose the active session and Disconnect action. Battery saver, thermal pressure and device-specific background restrictions can affect sustained operation. The app offers a dim-display camera control; it dims the screen rather than powering off the panel.

## Pairing

Put both devices on the same reachable local network. On Windows select **Connect phone**. On Android select **Connect a computer** or the link icon, then **Scan PC code**. A pairing link can be pasted instead. An invitation expires after ten minutes. Treat it as a secret; do not post it in issues or screenshots. No account is needed.

If pairing fails, check that the PC address is reachable, the invitation has not expired, the Windows firewall permits Synqora on the intended network, and the Wi-Fi access point does not isolate clients. No firewall rules or security settings are changed silently.

## Windows microphone

Synqora receiving phone audio is separate from making a recording device available to apps. The optional bundled VB-CABLE component provides that recording endpoint.

1. Open **Microphone → Setup → Open bundled setup folder** in Synqora.
2. Run the original **VBCABLE_Setup_x64.exe** as administrator, then restart Windows.
3. Start microphone capture on the phone. In Synqora use **Share with apps** if routing has not started automatically.
4. Select **CABLE Output (VB-Audio Virtual Cable)** in Meet, your recorder or other app. Its name is not “Synqora Microphone”.

**CABLE Input** is the playback endpoint Synqora sends audio to. **CABLE Output** is the recording endpoint other apps read. Keep your normal speakers/headphones selected as Windows output. Synqora does not silently install VB-CABLE or change device defaults. The original unsigned Synqora research audio driver is excluded.

VB-CABLE is donationware from [VB-Audio Software](https://vb-audio.com/Cable/). Its archive, licensing information, signed binaries and installer are provided unchanged. [Support its developer and review its terms](https://vb-audio.com/Services/licensing.htm).

If no microphone appears, confirm the vendor installation completed and Windows restarted. Reopen the target app's device selector and select **CABLE Output**. “Monitor on speakers” is only local playback; it does not create a microphone.

## Windows webcam

Install the MSI on Windows 11. Start Webcam on the phone; Windows sharing starts automatically. Wait for **Available to apps**, then choose **Synqora Camera** in the target app. The availability check requires enumeration and a sample from another process. If no device appears, confirm the MSI is the current version, reopen the app's selector, and check the error shown in Synqora's Webcam screen.

Do not expect the ZIP alone to register a camera. Stop camera-consuming apps before upgrading. Fit keeps the complete frame and may show bars; Fill crops edges to match the virtual output aspect ratio. Neither stretches the image. Camera modes request supported frame rates; the displayed received rate is the actual result.

## Remote input

Enable **Allow phone keyboard & pointer** on the PC. Touchpad and Keyboard are disabled until connection and permission are active. Focus the target PC window before typing. Edits and keys transmit immediately. Windows may block input into elevated apps. Disconnect revokes permission.

## Files, gallery, notifications and calls

Files stream in bounded chunks with no fixed 1 GB cap. Allow enough storage on the receiver; Android verifies an app-private staged copy before writing the destination chosen in its system save picker. PC files go to `Downloads/Synqora`. Existing files are not overwritten. Storage-provider and filesystem limits still apply.

On Android, **Files → Photos and videos** provides gallery sharing and a system picker for sharing individual items. Library access is opt-in and follows Android's full/selected-media permissions. On PC, **Photos** pages through thumbnails and saves selected originals. The app does not open the camera or repeatedly scan the library to do this.

In Android **Inbox**, turn on notification sync, grant notification access and enable Synqora alerts if you want incoming-file/dial notifications. Inline replies use the original app's current action. Android may redact content or require unlocking. Turn off sync to clear the PC's notification view. Notification access alone does not start the camera or microphone.

**Inbox → Call settings** has separate permissions for cellular history and PC-initiated dialing. Android/installer policy can restrict history. See [calling setup](CALLING.md) for the optional experimental Bluetooth MSIX, its development-certificate requirement, actual HFP implementation and unverified hardware status. Direct outgoing WhatsApp calls/private WhatsApp history are not implemented.

## Background operation and PC tray

The idle phone companion holds no camera, microphone or CPU wake lock. A lost PC/control/media connection stops capture; reconnect never restarts it automatically. Swiping away the phone app stops capture. Explicit media sharing can continue with the screen locked while the session is alive. Idle Doze delivery and long-session battery use require physical-device testing.

On Windows use **Settings → Minimize to notification area** and **Keep running when the window closes**. Both are enabled initially for companion use. The tray menu can open, connect, disconnect or quit. Closing a window can keep its live session running; use Disconnect or Quit when you want it ended. Hidden windows pause preview drawing. A taskbar-inline widget is not included.

## Logs and reporting issues

Windows startup logs are in `%LOCALAPPDATA%\Synqora\Logs\startup.log`. Report the app version, Windows/Android versions, feature, expected behavior and exact error. Review logs/screenshots before sharing; remove pairing links, addresses and personal device names. [Known validation limits](VALIDATION.md).
