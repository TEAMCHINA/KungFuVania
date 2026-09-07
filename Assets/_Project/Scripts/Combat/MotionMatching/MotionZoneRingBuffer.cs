using System;

namespace KungFuVania.MotionMatching
{
    // Fixed-capacity ring buffer of MotionZoneEvent — same general shape as the existing
    // Assets/_Project/Scripts/Combat/InputBuffer.cs (fixed array + write index, not a List<T>),
    // not the same class: this one holds a rolling window of every zone this attempt has visited,
    // not a small set of not-yet-consumed button presses. Lives in this pure assembly so
    // MotionMatcher can be exercised from a synchronous unit test with zero Unity dependency (see
    // GAME_PLAN.md 3l "Testability").
    //
    // Capacity is generous, not precisely tuned — motion sequenceWindows are short (well under a
    // couple of seconds) and matching only ever cares about entries still inside that window, so
    // an entry that ages out is irrelevant whether or not it happens to still be physically
    // present in the array.
    public sealed class MotionZoneRingBuffer
    {
        private const int DefaultCapacity = 64;

        private readonly MotionZoneEvent[] entries;
        private int writeIndex;
        private int count;

        public MotionZoneRingBuffer(int capacity = DefaultCapacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            entries = new MotionZoneEvent[capacity];
        }

        public int Count => count;

        public void Push(int zone, float timestamp, bool isChargeSatisfaction = false)
        {
            entries[writeIndex] = new MotionZoneEvent(zone, timestamp, isChargeSatisfaction);
            writeIndex = (writeIndex + 1) % entries.Length;
            if (count < entries.Length) count++;
        }

        // 0 = the most recent entry, 1 = the one before that, etc. Callers (MotionMatcher) always
        // check Count first — this throws rather than clamping so an off-by-one in the scan loop
        // fails loudly instead of silently reading garbage.
        public MotionZoneEvent FromEnd(int indexFromEnd)
        {
            if (indexFromEnd < 0 || indexFromEnd >= count)
                throw new ArgumentOutOfRangeException(nameof(indexFromEnd));

            var idx = (writeIndex - 1 - indexFromEnd + entries.Length * 2) % entries.Length;
            return entries[idx];
        }
    }
}
