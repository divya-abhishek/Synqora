package com.synqora.design

import androidx.compose.animation.core.*
import androidx.compose.foundation.*
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.*
import androidx.compose.ui.window.Dialog

data class GlassStyle(val colors: Map<String, Color>, val dark: Boolean, val solid: Boolean, val motion: Boolean, val economy: Boolean, val rich: Boolean = false) {
    operator fun get(token: String) = colors.getValue(token)
}
val LocalGlass = staticCompositionLocalOf { GlassStyle(Tokens.dark, true, false, true, false) }

@Composable
fun SynqoraTheme(dark: Boolean, solid: Boolean, motion: Boolean, economy: Boolean, highContrast: Boolean = false, rich: Boolean = false, content: @Composable () -> Unit) {
    var colors = if (dark) Tokens.dark else Tokens.light
    if (highContrast) colors = colors + mapOf("background" to if (dark) Color.Black else Color.White, "surface" to if (dark) Color.Black else Color.White, "solid" to if (dark) Color.Black else Color.White, "text" to if (dark) Color.White else Color.Black, "muted" to if (dark) Color.White else Color.Black)
    val c = GlassStyle(colors, dark, solid || highContrast, motion, economy, rich)
    val scheme = if (dark) darkColorScheme(primary = c["accent"], onPrimary = c["onAccent"], background = c["background"], surface = c["surface"], onSurface = c["text"], onSurfaceVariant = c["muted"], outline = c["border"], secondary = c["cyan"])
    else lightColorScheme(primary = c["accent"], onPrimary = c["onAccent"], background = c["background"], surface = c["surface"], onSurface = c["text"], onSurfaceVariant = c["muted"], outline = c["border"], secondary = c["cyan"])
    CompositionLocalProvider(LocalGlass provides c) { MaterialTheme(colorScheme = scheme, content = content) }
}

@Composable
fun GlassSurface(modifier: Modifier = Modifier, material: String = "Regular", padding: Dp = 24.dp, radius: Dp? = null, content: @Composable ColumnScope.() -> Unit) {
    val c = LocalGlass.current
    val m = Tokens.materials.getValue(if (c.solid) "SolidFallback" else material)
    val shape = RoundedCornerShape(radius ?: m.radius.dp)
    val fill = if (c.solid) Brush.linearGradient(listOf(c["solid"], c["solid"])) else Brush.linearGradient(listOf(c[if (material == "Elevated") "elevated" else "surface"].copy(alpha = (m.opacity + .08f).coerceAtMost(1f)), c["surface"].copy(alpha = m.opacity)))
    val rim = if (c.solid) Brush.linearGradient(listOf(c["border"], c["border"])) else Brush.linearGradient(0f to c["text"].copy(alpha = if (c.rich) .28f else .18f), .5f to c["text"].copy(alpha = .05f), 1f to c["text"].copy(alpha = .09f))
    Column(modifier.shadow(if (c.solid || c.economy) 0.dp else m.elevation.dp, shape, ambientColor = c["background"], spotColor = c["background"]).clip(shape).background(fill).border(1.dp, rim, shape).padding(padding), verticalArrangement = Arrangement.spacedBy(16.dp), content = content)
}

@Composable fun Modifier.glassPress(source: MutableInteractionSource): Modifier {
    val c = LocalGlass.current
    val pressed by source.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) .975f else 1f, if (c.motion) spring(.88f, 520f) else snap(), label = "glass compression")
    return graphicsLayer { scaleX = scale; scaleY = scale }
}

@Composable fun GlassCard(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) = GlassSurface(modifier, "Subtle", content = content)
@Composable fun GlassText(text: String, size: Int = 14, token: String = "text", bold: Boolean = false, modifier: Modifier = Modifier) = Text(text, modifier, color = LocalGlass.current[token], fontSize = size.sp, fontWeight = if (bold) FontWeight.SemiBold else FontWeight.Normal, lineHeight = (size * 1.4).sp)
@Composable fun GlassDisplay(text: String, size: Int = 30, modifier: Modifier = Modifier) = Text(text, modifier, color = LocalGlass.current["text"], fontSize = size.sp, fontFamily = FontFamily.SansSerif, fontWeight = FontWeight.Medium, lineHeight = (size * 1.18f).sp, letterSpacing = (-.7).sp)
@Composable fun GlassIcon(icon: ImageVector, label: String? = null, size: Dp = 24.dp, token: String = "accent") = Icon(icon, contentDescription = label, modifier = Modifier.size(size), tint = LocalGlass.current[token])

@Composable
fun GlassButton(label: String, modifier: Modifier = Modifier, primary: Boolean = false, enabled: Boolean = true, loading: Boolean = false, icon: ImageVector? = null, onClick: () -> Unit) {
    val c = LocalGlass.current
    val source = remember { MutableInteractionSource() }
    OutlinedButton(onClick, modifier.glassPress(source).heightIn(min = 48.dp), enabled = enabled && !loading, shape = RoundedCornerShape(26.dp), interactionSource = source, border = BorderStroke(1.dp, c["text"].copy(alpha = .12f)), colors = ButtonDefaults.outlinedButtonColors(containerColor = if (primary) c["accent"] else c["elevated"].copy(alpha = if (c.solid) 1f else .24f), contentColor = c[if (primary) "onAccent" else "text"]), contentPadding = PaddingValues(horizontal = 18.dp, vertical = 12.dp)) {
        if (loading) { CircularProgressIndicator(Modifier.size(18.dp), color = c[if (primary) "onAccent" else "accent"], strokeWidth = 2.dp); Spacer(Modifier.width(10.dp)) }
        else if (icon != null) { Icon(icon, null, Modifier.size(20.dp)); Spacer(Modifier.width(8.dp)) }
        Text(label, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
    }
}
@Composable fun GlassIconButton(icon: ImageVector, label: String, enabled: Boolean = true, onClick: () -> Unit) {
    val c = LocalGlass.current
    val source = remember { MutableInteractionSource() }
    FilledTonalIconButton(onClick, modifier = Modifier.size(48.dp).glassPress(source).border(1.dp, c["text"].copy(alpha = .1f), RoundedCornerShape(24.dp)), enabled = enabled, interactionSource = source, shape = RoundedCornerShape(24.dp), colors = IconButtonDefaults.filledTonalIconButtonColors(containerColor = c["elevated"].copy(alpha = if (c.solid) 1f else .5f), contentColor = c["accent"])) { Icon(icon, label, Modifier.size(22.dp)) }
}
@Composable fun GlassStatusIndicator(label: String, connected: Boolean = false) {
    val c = LocalGlass.current
    Row(Modifier.clip(RoundedCornerShape(99.dp)).background(c["surface"].copy(alpha = .6f)).padding(horizontal = 12.dp, vertical = 8.dp).semantics(mergeDescendants = true) { liveRegion = LiveRegionMode.Polite }, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Box(Modifier.size(6.dp).background(c[if (connected) "success" else "muted"], RoundedCornerShape(99.dp)))
        GlassText(label, 12, if (connected) "success" else "muted")
    }
}
@OptIn(ExperimentalMaterial3Api::class)
@Composable fun GlassSlider(label: String, value: Float, onChange: (Float) -> Unit) {
    val c = LocalGlass.current
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) 1.08f else 1f, if (c.motion) spring(.9f, 520f) else snap(), label = "slider thumb pressure")
    Column {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) { GlassText(label, bold = true); GlassText("${(value * 100).toInt()}%", token = "accent") }
        Slider(value, onChange, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).semantics { contentDescription = label }, interactionSource = source,
            thumb = { Box(Modifier.size(30.dp, 24.dp).graphicsLayer { scaleX = scale; scaleY = scale }
                .shadow(if (c.economy) 0.dp else 4.dp, RoundedCornerShape(12.dp))
                .background(Brush.verticalGradient(listOf(c["accent"], c["accent"].copy(alpha = .88f))), RoundedCornerShape(12.dp))
                .border(1.dp, c["onAccent"].copy(alpha = .14f), RoundedCornerShape(12.dp))) },
            track = { position -> Canvas(Modifier.fillMaxWidth().height(6.dp)) {
                val start = androidx.compose.ui.geometry.Offset(0f, size.height / 2)
                drawLine(c["text"].copy(alpha = .14f), start, androidx.compose.ui.geometry.Offset(size.width, size.height / 2), size.height, StrokeCap.Round)
                drawLine(c["accent"], start, androidx.compose.ui.geometry.Offset(size.width * position.value, size.height / 2), size.height, StrokeCap.Round)
            } })
    }
}
@Composable fun GlassSwitch(label: String, description: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) { GlassText(label, bold = true); GlassText(description, 12, "muted") }
        Switch(checked, onChange, modifier = Modifier.semantics { contentDescription = label }, colors = SwitchDefaults.colors(checkedTrackColor = LocalGlass.current["accent"], checkedThumbColor = LocalGlass.current["onAccent"], uncheckedTrackColor = LocalGlass.current["elevated"], uncheckedBorderColor = LocalGlass.current["border"], uncheckedThumbColor = LocalGlass.current["muted"]))
    }
}
@Composable fun GlassDropdown(label: String, choices: List<String>, value: String, onChange: (String) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        GlassText(label, 12, "muted")
        Box {
            GlassButton(value, Modifier.fillMaxWidth(), onClick = { expanded = true })
            DropdownMenu(expanded, { expanded = false }, containerColor = LocalGlass.current["elevated"], shape = RoundedCornerShape(20.dp)) {
                choices.forEach { choice -> DropdownMenuItem(text = { Text(choice) }, onClick = { expanded = false; onChange(choice) }) }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable fun GlassModal(onDismiss: () -> Unit, title: String, content: @Composable ColumnScope.() -> Unit) {
    val c = LocalGlass.current
    val body: @Composable ColumnScope.() -> Unit = { GlassText(title, 26, bold = true); content(); GlassButton("Done", Modifier.fillMaxWidth(), primary = true, onClick = onDismiss) }
    if (!c.motion) {
        Dialog(onDismiss) { GlassSurface(Modifier.verticalScroll(rememberScrollState()), "Frosted", content = body) }
    } else ModalBottomSheet(onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = c["elevated"], shape = RoundedCornerShape(topStart = 30.dp, topEnd = 30.dp)) {
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).imePadding().navigationBarsPadding().padding(24.dp), verticalArrangement = Arrangement.spacedBy(20.dp), content = body)
    }
}
@Composable fun GlassDialog(title: String, text: String, onDismiss: () -> Unit) {
    AlertDialog(onDismiss, title = { Text(title) }, text = { Text(text) }, confirmButton = { TextButton(onDismiss) { Text("Done") } }, containerColor = LocalGlass.current["elevated"], shape = RoundedCornerShape(Tokens.RadiusModal.dp))
}
@OptIn(ExperimentalMaterial3Api::class)
@Composable fun GlassTooltip(label: String, content: @Composable () -> Unit) {
    TooltipBox(positionProvider = TooltipDefaults.rememberPlainTooltipPositionProvider(), tooltip = { PlainTooltip { Text(label) } }, state = rememberTooltipState(), content = content)
}
@Composable fun GlassVolumeControl(value: Float, onChange: (Float) -> Unit) = GlassSlider("Volume preference", value, onChange)
@Composable fun GlassAudioVisualizer(levels: List<Float>, modifier: Modifier = Modifier) {
    val c = LocalGlass.current
    Canvas(modifier.fillMaxWidth().height(116.dp).semantics { contentDescription = "Audio input level" }) {
        val n = levels.size.coerceAtLeast(1)
        levels.forEachIndexed { i, value ->
            val x = (i + .5f) * size.width / n
            val height = (value.coerceIn(0f, 1f) * size.height).coerceAtLeast(3.dp.toPx())
            drawLine(c[if (i % 3 == 0) "cyan" else "accent"], androidx.compose.ui.geometry.Offset(x, (size.height - height) / 2), androidx.compose.ui.geometry.Offset(x, (size.height + height) / 2), strokeWidth = 3.dp.toPx(), cap = StrokeCap.Round)
        }
    }
}
