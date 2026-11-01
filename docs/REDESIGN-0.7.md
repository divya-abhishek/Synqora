# 0.7 — Charcoal & ivory

This mobile rebuild uses the maintainer's actual supplied logo. Its dominant cream stroke is **#FFF4D6**, paired with the requested **#171717** base. The apps expose dark appearance only. Historical light tokens remain compatibility data, not a selectable theme.

The mobile home now has a centered connection emblem, actual connection state, separate sound/video rows and compact remote-input actions. One floating Home / Files / Activity capsule replaces the stacked navigation bars. A separate QR control opens the real connection sheet. Microphone has a central mute instrument, actual sampled waveform and direct gain control. Files, activity, photos, calls, camera, touchpad, keyboard and settings have new introductions and a shared warm material hierarchy. Existing feature callbacks and permission gates remain connected.

The transparent supplied PNG sits directly in the connection halo, with no dark disc behind it. Windows uses the same transparent artwork in its title bar and multi-resolution icon. Android launcher icons have a #171717 background; the adaptive foreground is inset to fit circular and rounded masks without cropping the design. No Apple icons or reference photographs are bundled.

## Materials and motion

The functional navigation layer gets the strongest glass treatment. Content panels use less expensive warm gradients, edge highlights and solid fallbacks. A single opaque, cached radial gradient gives the canvas depth without continuous animation or two full-screen translucent paint passes. The lens reuses its RenderEffect when geometry, pressure and tint are unchanged, and creates a fresh snapshot when shader inputs change. Android 13+ can sample a bounded strip of the app's own content for the navigation lens; this does not reveal arbitrary content behind the Android app. Earlier Android versions and constrained/reduced-transparency modes use layered fallback materials.

Buttons compress with an interruptible spring. Pages switch immediately, without full-page fading or translation. An initial full-page fade was removed after a release-emulator frame test exposed excessive compositing cost. Sliders and remote input retain immediate control updates. Reduced motion skips decorative transitions. Battery, thermal and rendering-pressure policies can reduce effects. Streaming work stays in the existing media/transport pipelines.

These choices follow Apple's separation of glass controls from content and Google's guidance on adaptive layout and efficient Compose animation. This is an original cross-platform implementation, not Apple's proprietary renderer or identical optical physics. See [Apple Liquid Glass](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass), [Apple materials](https://developer.apple.com/design/human-interface-guidelines/materials), [Google mobile layout](https://developer.android.com/design/ui/mobile/guides/layout-and-content/layout-basics) and [Compose animation](https://developer.android.com/develop/ui/compose/animation/quick-guide).

## Testing and distribution

This remains a development preview. The Windows single-instance activation fix from 0.6 is retained. Camera registration, optional VB-CABLE microphone setup and experimental Bluetooth calling requirements are unchanged; see [installation](INSTALLATION.md).

The Android package keeps the existing release signing identity. Signing does not grant Play Protect approval; the earlier Google appeal concerned the exact 0.5 APK. This revision is not automatically submitted to Google or VirusTotal.

Actual build, native screen, interaction and frame-time evidence is recorded in [validation](VALIDATION.md). Physical-phone streaming, multi-hour battery behavior, installed upgrade/uninstall and external-app audio acceptance remain open. No production performance gate is passed by screenshots alone.
