using UnityEngine;

namespace KungFuVania.Input
{
    // Tuning for stick/dpad -> zone mapping (GAME_PLAN.md 3l). Numpad-style 1-9 zones, 5 = neutral:
    //     7 8 9
    //     4 5 6
    //     1 2 3
    [CreateAssetMenu(fileName = "StickInputConfig", menuName = "KungFuVania/Stick Input Config")]
    public class StickInputConfigSO : ScriptableObject
    {
        [SerializeField] private float zoneDeadzone = 0.3f;
        // Wider cardinal sectors make quarter-circles more forgiving. Diagonal width isn't its
        // own field — it's whatever's left over (90 - cardinalSectorWidth per quadrant) once the
        // four cardinal wedges are placed, since the two must tile 360 together; default 50°
        // cardinals leave 40° diagonals, matching GAME_PLAN.md 3l's forgiving-quarter-circle tuning.
        [SerializeField] private float cardinalSectorWidth = 50f;

        // Facing-normalized 1-9 zone for a raw stick/dpad Vector2. Mirrors horizontal-component
        // zones (1,3,4,6,7,9) by facing so every MotionInputTriggerSO is authored once in
        // toward/away terms — see GAME_PLAN.md 3l "mirror once at the source".
        public int ComputeZone(Vector2 rawStick, bool facingRight)
        {
            if (rawStick.magnitude < zoneDeadzone) return 5;

            var angle = Mathf.Atan2(rawStick.y, rawStick.x) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            var zone = ClassifyAngle(angle);
            return MirrorIfHorizontal(zone, facingRight);
        }

        // Cardinal sectors are centered on 0/90/180/270; whatever's left over (necessarily,
        // since the cardinal checks already ruled it out) is one of the four diagonal gaps.
        private int ClassifyAngle(float angle)
        {
            var halfCardinal = cardinalSectorWidth * 0.5f;

            if (AngleWithin(angle, 0f, halfCardinal)) return 6;   // right
            if (AngleWithin(angle, 90f, halfCardinal)) return 8;  // up
            if (AngleWithin(angle, 180f, halfCardinal)) return 4; // left
            if (AngleWithin(angle, 270f, halfCardinal)) return 2; // down

            if (angle > 0f && angle < 90f) return 9;    // up-right
            if (angle > 90f && angle < 180f) return 7;  // up-left
            if (angle > 180f && angle < 270f) return 1; // down-left
            return 3;                                   // down-right
        }

        private static bool AngleWithin(float angle, float center, float halfWidth) =>
            Mathf.Abs(Mathf.DeltaAngle(angle, center)) <= halfWidth;

        private static int MirrorIfHorizontal(int zone, bool facingRight)
        {
            if (facingRight) return zone;

            switch (zone)
            {
                case 1: return 3;
                case 3: return 1;
                case 4: return 6;
                case 6: return 4;
                case 7: return 9;
                case 9: return 7;
                default: return zone; // 2, 5, 8 carry no horizontal component
            }
        }
    }
}
