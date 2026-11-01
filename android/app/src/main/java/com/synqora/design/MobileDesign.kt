package com.synqora.design

import androidx.compose.foundation.*
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.*
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.*
import com.synqora.R

/** New mobile hierarchy: centered introductions, a focal instrument, then direct actions. */
@Composable fun ScreenIntro(kicker: String, title: String, detail: String) {
    val c = LocalGlass.current
    Column(Modifier.fillMaxWidth().padding(top = 8.dp, bottom = 6.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text(kicker.uppercase(), color = c["muted"], fontSize = 10.sp, letterSpacing = 2.5.sp)
        Text(title, color = c["text"], fontSize = 34.sp, lineHeight = 39.sp, fontWeight = FontWeight.Medium, letterSpacing = (-1.2).sp, textAlign = TextAlign.Center, modifier = Modifier.semantics { heading() })
        Text(detail, color = c["muted"], fontSize = 14.sp, lineHeight = 20.sp, textAlign = TextAlign.Center)
    }
}

@Composable fun ConnectionEmblem(connected: Boolean, onClick: () -> Unit) {
    val c = LocalGlass.current
    val source = remember { MutableInteractionSource() }
    Box(Modifier.fillMaxWidth().height(184.dp), contentAlignment = Alignment.Center) {
        Box(Modifier.size(184.dp).drawWithCache {
            val glow = Brush.radialGradient(listOf(c["accent"].copy(alpha = .14f), Color.Transparent))
            onDrawBehind {
                drawCircle(glow)
                drawCircle(c["accent"].copy(alpha = .09f), radius = size.minDimension * .49f, style = Stroke(1.dp.toPx()))
                drawCircle(c["accent"].copy(alpha = if (connected) .40f else .15f), radius = size.minDimension * .39f, style = Stroke(1.dp.toPx()))
            }
        })
        Box(Modifier.size(118.dp).glassPress(source).clip(CircleShape)
            .clickable(source, null, role = Role.Button, onClick = onClick).semantics { contentDescription = "Connection details" }, contentAlignment = Alignment.Center) {
            Image(painterResource(R.drawable.synqora_brand), null, Modifier.size(106.dp))
        }
    }
}

@Composable fun ActionRow(icon: ImageVector, title: String, subtitle: String, active: Boolean = false, onClick: () -> Unit) {
    val c = LocalGlass.current
    val source = remember { MutableInteractionSource() }
    Row(Modifier.fillMaxWidth().glassPress(source).clip(RoundedCornerShape(26.dp))
        .background(Brush.linearGradient(listOf(c["elevated"].copy(alpha = .75f), c["surface"].copy(alpha = .55f))))
        .border(.7.dp, Brush.linearGradient(listOf(c["accent"].copy(alpha = .20f), c["accent"].copy(alpha = .03f))), RoundedCornerShape(26.dp))
        .clickable(source, null, role = Role.Button, onClick = onClick).padding(18.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
        Box(Modifier.size(48.dp).background(c["accent"].copy(alpha = if (active) .18f else .07f), CircleShape), contentAlignment = Alignment.Center) { GlassIcon(icon, size = 24.dp) }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) { GlassText(title, 18, bold = true); GlassText(subtitle, 12, "muted") }
        GlassIcon(Icons.Outlined.NorthEast, size = 18.dp, token = "muted")
    }
}

@Composable fun SectionCaption(label: String, trailing: String = "") {
    Row(Modifier.fillMaxWidth().padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
        GlassText(label, 18, bold = true)
        if (trailing.isNotBlank()) GlassText(trailing, 12, "muted")
    }
}
