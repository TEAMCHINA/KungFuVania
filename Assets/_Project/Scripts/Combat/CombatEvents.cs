using UnityEngine;

namespace KungFuVania.Combat
{
    public struct CombatStateChanged
    {
        public string StateId;
    }

    public struct OnEntityDamaged
    {
        public GameObject Target;
        public float Damage;
        public bool IsLowHit;
    }
}
