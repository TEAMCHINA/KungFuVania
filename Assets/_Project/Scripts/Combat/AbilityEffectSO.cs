namespace KungFuVania.Combat
{
    // Combat-specific EffectSO base — a domain-specific concrete base (not a direct EffectSO
    // reuse) so combat effects can share combat-specific plumbing later (AbilityExecutionContext,
    // GAME_PLAN.md 3j) without that leaking into unrelated trigger/effect domains.
    public abstract class AbilityEffectSO : EffectSO { }
}
