# Third-party components

The Windows distribution includes Microsoft's .NET runtime, Windows App SDK / WinUI, Windows SDK projections, and Visual C++ runtime libraries under their respective licenses. The Android APK includes Kotlin, AndroidX Compose, lifecycle, activity, core, and CameraX components under their respective licenses. Runtime license and notice files provided by dependencies are retained in the Windows payload where supplied. Android dependency notices are available from the upstream projects.

- [.NET runtime notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)
- [Windows App SDK](https://github.com/microsoft/WindowsAppSDK)
- [WinUI](https://github.com/microsoft/microsoft-ui-xaml)
- [Win2D 1.4.0](https://github.com/microsoft/Win2D) — MIT; native Composition effect descriptions for small frosted controls. Its packaged license is preserved in the distribution.
- [AndroidX](https://android.googlesource.com/platform/frameworks/support/)
- [Kotlin](https://github.com/JetBrains/kotlin)
- [NAudio 2.2.1](https://github.com/naudio/NAudio) — MIT; Windows audio playback and endpoint volume.
- [QRCoder 1.6.0](https://github.com/codebude/QRCoder) — MIT; local pairing code generation.
- [ZXing core 3.5.3](https://github.com/zxing/zxing) — Apache License 2.0; local QR decoding, without a downloaded model or cloud service.

WiX Toolset is a build-time dependency used to create the MSI. It is not an application runtime dependency. The active Synqora logo is original vector geometry rendered by the project export script. The earlier generated raster master is retained but is no longer used for exports. No Apple logo, icon assets, or Figma kit assets are included.

The microphone driver development source uses Microsoft SysVAD at commit `4d4618400525f9b2ce66fa2e267f58a55043be40` under the Microsoft Public License (MS-PL), not MIT. The full license is preserved in `native/audio/LICENSE.SysVAD.txt`. The preparation recipe retains upstream copyright notices and applies Synqora's endpoint and PCM bridge changes. This unsigned development driver is excluded from current consumer packages. No WO Mic or DroidCam driver is bundled.
# VB-CABLE optional microphone component

The original **VBCABLE_Driver_Pack45.zip** is redistributed unchanged. VB-CABLE is © Vincent Burel / VB-Audio Software; it is donationware, and all participation is welcome. Vendor origin and support: https://vb-audio.com/Cable/. Licensing/distribution terms: https://vb-audio.com/Services/licensing.htm. Its original readme/license and signed binaries remain in the archive.

Synqora does not silently install this driver, rename its devices or change Windows default devices. The user runs the vendor's separate setup with administrator access and restarts Windows. Applications select **CABLE Output (VB-Audio Virtual Cable)** as their microphone. The public redistribution model requires visible vendor identification and an opportunity for users to donate; professional/enterprise distribution requires separate licensing review. Only the standard public VB-CABLE package is included, not A+B/C+D or other products.

Archive SHA-256: B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB.

