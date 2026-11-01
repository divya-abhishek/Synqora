package com.synqora.design

import android.os.Build
import android.graphics.RuntimeShader
import androidx.annotation.RequiresApi
import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.interaction.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.*
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.graphics.layer.*
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.unit.*

/** Captures only our own content. Controls are outside this layer, avoiding feedback. */
@Stable class GlassBackdrop(val layer: GraphicsLayer) {
    init { layer.renderEffect = BlurEffect(14f, 14f, TileMode.Clamp) }
    var origin = Offset.Zero
    var bandTop = 0f
}
val LocalBackdrop = staticCompositionLocalOf<GlassBackdrop?> { null }
fun Modifier.recordBackdrop(backdrop: GlassBackdrop) = onGloballyPositioned { backdrop.origin = it.positionInRoot() }
    .drawWithContent {
        // The only optical control is at the bottom. Record a bounded strip,
        // avoiding a full-screen offscreen target during every press or scroll.
        val height = minOf(size.height.toInt(), 224.dp.roundToPx())
        backdrop.bandTop = size.height - height
        backdrop.layer.record(size = IntSize(size.width.toInt(), height)) {
            clipRect { translate(0f, -backdrop.bandTop) { this@drawWithContent.drawContent() } }
        }
        drawContent()
    }

/** Small control-only lens. API 29–32 and reduced-effects mode use the same readable rim. */
@Composable fun GlassLens(modifier: Modifier = Modifier, source: MutableInteractionSource? = null, radius: Dp = 32.dp, content: @Composable BoxScope.() -> Unit) {
    val c = LocalGlass.current
    val backdrop = LocalBackdrop.current
    var position by remember { mutableStateOf(Offset.Zero) }
    val pressed = source?.collectIsPressedAsState()?.value ?: false
    val pressure by animateFloatAsState(if (pressed) 1f else 0f, if (c.motion) spring(.92f, 800f) else snap(), label = "lens pressure")
    val shape = RoundedCornerShape(radius)
    val optical = Build.VERSION.SDK_INT >= 33 && c.rich && !c.solid && !c.economy && backdrop != null
    Box(modifier.shadow(if (c.solid || c.economy) 0.dp else 10.dp, shape).onGloballyPositioned { position = it.positionInRoot() }.clip(shape)
        .background(c["solid"].copy(alpha = if (c.solid) 1f else if (optical) .12f else .90f))
        .border(1.dp, Brush.linearGradient(listOf(c["text"].copy(alpha = .26f), c["text"].copy(alpha = .035f), c["text"].copy(alpha = .14f))), shape)) {
        if (optical) OpticalLayer(backdrop!!, position, pressure, radius, Modifier.matchParentSize())
        content()
    }
}

@RequiresApi(33)
@Composable private fun OpticalLayer(backdrop: GlassBackdrop, position: Offset, pressure: Float, radius: Dp, modifier: Modifier) {
    val c = LocalGlass.current
    val shader = remember { runCatching { RuntimeShader(LENS_SHADER) }.getOrNull() }
    if (shader == null) { Box(modifier.background(c["solid"])); return }
    val effects = remember(shader) { LensEffectCache(shader) }
    val radiusPx = with(androidx.compose.ui.platform.LocalDensity.current) { radius.toPx() }
    val tint = c["elevated"]
    Canvas(modifier.graphicsLayer {
        renderEffect = effects.snapshot(size.width, size.height, radiusPx, pressure, tint.copy(alpha = if (c.dark) .76f else .84f))
        clip = true
    }) {
        val offset = position - backdrop.origin - Offset(0f, backdrop.bandTop)
        translate(-offset.x, -offset.y) { drawLayer(backdrop.layer) }
    }
}

/** RenderEffect snapshots shader uniforms. Reuse only when those inputs match. */
@RequiresApi(33)
private class LensEffectCache(private val shader: RuntimeShader) {
    private var effect: androidx.compose.ui.graphics.RenderEffect? = null
    private var width = -1f
    private var height = -1f
    private var radius = -1f
    private var pressure = -1f
    private var tint = Color.Unspecified

    fun snapshot(w: Float, h: Float, r: Float, p: Float, color: Color): androidx.compose.ui.graphics.RenderEffect {
        effect?.let { if (width == w && height == h && radius == r && pressure == p && tint == color) return it }
        shader.setFloatUniform("bounds", w, h)
        shader.setFloatUniform("radius", r)
        shader.setFloatUniform("pressure", p)
        shader.setFloatUniform("tint", color.red, color.green, color.blue, color.alpha)
        val next = android.graphics.RenderEffect.createRuntimeShaderEffect(shader, "scene").asComposeRenderEffect()
        width = w; height = h; radius = r; pressure = p; tint = color; effect = next
        return next
    }
}

// Rounded-rectangle distance field bends sampled app content around the rim.
// There is no continuous time uniform, no camera sampling and no effect on foreground text.
private const val LENS_SHADER = """
uniform shader scene;
uniform float2 bounds;
uniform float radius;
uniform float pressure;
uniform float4 tint;
half4 main(float2 p) {
    float2 halfSize = bounds * 0.5;
    float r = min(radius, min(halfSize.x, halfSize.y));
    float2 local = p - halfSize;
    float2 q = abs(local) - (halfSize - r);
    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
    float2 outward = max(q, 0.0);
    if (length(outward) < 0.001) outward = q.x > q.y ? float2(1.0, 0.0) : float2(0.0, 1.0);
    float2 normal = normalize(outward) * sign(local);
    float edge = pow(clamp(1.0 + d / 14.0, 0.0, 1.0), 2.0);
    float2 samplePoint = clamp(p - normal * edge * (7.0 + pressure * 4.0), float2(0.5), bounds - 0.5);
    half4 color = scene.eval(samplePoint);
    float light = pow(clamp(1.0 + d / 2.8, 0.0, 1.0), 2.0) * (0.08 + 0.13 * max(0.0, -normal.y));
    return half4(mix(color.rgb, half3(tint.rgb), tint.a) + half3(light), 1.0);
}
"""
