# Android signing and Play Protect

Checked on 22 October 2026 for the public **0.5.0 development preview**. The published APK is already signed with Synqora's release key and is not debuggable. No replacement APK or signing-key change is needed to fix a missing signature.

## Verified release identity

| Field | Value |
| --- | --- |
| Application | `com.synqora` |
| Version | `0.5.0`, version code `5` |
| Minimum / target Android API | `29` / `35` |
| Signing certificate subject | `CN=Synqora Local Build` |
| Certificate SHA-256 | `d8c22f09ce5aec5136c6d1f1ea32915435226bbdbace8cc44abbcdb9fc3a2c86` |
| APK SHA-256 | `0dbeeba8dc447d9aefcc1b0fb690823c70766834e3a7881b23cde3c18feb21bc` |
| APK size | `3,820,057` bytes |
| Signature | APK Signature Scheme v2; verification passed |

The certificate name describes the existing developer key. It is not an Android debug key, a Google-issued approval, or a Windows code-signing certificate. The certificate fingerprint identifies the signer; the APK hash identifies one exact file. A valid signature does not by itself establish that software is harmless.

Android requires APK signatures and uses signing identity for updates. Re-signing with an unrelated key would break ordinary update compatibility. Keep the existing private key secret and backed up. [Android app-signing documentation](https://developer.android.com/studio/publish/app-signing).

## Why an installation can still be blocked

A tester reported Google's warning about sensitive data, identity theft and financial fraud. Google's developer guidance describes that warning for internet-downloaded APKs that declare certain sensitive capabilities, including notification-listener access. Synqora declares that service for its optional phone-to-PC notification feature. This is the matching documented trigger, rather than evidence of a missing APK signature. Only Google can determine the classification of this particular APK. [Google's warning guidance](https://developers.google.com/android/play-protect/warning-dev-guidance).

Turning notification sync off in Synqora does not remove the service declaration from an APK. Buying a signing certificate, changing the certificate label, or adding more APK signature schemes does not constitute Play Protect approval. This repository does not recommend disabling device protections or changing package/signing identities to evade a block.

## Sensitive capabilities and consent

| Capability | Purpose and controls in 0.5.0 |
| --- | --- |
| Notification-listener service | **Inbox → Sync notifications**, off by default, plus Android's separate notification-access approval. Relays active notification content to the connected PC. PC actions can dismiss notifications or use their actual reply/call actions. Secret notifications, group summaries and Synqora's own notifications are excluded. |
| `READ_CALL_LOG` | Optional recent cellular history, separately enabled in **Inbox → Call settings** and subject to Android permission/installer restrictions. Not needed for file transfer or notification replies. |
| `CALL_PHONE` | Separate opt-in for PC-initiated cellular dialing, plus Android permission. The phone places the call; call audio is a separate system/Bluetooth route. |
| `CAMERA`, `RECORD_AUDIO` | Features explicitly started by the user, with runtime permission and foreground-service indication. Lost connections stop capture; reconnect does not reactivate it. |
| Media permissions | Optional gallery browsing within Android's full/selected-media grant. A system picker can share individual files without enabling gallery access. |
| `POST_NOTIFICATIONS` | Synqora's own session, transfer and dial alerts. This is separate from permission to read other apps' notifications. |
| Network, foreground service, wake lock | Local encrypted device sessions and active sharing/transfers. An idle companion does not hold capture resources or a CPU wake lock. |

The notification service is exported **with** Android's `BIND_NOTIFICATION_LISTENER_SERVICE` protection so the system can bind it. Removing that protection would weaken security. The APK does not declare an accessibility service or SMS inbox-reading permissions. See the [data-handling notice](PRIVACY.md) for what is sent and retained.

## Google review

The public 0.5.0 APK was uploaded to [VirusTotal](https://www.virustotal.com/gui/file/0dbeeba8dc447d9aefcc1b0fb690823c70766834e3a7881b23cde3c18feb21bc), and an appeal was submitted through the [official Play Protect form](https://support.google.com/googleplay/android-developer/contact/protectappeals) on 22 October 2026 with the maintainer's authorization. Google displayed its submission confirmation and requested verification through the contact email. Google review remains pending; this is not installation approval. No signing keys, passwords, pairing invitations or private user messages were submitted.

VirusTotal's completed scan on that date reported **0 detections from 68 engines** for this exact APK. That result does not determine Google's separate sensitive-permission installation decision.

For future reviews, the form requests a contact email, package name, the SHA-256 of the APK uploaded to VirusTotal, and supporting information. Use the public APK only. The supporting-information field currently has a 1,000-character limit.

Explain the optional companion use, consent flow, network destination, permission purposes and revocation controls. Include the exact warning, device/Android version, install source, release URL, source URL, APK hash and signing-certificate fingerprint. The repository includes actual UI screenshots and validation limits. Submission and review do not guarantee approval. Play Store publication has its own developer verification, permission-policy and review requirements; signing an app bundle alone does not complete them.

## Reproduce the signature check

Install Android SDK build-tools and configure `JAVA_HOME` for a supported JDK. This command reads the APK and public certificate only; it neither installs the app nor accesses a private keystore:

```powershell
tools/verify_android_apk.ps1 -ApkPath dist/development/0.5.0/Synqora-0.5.0-dev-android.apk -ReportPath previews/security-review/0.5.0/signature.json
```

The verifier checks the cryptographic signature, pinned official signer, package identity and non-debuggable manifest. Release packaging runs it before copying an artifact. Developers building a fork with their own key must pass their own public fingerprint with `-ExpectedSignerSha256` to the verifier or package script. An APK signed by a fork's key cannot normally update the official build.
