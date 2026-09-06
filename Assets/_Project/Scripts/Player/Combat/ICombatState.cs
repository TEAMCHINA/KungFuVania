namespace KungFuVania.Player.Combat
{
    public interface ICombatState
    {
        void Enter();
        void Tick(float deltaTime);
        // For states that move a Rigidbody2D (currently just DodgeState). Rigidbody moves belong
        // in FixedUpdate, never Update — Update can fire multiple times per physics step at high
        // framerates, and since Kinematic MovePosition targets are computed from the not-yet-moved
        // current position, each extra call overwrites the last instead of adding to it, silently
        // discarding most of the intended movement. No-op for states that don't touch physics.
        void FixedTick(float fixedDeltaTime);
        void Exit();
    }
}
