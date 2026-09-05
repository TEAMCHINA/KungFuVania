namespace KungFuVania.Player.Combat
{
    // Just a timer — prevents dodge-spam by holding NONE off until it expires.
    public class DodgeRecoveryState : ICombatState
    {
        private readonly PlayerCombatStateMachine machine;
        private readonly float duration;
        private float elapsed;

        public DodgeRecoveryState(PlayerCombatStateMachine machine, float duration)
        {
            this.machine = machine;
            this.duration = duration;
        }

        public void Enter() => elapsed = 0f;

        public void Tick(float deltaTime)
        {
            elapsed += deltaTime;
            if (elapsed >= duration)
                machine.ChangeState("NONE");
        }

        public void Exit() { }
    }
}
