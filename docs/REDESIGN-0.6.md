# 0.6 mobile redesign preview

The mobile interface follows the maintainer's November 2026 Apple Music and gallery references: black or white canvases, large headings, inset settings groups and two floating frosted bars. Color accents are monochrome. The supplied overlapping-squares/S artwork is reserved for platform icons, not app headers. No Apple artwork, music covers or gallery reference photographs are bundled.

## Interaction and materials

Device, Inbox and Files remain the three root destinations. The upper floating bar opens the real connection sheet and shows actual camera/microphone sharing state; the lower bar navigates. Feature pages retain their capture, transfer, notification and input bindings. Settings offers System, Light and Dark choices persisted on the phone. Windows keeps its existing dark acrylic shell.

Buttons use restrained interruptible spring compression. Page changes are immediate, without the previous slow menu transition. Insets and scroll padding leave room to bring the final controls above both floating bars. Reduced motion/transparency, battery saver, thermal pressure and the rendering-pressure monitor retain their fallback behavior.

On Android 13+, the dock samples a bounded 224-dp strip of the app's own content. A small RenderEffect blur precedes a rounded-edge refraction shader; foreground labels remain outside that effect. This is not arbitrary transparency into other apps. Android 10–12 and reduced-effects modes use a layered material with a highlighted edge. The effect has no continuous time animation. [Compose graphics layer documentation](https://developer.android.com/develop/ui/compose/graphics/draw/modifiers).

Both dock surfaces reuse the same blurred source layer. Short landscape windows collapse the upper connection bar. Dark/light tinting keeps navigation readable over bright or dark app content.

## Windows reopening fix

A per-user/session named mutex now owns the ordinary application lifetime. Another launch signals the owner and exits before constructing a media session. The owner's dispatcher restores and activates its existing window, preserving the running connection. Explicit automated screenshot/receiver probes use isolated identities so they cannot restore a user's window. The connection control uses a phone glyph instead of a conversation glyph.

Before testing the upgrade, quit the old 0.5 client from its tray menu. Older binaries do not participate in the new activation mechanism. Driver setup is unchanged; see [installation](INSTALLATION.md).

## Distribution limits

This remains a development preview. The original Google appeal concerned the exact 0.5 APK, not the new 0.6 artifact. Notification-listener access remains present for notification sync; an installer may still apply Play Protect restrictions. A release signature is not Google installation approval. No new appeal or external APK submission is made automatically.

Physical-phone frame times, streaming performance, long-session battery behavior and the complete installed-app upgrade lifecycle still need device acceptance. Emulator and source checks are recorded separately from those requirements.

## Validation — 1 November 2026

- Android debug/release builds, unit tests and lint completed; lint reports 34 warnings and zero errors. Windows Release builds and installer payload checks passed without build warnings.
- The signed final release APK was installed and opened on a disposable API 35 emulator. It retains the existing release signing certificate and increments the version to 0.6.0/code 6.
- Actual touch checks passed for all three root destinations, opening the connection sheet from the dock, selecting a theme, and preserving that choice after an app restart.
- Native previews cover all ten mobile screens in both themes and the Windows screens. Additional captures cover 720×1600/density 320, 1920×1080/density 360 and 1.6 font scaling. These are screenshot/layout checks, not a claim of exhaustive accessibility acceptance.
- Five separate Windows process launches signalled the original instance; none acquired a second owner. Ownership was released after exit. The restore dispatcher path compiles and Windows startup renders its screens. An interactive installed-app tray/minimize/restore check remains for the tester.
- Final release navigation measurement: 24 real taps, API 35 x86_64 SwiftShader emulator at 1080×2400/density 420; 474 frames, 68 janky frames (14.35%), p50 30 ms, p95 61 ms, p99 85 ms. The software emulator did **not** meet the 60 FPS target. This is not a physical-phone or active-streaming benchmark. Raw data: [release frame times](validation/0.6/android-release-gfxinfo.txt).
- Effective mobile text/accent palette endpoint contrast: minimum 6.11:1 dark and 5.20:1 light; composited controls also received visual review.

[Interaction results](validation/0.6/mobile-interaction.json) · [Release startup evidence](validation/0.6/release-smoke.json) · [Screenshots](SCREENSHOTS.md).
