# Synqora design system

The shared source is `design/tokens.json`, exported into WinUI effective-pixel and Compose dp/sp tokens. Dark charcoal backgrounds, neutral text, restrained status colors and an original paired-loop mark replace the previous identity.

## Layout and interaction

Windows has a left sidebar and one centered, width-constrained workspace. It starts at 1080×820, collapses labels on smaller windows, and caps content at 900 effective pixels. Resize updates existing bounds; it does not rebuild the shell or cached pages. Each navigation button owns its selected background. There is no separate moving highlight or animated page entrance. Native title-bar controls and keyboard focus remain available.

Phone navigation is rebuilt around Device, Inbox and Files, with two-column remote-control cards, a connection panel, and direct Settings access. Photos and call permissions have focused child pages. Pages switch immediately; there is no slide, moving bubble or full-screen transition. The phone and PC expose dark mode only. The microphone console, camera preview, native touch surface and live IME editor remain connected to application state. No Tools, Media or Send-text destination is exposed.

Press feedback uses a restrained spring, with immediate input delivery. Hover light changes take 140 ms. Navigation changes content immediately; camera toolbar visibility uses a brief fade. Gain sliders never smooth the underlying value. Waveforms consume at most 64 sampled levels at 30 Hz. There are no decorative idle animation loops.

## Materials

Following [Apple's adopting Liquid Glass guidance](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass), glass is concentrated in navigation and controls above the content. Large content areas remain quiet; nested glass cards have been removed. This guidance describes Apple-platform APIs, not a cross-platform shader or a specification for Apple's exact physics.

Windows uses a custom SystemBackdrop wrapping DesktopAcrylicController with the Thin material, theme-specific tint/luminosity, activation/policy configuration and solid fallback. Selected controls sample the native app backdrop through a small Gaussian-blur/tint Composition effect. Rounded rims, hover light and compositor press springs provide interaction. Transformed backdrop inputs are excluded: a test of that unsupported graph caused a native compositor failure. Windows does not implement nonlinear bubble refraction.

Android uses layered neutral surfaces, restrained edge highlights and press springs. The new shell does not record a background layer or run a refraction shader for bottom navigation. This removes the unused optical experiment and its rendering work. High quality strengthens rim treatment; economy/reduced-transparency uses solid materials. Windows follows transparency, animation, high contrast and energy-saver policy. Android follows animation, high text contrast, battery and thermal state. Both expose motion, transparency and quality controls.

| Preset | Use |
| --- | --- |
| Subtle | Status pills and small secondary containers |
| Regular | Sidebar and selected primary surfaces |
| Elevated | Transient toolbars and overlays |
| Frosted | Modal content separation |
| SolidFallback | Accessibility and constrained rendering |

## Verification and limits

Native screenshots cover the dark-only screens, including Inbox, Files, Photos and Calls. Windows captures include 720, 1080, 1920 and 2560-pixel requested sizes, with live resize checks for horizontal bounds, exactly one selected navigation item, and retained page instances. Android captures cover portrait, smaller phones, landscape and enlarged text. A screenshot is a layout check, not proof of animation fluidity or external-device availability. Measured results and remaining acceptance work are in [VALIDATION.md](VALIDATION.md).

Apple's proprietary visual assets, exact renderer and system physics are not included. The Kube CSS/SVG experiment and user-supplied reference images informed material hierarchy and clean composition; no reference images or Figma assets are bundled. The implementations and brand geometry are original.
