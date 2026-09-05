namespace KungFuVania.Player.Combat
{
    public interface ICombatState
    {
        void Enter();
        void Tick(float deltaTime);
        void Exit();
    }
}
