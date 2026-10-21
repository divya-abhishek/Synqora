# Synqora design system

The shared source is `design/tokens.json`, exported into WinUI effective-pixel and Compose dp/sp tokens. Charcoal and pearl backgrounds, neutral text, restrained status colors and an original paired-loop mark replace the previous identity.

## Layout and interaction

Windows has a left sidebar and one centered, width-constrained workspace. It starts at 1080×820, collapses labels on smaller windows, and caps content at 900 effective pixels. Resize updates existing bounds; it does not rebuild the shell or cached pages. Each navigation button owns its selected background. There is no separate moving highlight or animated page entrance. Native title-bar controls and keyboard focus remain available.

Phone screens use a compact header, direct feature rows and flat content sections. Home and Settings are the only root destinations. Feature screens have an explicit Back control. The microphone console, camera preview, native touch surface and live IME editor remain connected to application state. No Tools, Media or Send-text screen is exposed. Both platforms center camera placeholders and use finite text columns.

Press feedback uses a restrained spring, with immediate input delivery. Hover light changes take 140 ms. Navigation changes content immediately; camera toolbar visibility uses a brief fade. Gain sliders never smooth the underlying value. Waveforms consume at most 64 sampled levels at 30 Hz. There are no decorative idle animation loops.

## Materials

Following [Apple's adopting Liquid Glass guidance](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass), glass is concentrated in navigation and controls above the content. Large content areas remain quiet; nested glass cards have been removed. This guidance describes Apple-platform APIs, not a cross-platform shader or a specification for Apple's exact physics.

Windows uses a custom SystemBackdrop wrapping DesktopAcrylicController with the Thin material, theme-specific tint/luminosity, activation/policy configuration and solid fallback. Selected controls sample the native app backdrop through a small Gaussian-blur/tint Composition effect. Rounded rims, hover light and compositor press springs provide interaction. Transformed backdrop inputs are excluded: a test of that unsupported graph caused a native compositor failure. Windows does not implement nonlinear bubble refraction.

In High effects mode, Android 13+ records a bounded 136-dp strip of the app-owned background/content into a GraphicsLayer, excluding floating controls. The floating navigation draws a local sample through an AGSL rounded-rectangle distance-field lens: the narrow edge band displaces the sample, adds a light rim and responds to pressure. Foreground text/icons remain sharp. This samples Synqora content only, not the launcher, other apps or arbitrary camera SurfaceViews. It does not use Compose blur as backdrop capture. There is no time-driven shader animation or full-screen animated blur.

Balanced mode uses lighter layered glass. Android 10–12, reduced transparency, economy, battery/thermal pressure and sustained CPU/GPU rendering stress use readable layered fallback materials. Windows follows transparency, animation, high contrast and energy-saver policy. Both expose motion, transparency and quality controls. Theme changes rebuild material definitions; navigation does not.

| Preset | Use |
| --- | --- |
| Subtle | Status pills and small secondary containers |
| Regular | Sidebar and selected primary surfaces |
| Elevated | Transient toolbars and overlays |
| Frosted | Modal content separation |
| SolidFallback | Accessibility and constrained rendering |

## Verification and limits

Native screenshots cover six screens in light/dark themes. Windows captures include 720, 1080, 1920 and 2560-pixel requested sizes, with live resize checks for horizontal bounds, exactly one selected navigation item, and retained page instances. Android captures cover portrait, smaller phones, landscape and enlarged text. A screenshot is a layout check, not proof of animation fluidity or external-device availability. Measured results and remaining acceptance work are in [VALIDATION.md](VALIDATION.md).

Apple's proprietary visual assets, exact renderer and system physics are not included. The Kube CSS/SVG experiment and user-supplied reference images informed material hierarchy and clean composition; no reference images or Figma assets are bundled. The implementations and brand geometry are original.
