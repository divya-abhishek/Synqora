# Veil — Synqora glass system

The source is `design/tokens.json`, exported by `tools/generate_tokens.py`. Windows uses effective pixels; Android uses dp/sp. Both share material hierarchy, semantic colors, shapes and motion timing.

| Material | Purpose | Behavior |
| --- | --- | --- |
| Subtle | Secondary cards | Quiet tint and light rim |
| Regular | Primary controls/cards | Stronger separation |
| Elevated | Device card/navigation/overlays | Luminous edge and restrained depth |
| Frosted | Waveform and touch surfaces | Strong content separation |
| SolidFallback | Accessibility/constrained rendering | Opaque theme-aware surfaces |

Dark combines charcoal, neutral translucent grey and silver. Light combines pearl white, cool grey and graphite. Colour is reserved for restrained success/error states. Semantic text passes 4.5:1 against tested background endpoints; this does not replace composited-screen or assistive technology testing.

Windows uses DesktopAcrylicBackdrop where supported with Mica fallback, selected AcrylicBrush surfaces and quieter layered cards. The default shell is 940×760 with a floating horizontal navigation pill; compact widths retain accessible icon buttons. Android uses layered tint, edge reflections and restrained shadows, with content constrained on wide displays. Compose blur is not presented as arbitrary backdrop sampling. Android 10/11 use the same layered approach. Solid/economy settings remove decorative transparency and elevation. No animated full-window blur or SVG displacement shader is shipped.

Following the user's final rejection of slow menu motion, Windows pages and selected navigation change immediately. There is no sliding page entrance. Windows button pressure uses UIElement facade compositor springs; it does not mix those properties with raw visual transforms. Hover light fades over 140 ms. Android moves its navigation lens in graphicsLayer with a short critically damped spring; page changes use 100/80 ms fades. Its slider thumb responds to pressure while the value tracks input immediately. Reduced motion uses immediate changes. Input transmission never waits for animation. Waveform publication is capped at 30 Hz with 64 samples. Camera encoding has a dedicated executor; audio/network work uses IO. Video queues retain only the newest frame. Windows coalesces gain preference writes off the UI thread.

Visual acceptance is still open. The user wants cleaner ChatGPT/Claude-like shells and actual refractive bubble optics. Current native acrylic/layered tints do not implement Apple's exact optical behavior. The final test package lowers desktop overlay/tint opacity and removes boxed feature backgrounds; it must not be described as a finished optical Liquid Glass renderer.

Home makes the paired computer the main card and offers four direct feature controls. Windows microphone setup stays above the input monitor. Camera controls float over the preview; Windows Fit/Fill framing changes the actual virtual camera output. PC input screens show real permission state and concise instructions. Android has only Home and Settings at the root, with a real touchpad and live IME keyboard inside their feature pages. No Tools or Media tab is exposed.

Windows follows OS transparency, animation, high contrast and energy saver. Android follows animation settings, exposed high text contrast, battery saver and thermal pressure. Both expose reduced motion/transparency/effects quality. Native focus/disabled states and accessible icon labels are retained. Physical device and screen reader verification remain necessary.

References:

- [Kube liquid glass experiment](https://kube.io/blog/liquid-glass-css-svg/) informed luminous rims and material hierarchy; experimental browser refraction is not a native rendering API.
- [Figma reference](https://www.figma.com/community/file/1515608601108987500/apple-liquid-glass-ios-26-control-center-ui-kit) could not be inspected in this environment. No kit assets or layouts were imported.
- Six images supplied on 21 October informed smoked black/pearl materials, fine edges and white primary controls. The layouts, icons, typography and brand remain Synqora's own implementation; no reference imagery is bundled.

No Apple logos, proprietary icons, or direct control-center copies are included.
