package com.synqora.input

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.os.SystemClock
import android.text.Editable
import android.text.Selection
import android.text.SpannableStringBuilder
import android.text.InputType
import android.text.StaticLayout
import android.text.Layout
import android.text.TextPaint
import android.view.*
import android.view.inputmethod.*
import com.synqora.data.SynqoraSession
import kotlin.math.abs
import kotlin.math.hypot

/** Rebuild text layout only when content, available width or font scale changes. */
private class CenteredLabel {
    private var layout: StaticLayout? = null
    private val textPaint = TextPaint(Paint.ANTI_ALIAS_FLAG)
    private var content = ""
    fun draw(canvas: Canvas, text: String, width: Int, height: Int, padding: Int, size: Float, color: Int) {
        val available = (width - padding * 2).coerceAtLeast(1)
        if (content != text || layout?.width != available || textPaint.textSize != size) {
            content = text; textPaint.textSize = size
            layout = StaticLayout.Builder.obtain(text, 0, text.length, textPaint, available).setAlignment(Layout.Alignment.ALIGN_CENTER).setIncludePad(false).build()
        }
        textPaint.color = color
        layout?.let { canvas.save(); canvas.translate(padding.toFloat(), (height - it.height) / 2f); it.draw(canvas); canvas.restore() }
    }
}

/** Raw MotionEvent history is sent immediately; drawing is independently vsync bounded. */
class RawTouchpadView(context: Context) : View(context) {
    lateinit var session: SynqoraSession
    var ready = false
    var dark = true
    var haptics = true
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val label = CenteredLabel()
    private var positions = emptyMap<Int, Pair<Float, Float>>()
    private var moved = false
    private var multi = false
    private var travel = 0f
    private var active = false
    private var xTouch = 0f
    private var yTouch = 0f
    init { isClickable = true; contentDescription = "Remote touchpad"; importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_YES }
    private fun report(event: MotionEvent, history: Int = -1, excluded: Int = -1) {
        val current = (0 until event.pointerCount).filter { it != excluded }.associate { i ->
            event.getPointerId(i) to if (history >= 0) event.getHistoricalX(i, history) to event.getHistoricalY(i, history) else event.getX(i) to event.getY(i)
        }
        if (current.keys == positions.keys && current.isNotEmpty()) {
            val dx = current.entries.sumOf { (id, xy) -> (xy.first - positions.getValue(id).first).toDouble() }.toFloat() / current.size
            val dy = current.entries.sumOf { (id, xy) -> (xy.second - positions.getValue(id).second).toDouble() }.toFloat() / current.size
            travel += hypot(dx, dy)
            moved = moved || travel > ViewConfiguration.get(context).scaledTouchSlop
            if (ready && (dx != 0f || dy != 0f)) {
                val density = resources.displayMetrics.density
                if (current.size > 1) session.scroll(-dy / density * 4f)
                else session.pointer(dx / density * 1.8f, dy / density * 1.8f)
            }
        }
        positions = current
        current.values.firstOrNull()?.let { xTouch = it.first; yTouch = it.second }
    }
    override fun onTouchEvent(event: MotionEvent): Boolean {
        parent?.requestDisallowInterceptTouchEvent(true)
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> { moved = false; multi = false; travel = 0f; active = true; positions = emptyMap(); report(event) }
            MotionEvent.ACTION_POINTER_DOWN -> { multi = true; report(event) }
            MotionEvent.ACTION_MOVE -> { for (i in 0 until event.historySize) report(event, i); report(event) }
            MotionEvent.ACTION_POINTER_UP -> report(event, excluded = event.actionIndex)
            MotionEvent.ACTION_UP -> { report(event); active = false; positions = emptyMap(); if (!moved) { if (multi) { if (ready) session.click(true) } else performClick() }; parent?.requestDisallowInterceptTouchEvent(false) }
            MotionEvent.ACTION_CANCEL -> { active = false; positions = emptyMap(); parent?.requestDisallowInterceptTouchEvent(false) }
        }
        postInvalidateOnAnimation()
        return true
    }
    override fun performClick(): Boolean {
        super.performClick()
        if (ready) { session.click(false); if (haptics) performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP) }
        return true
    }
    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val dp = resources.displayMetrics.density
        paint.color = if (dark) 0xff777b7e.toInt() else 0xff777c80.toInt()
        paint.alpha = 28
        val step = 24 * dp
        var y = step
        while (y < height) { var x = step; while (x < width) { canvas.drawCircle(x, y, .7f * dp, paint); x += step }; y += step }
        paint.alpha = 255; paint.textAlign = Paint.Align.CENTER; paint.textSize = android.util.TypedValue.applyDimension(android.util.TypedValue.COMPLEX_UNIT_SP, 14f, resources.displayMetrics)
        paint.color = if (dark) 0xffb9bdc0.toInt() else 0xff53575b.toInt()
        label.draw(canvas, if (ready) "One finger to move · two to scroll" else "Connect your PC to use the touchpad", width, height, (24 * dp).toInt(), paint.textSize, paint.color)
        if (active) { paint.alpha = 35; canvas.drawCircle(xTouch, yTouch, 22 * dp, paint); paint.alpha = 220; canvas.drawCircle(xTouch, yTouch, 3 * dp, paint) }
    }
}

/** A real IME editor. Composition, deletion, Enter and shortcuts go to the PC as they happen. */
class LiveKeyboardView(context: Context) : View(context) {
    lateinit var session: SynqoraSession
    var dark = true
    private var ready = false
    private val text = SpannableStringBuilder()
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val label = CenteredLabel()
    private var editor: InputConnection? = null
    init { isFocusableInTouchMode = true; isFocusable = true; isClickable = true; contentDescription = "Live keyboard" }
    fun configure(enabled: Boolean, focus: Boolean) {
        val changed = ready != enabled
        ready = enabled
        if (!ready && changed) { text.clear(); clearFocus(); inputManager.hideSoftInputFromWindow(windowToken, 0) }
        if (ready && focus && changed) post { requestFocus(); inputManager.showSoftInput(this, InputMethodManager.SHOW_IMPLICIT) }
        invalidate()
    }
    private val inputManager get() = context.getSystemService(InputMethodManager::class.java)
    override fun onDetachedFromWindow() {
        if (inputManager.isActive(this)) inputManager.hideSoftInputFromWindow(windowToken, 0)
        editor = null; super.onDetachedFromWindow()
    }
    fun sendKey(name: String, modifiers: Int = 0) {
        if (!ready) return
        editor?.finishComposingText(); session.key(name, modifiers)
        text.clear(); Selection.setSelection(text, 0); inputManager.restartInput(this)
    }
    override fun onCheckIsTextEditor() = true
    override fun performClick(): Boolean { super.performClick(); if (ready) { requestFocus(); inputManager.showSoftInput(this, InputMethodManager.SHOW_IMPLICIT) }; return true }
    override fun onTouchEvent(event: MotionEvent): Boolean { if (event.actionMasked == MotionEvent.ACTION_UP) performClick(); return true }
    override fun onCreateInputConnection(info: EditorInfo): InputConnection {
        info.inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
        info.imeOptions = EditorInfo.IME_FLAG_NO_EXTRACT_UI or EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING
        info.initialSelStart = text.length; info.initialSelEnd = text.length
        return object : BaseInputConnection(this, true) {
            override fun getEditable(): Editable = text
            private fun change(action: () -> Boolean): Boolean {
                if (!ready) return false
                val old = text.toString(); val cursor = Selection.getSelectionEnd(text).takeIf { it >= 0 } ?: text.length
                val result = action()
                val nextCursor = Selection.getSelectionEnd(text).takeIf { it >= 0 } ?: text.length
                liveEdits(old, text.toString(), cursor, nextCursor).forEach { session.edit(it.move, it.before, it.after, it.text) }
                return result
            }
            override fun commitText(value: CharSequence?, cursor: Int) = change { super.commitText(value, cursor) }
            override fun setComposingText(value: CharSequence?, cursor: Int) = change { super.setComposingText(value, cursor) }
            override fun setSelection(start: Int, end: Int) = change { super.setSelection(end, end) }
            override fun deleteSurroundingText(before: Int, after: Int): Boolean {
                if (text.isEmpty() && ready) { repeat(before.coerceAtMost(256)) { session.key("backspace") }; repeat(after.coerceAtMost(256)) { session.key("delete") }; return true }
                return change { super.deleteSurroundingText(before, after) }
            }
            override fun deleteSurroundingTextInCodePoints(before: Int, after: Int): Boolean {
                if (text.isEmpty()) return deleteSurroundingText(before, after)
                return change { super.deleteSurroundingTextInCodePoints(before, after) }
            }
            override fun sendKeyEvent(event: KeyEvent): Boolean = if (event.action == KeyEvent.ACTION_DOWN) handleKey(event) else true
            override fun performEditorAction(actionCode: Int): Boolean { if (ready) session.key("enter"); return true }
        }.also { editor = it; Selection.setSelection(text, text.length) }
    }
    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean = handleKey(event) || super.onKeyDown(keyCode, event)
    private fun handleKey(event: KeyEvent): Boolean {
        if (!ready) return false
        val key = when(event.keyCode) { KeyEvent.KEYCODE_ENTER -> "enter"; KeyEvent.KEYCODE_TAB -> "tab"; KeyEvent.KEYCODE_ESCAPE -> "escape"; KeyEvent.KEYCODE_DEL -> "backspace"; KeyEvent.KEYCODE_FORWARD_DEL -> "delete"; KeyEvent.KEYCODE_DPAD_LEFT -> "left"; KeyEvent.KEYCODE_DPAD_RIGHT -> "right"; KeyEvent.KEYCODE_DPAD_UP -> "up"; KeyEvent.KEYCODE_DPAD_DOWN -> "down"; KeyEvent.KEYCODE_MOVE_HOME -> "home"; KeyEvent.KEYCODE_MOVE_END -> "end"; KeyEvent.KEYCODE_PAGE_UP -> "pageup"; KeyEvent.KEYCODE_PAGE_DOWN -> "pagedown"; in KeyEvent.KEYCODE_F1..KeyEvent.KEYCODE_F12 -> "f${event.keyCode-KeyEvent.KEYCODE_F1+1}"; else -> null }
        val mods = (if (event.isShiftPressed) 1 else 0) or (if (event.isCtrlPressed) 2 else 0) or (if (event.isAltPressed) 4 else 0)
        if (key == "backspace" && text.isNotEmpty() && mods == 0) return editor?.deleteSurroundingTextInCodePoints(1, 0) ?: false
        if (key != null) { session.key(key, mods); text.clear(); Selection.setSelection(text, 0); return true }
        if (event.isCtrlPressed || event.isAltPressed) {
            val letter = KeyEvent.keyCodeToString(event.keyCode).removePrefix("KEYCODE_").lowercase()
            if (letter.length == 1 && letter[0].isLetterOrDigit() && letter[0].code < 128) { session.key(letter, mods); text.clear(); Selection.setSelection(text, 0); return true }
        }
        val code = event.unicodeChar
        return if (code > 0) editor?.commitText(String(Character.toChars(code)), 1) ?: false else false
    }
    override fun onDraw(canvas: Canvas) {
        paint.isAntiAlias = true; paint.color = if (dark) 0xffbabdc0.toInt() else 0xff53575b.toInt()
        paint.textSize = android.util.TypedValue.applyDimension(android.util.TypedValue.COMPLEX_UNIT_SP, 14f, resources.displayMetrics); paint.textAlign = Paint.Align.CENTER
        label.draw(canvas, if (ready) "Typing live · tap here for keyboard" else "Connect your PC and allow remote input", width, height, (16 * resources.displayMetrics.density).toInt(), paint.textSize, paint.color)
    }
}
