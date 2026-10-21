# Synqora preview: data handling

This technical notice describes **0.5.0 development preview**, as reviewed on 22 October 2026. It is not a Google approval or an independent security certification. Future releases may change behavior; review their source and notices before use.

## Where data goes

Synqora connects the phone directly to the PC selected through a pairing invitation on a reachable local network. Its application protocol uses TLS 1.2/1.3, pins the PC certificate from that invitation and authenticates session/data lanes. Pairing invitations contain credentials: do not post them in public issues, logs or screenshots. The application does not use a Synqora cloud relay, analytics SDK or advertising service for these features.

The connected PC receives the data for features you enable. Anyone with access to that PC may see its displayed or saved content. Encryption in transit does not protect data already displayed or saved on either device. Network addresses and device names are used for the connection. Android, Windows, your chosen file provider and third-party calling/messaging apps have their own data handling.

## Optional sharing

- **Notifications:** off by default. Enable sync in **Inbox** and separately grant Android notification access. Synqora sends active notification app names, titles, text, timestamps and available reply/call action labels to the connected PC. Notification content can contain private messages or codes. Secret notifications, group summaries and Synqora's own notifications are excluded; this is not a promise to identify every sensitive message. Source-app action tokens remain on the phone. Replies, dismissals and available call actions require a current PC command and are subject to Android/source-app restrictions.
- **Photos and videos:** gallery sharing is off by default and requires Android media access. When enabled, PC requests retrieve pages of metadata and thumbnails. Saving an original requests a file transfer. Android's selected-media grant limits what is visible. The individual-file picker is available without broad gallery access.
- **Call history and dialing:** separate opt-ins and Android grants. History requests return up to 50 recent cellular records, including numbers, cached names, times, durations and call types. PC dialing asks the phone's system calling API to place a call. Synqora does not read private WhatsApp call history or implement direct outgoing WhatsApp calling.
- **Camera and microphone:** require feature start and Android permission. Capture uses a foreground service. Connection loss, capture failure or task removal stops capture; reconnect does not start it automatically. Physical-device battery and background behavior remain under validation.
- **Files and remote input:** files are explicitly selected or accepted. Input is transmitted to the paired PC only when its remote-input permission is enabled. Review the destination before sending sensitive data or typing.

## Storage and retention

Feature preferences are stored in Android's app-private preferences. The notification bridge keeps its current action map in memory; the PC notification view is held in memory and cleared when sync is disabled or the session disconnects. These actions cannot erase content already copied, captured or saved elsewhere.

Android verifies incoming files in an app-private temporary file, deletes that staging file when the operation ends, and writes accepted content to the destination selected in the system picker. PC downloads are saved in `Downloads/Synqora`. Saved files remain until you delete them. Gallery thumbnails use a bounded memory cache on the phone and are cleared when gallery sharing is turned off or the session disconnects. The phone's application manifest disables Android app backup.

Windows writes local startup diagnostics to `%LOCALAPPDATA%\Synqora\Logs\startup.log`. The app does not automatically upload that file. Diagnostic tools, OS backups, crash reporting configured outside the app and user-shared screenshots can retain additional information. Review and redact reports before sharing them.

## Stop sharing or delete local data

Turn off **Sync notifications**, gallery sharing, call history and PC dialing in the phone app as appropriate. Revoke notification access and camera, microphone, media and call permissions in Android Settings. **Disconnect** ends the active device session; stop camera/microphone sharing when finished. Previously enabled feature preferences can remain enabled for your next deliberately paired session.

Android Settings → Apps → Synqora → Storage provides controls to clear app data; uninstalling also removes app-private data. Files saved to user-selected locations or the PC download folder must be deleted separately. Clearing or uninstalling Synqora does not delete files held by other apps.

For technical questions, use the [public issue tracker](https://github.com/divya-abhishek/Synqora/issues), without posting private data, credentials or message content. This preview does not yet provide a private support contact or a reviewed Play Store privacy/Data safety declaration. See [installation and Play Protect review](ANDROID-SECURITY.md).
