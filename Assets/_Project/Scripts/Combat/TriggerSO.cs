using UnityEngine;

namespace KungFuVania.Combat
{
    // Base for anything that can trigger an effect — a motion input today, potentially a prop
    // interaction or a kill condition later (see GAME_PLAN.md 3l). No members yet; subclasses
    // carry whatever data their own trigger condition needs.
    public abstract class TriggerSO : ScriptableObject { }
}
