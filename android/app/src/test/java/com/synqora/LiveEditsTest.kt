package com.synqora

import com.synqora.input.*
import org.junit.Assert.*
import org.junit.Test

class LiveEditsTest {
    @Test fun typing_sends_only_new_character() { assertEquals(listOf(LiveEdit(text = "b")), liveEdits("a", "ab", 1, 2)) }
    @Test fun composing_correction_replaces_only_changed_tail() { assertEquals(listOf(LiveEdit(before = 1, text = "t")), liveEdits("tesx", "test", 4, 4)) }
    @Test fun deletion_is_immediate() { assertEquals(listOf(LiveEdit(before = 1)), liveEdits("hello", "hell", 5, 4)) }
    @Test fun unicode_character_is_not_split() { assertEquals(listOf(LiveEdit(before = 1, text = "😁")), liveEdits("😀", "😁", 2, 2)) }
    @Test fun caret_moves_are_independent_of_text() { assertEquals(listOf(LiveEdit(move = -1)), liveEdits("hi", "hi", 2, 1)) }
    @Test fun edits_in_middle_preserve_suffix() { assertEquals(listOf(LiveEdit(text = "x")), liveEdits("abc", "axbc", 1, 2)) }
}
