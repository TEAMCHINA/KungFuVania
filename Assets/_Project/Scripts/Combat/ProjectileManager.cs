using UnityEngine;

namespace KungFuVania.Combat
{
    // Scene-owned component, not a static utility -- same reasoning as EquipmentManager/
    // AuraManager (GAME_PLAN.md 3l/3j): the moment pooling is added this needs real state (a
    // pool per prefab), so it starts as a component rather than needing a rewrite later. Every
    // spawn/despawn funnels through here; nothing else calls Instantiate/Destroy on a projectile.
    public class ProjectileManager : MonoBehaviour
    {
        // Plain Instantiate/Destroy for now, not pooling -- deliberate, not an oversight (3l).
        // Pooling earns its complexity once spawn volume is high enough for GC pressure to
        // matter, which is bullet-hell territory; a player manually inputting a motion+button
        // per special is nowhere near that. Funneling every spawn through this one method means
        // swapping in a real pool later, if profiling ever asks for it, is contained here.
        // caster is optional but should always be passed by real callers -- see
        // ProjectileController.Launch/OnTriggerEnter2D for why a projectile needs to know (and
        // ignore) whoever fired it, rather than treating every HurtboxController it touches as a
        // valid target.
        public ProjectileController Spawn(GameObject prefab, Vector2 position, Vector2 direction, HitboxDataSO hitboxData, Transform caster = null)
        {
            var instance = Instantiate(prefab).GetComponent<ProjectileController>();
            instance.Launch(this, position, direction, hitboxData, caster);
            return instance;
        }

        public void Despawn(ProjectileController instance) => Destroy(instance.gameObject);
    }
}
