using UnityEngine;

namespace KungFuVania.Combat
{
    public abstract class EffectSO : ScriptableObject
    {
        public abstract void Execute(GameObject caster);
    }
}
