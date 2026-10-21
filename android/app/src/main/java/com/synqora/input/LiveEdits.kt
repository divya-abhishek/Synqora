package com.synqora.input

data class LiveEdit(val move: Int = 0, val before: Int = 0, val after: Int = 0, val text: String = "")

/** Convert IME composition/replacement into edits, without resending an entire draft. */
fun liveEdits(old: String, next: String, oldCursor: Int, nextCursor: Int): List<LiveEdit> {
    fun distance(text: String, from: Int, to: Int): Int = if (to >= from) text.codePointCount(from, to) else -text.codePointCount(to, from)
    val a = oldCursor.coerceIn(0, old.length)
    val b = nextCursor.coerceIn(0, next.length)
    if (old == next) return distance(old, a, b).takeIf { it != 0 }?.let { listOf(LiveEdit(move = it)) } ?: emptyList()
    var prefix = 0
    while (prefix < old.length && prefix < next.length && old[prefix] == next[prefix]) prefix++
    if (prefix > 0 && prefix < old.length && old[prefix].isLowSurrogate()) prefix--
    var suffix = 0
    while (old.length - suffix > prefix && next.length - suffix > prefix && old[old.length - suffix - 1] == next[next.length - suffix - 1]) suffix++
    if (suffix > 0 && old.length - suffix < old.length && old[old.length - suffix].isLowSurrogate()) suffix--
    val end = old.length - suffix
    val inserted = next.substring(prefix, next.length - suffix)
    val edits = mutableListOf(LiveEdit(move = distance(old, a, end), before = old.codePointCount(prefix, end), text = inserted))
    val tailMove = distance(next, prefix + inserted.length, b)
    if (tailMove != 0) edits += LiveEdit(move = tailMove)
    return edits
}
