package com.synqora.media

import org.junit.Assert.assertEquals
import org.junit.Test

class CaptureRotationTest {
    @Test fun physical_landscape_does_not_depend_on_display_rotation() {
        assertEquals(3, captureRotation(90, 0))
        assertEquals(1, captureRotation(270, 0))
        assertEquals(2, captureRotation(180, 0))
        assertEquals(0, captureRotation(0, 2))
    }
    @Test fun unknown_pose_and_boundary_noise_keep_last_rotation() {
        assertEquals(3, captureRotation(-1, 3))
        assertEquals(0, captureRotation(50, 0))
        assertEquals(3, captureRotation(50, 3))
        assertEquals(3, captureRotation(60, 0))
        assertEquals(0, captureRotation(30, 3))
    }
}
