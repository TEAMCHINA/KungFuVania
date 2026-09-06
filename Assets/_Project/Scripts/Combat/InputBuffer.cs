using UnityEngine;

namespace KungFuVania.Combat
{
    // Timestamped ring buffer (capacity 16, see GAME_PLAN.md 3e) for presses the combat state
    // machine couldn't act on yet. Stores raw action ids only — it has no opinion on what an id
    // means or whether it's still valid later; PlayerController re-resolves that at consume time
    // instead of trusting whatever was true when the press was buffered.
    public class InputBuffer : MonoBehaviour
    {
        private const int Capacity = 16;

        // How long a buffered press stays worth firing before it's dropped instead. Measured from
        // the press itself rather than from a cancellable frame (no such concept exists yet — see
        // GAME_PLAN.md 3e ComboStep), so a press at the very start of the busiest current attack
        // (JUMP_KICK, ~0.3s) still needs to outlive it; kept generous on top of that since a
        // forgiving buffer is the whole point (spec calls out "slightly sloppy but still rewarded").
        [SerializeField] private float expireWindow = 0.4f;

        private readonly string[] actionIds = new string[Capacity];
        private readonly float[] timestamps = new float[Capacity];
        private int writeIndex;
        private bool hasEntries;

        public void Record(string actionId)
        {
            actionIds[writeIndex] = actionId;
            timestamps[writeIndex] = Time.time;
            writeIndex = (writeIndex + 1) % Capacity;
            hasEntries = true;
        }

        // Only the most recent press matters once the machine frees up — anything older is stale
        // intent, superseded by whatever was pressed after it. Clears the buffer on every
        // attempt, hit or miss, so a rejected or expired entry can never linger for some later,
        // unrelated attempt to consume.
        public bool TryConsumeFreshest(out string actionId)
        {
            actionId = null;
            if (!hasEntries) return false;

            var freshestIndex = (writeIndex - 1 + Capacity) % Capacity;
            var age = Time.time - timestamps[freshestIndex];
            hasEntries = false;

            if (age > expireWindow) return false;
            actionId = actionIds[freshestIndex];
            return true;
        }
    }
}
