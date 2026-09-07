using UnityEngine;

namespace KungFuVania.Combat
{
    // Binds one TriggerSO to one EffectSO plus display metadata. Display fields are always
    // present even where unused (e.g. an environment-prop buff might only ever show a generic
    // "Interact" prompt with no name/icon) — see GAME_PLAN.md 3l.
    [CreateAssetMenu(fileName = "TriggerEffect", menuName = "KungFuVania/Trigger Effect")]
    public class TriggerEffectSO : ScriptableObject
    {
        public TriggerSO trigger;
        public EffectSO effect;

        public string displayName;
        public string description;
        public Sprite icon;
    }
}
