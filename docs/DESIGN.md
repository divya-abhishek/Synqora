# Synqora design system

The shared source is `design/tokens.json`, exported into WinUI effective-pixel and Compose dp/sp tokens. The current palette is #171717 charcoal and #FFF4D6 ivory sampled from the supplied logo. The actual PNG is authoritative; Windows uses transparent stroke artwork and Android launcher icons add a charcoal background.

## Layout and interaction

Windows has a left sidebar and one centered, width-constrained workspace. It starts at 1080×820, collapses labels on smaller windows, and caps content at 900 effective pixels. Resize updates existing bounds; it does not rebuild the shell or cached pages. Each navigation button owns its selected background. There is no separate moving highlight or animated page entrance. Native title-bar controls and keyboard focus remain available.

Phone navigation uses one floating Home / Files / Activity capsule and a separate QR button. A centered connection emblem, sound/video action rows, compact touchpad/keyboard controls and direct Settings access replace the former dashboard. Photos and call permissions have focused child pages. Pages switch immediately, without full-page fading or translation. The phone and PC expose dark mode only. The microphone console, camera preview, native touch surface and live IME editor remain connected to application state. No Tools, Media or Send-text destination is exposed.

Press feedback uses a restrained spring, with immediate input delivery. Hover light changes take 140 ms. Navigation changes content immediately; camera toolbar visibility uses a brief fade. Gain sliders never smooth the underlying value. Waveforms consume at most 64 sampled levels at 30 Hz. There are no decorative idle animation loops.

## Materials

Following [Apple's adopting Liquid Glass guidance](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass), glass is concentrated in navigation and controls above the content. Large content areas remain quiet; nested glass cards have been removed. This guidance describes Apple-platform APIs, not a cross-platform shader or a specification for Apple's exact physics.

Windows uses a custom SystemBackdrop wrapping DesktopAcrylicController with the Thin material, theme-specific tint/luminosity, activation/policy configuration and solid fallback. Selected controls sample the native app backdrop through a small Gaussian-blur/tint Composition effect. Rounded rims, hover light and compositor press springs provide interaction. Transformed backdrop inputs are excluded: a test of that unsupported graph caused a native compositor failure. Windows does not implement nonlinear bubble refraction.

Android uses stationary warm radial gradients, layered surfaces, edge highlights and press springs. On supported Android 13+ devices the navigation lens samples only a bounded strip of app content. Foreground labels are drawn separately. Earlier versions, economy and reduced-transparency modes use layered or solid fallbacks; this is not transparency through other apps. Windows follows transparency, animation, high contrast and energy-saver policy. Android follows animation, high text contrast, battery and thermal state. Both expose motion, transparency and quality controls.

| Preset | Use |
| --- | --- |
| Subtle | Status pills and small secondary containers |
| Regular | Sidebar and selected primary surfaces |
| Elevated | Transient toolbars and overlays |
| Frosted | Modal content separation |
| SolidFallback | Accessibility and constrained rendering |

## Verification and limits

Native screenshots cover the dark-only screens, including Inbox, Files, Photos and Calls. Windows captures include 720, 1080, 1920 and 2560-pixel requested sizes, with live resize checks for horizontal bounds, exactly one selected navigation item, and retained page instances. Android captures cover portrait, smaller phones, landscape and enlarged text. A screenshot is a layout check, not proof of animation fluidity or external-device availability. Measured results and remaining acceptance work are in [VALIDATION.md](VALIDATION.md).

Apple's proprietary visual assets, exact renderer and system physics are not included. The Kube CSS/SVG experiment and user-supplied reference images informed material hierarchy and clean composition; no reference images or Figma assets are bundled. The implementations are original; logo exports use the maintainer-supplied PNG. See [0.7 design notes](REDESIGN-0.7.md).
