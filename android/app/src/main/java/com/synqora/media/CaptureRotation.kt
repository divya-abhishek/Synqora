package com.synqora.media

/** Physical pose to Surface rotation, independent of the screen's rotation lock. */
internal fun captureRotation(degrees: Int, previous: Int): Int {
    if (degrees !in 0..359) return previous
    // Retain the previous pose close to quadrant boundaries to avoid oscillation.
    val centres = intArrayOf(0, 270, 180, 90)
    val centre = centres.getOrElse(previous) { 0 }
    val distance = kotlin.math.abs((degrees - centre + 540) % 360 - 180)
    if (distance <= 55) return previous
    return when (degrees) { in 45 until 135 -> 3; in 135 until 225 -> 2; in 225 until 315 -> 1; else -> 0 }
}
