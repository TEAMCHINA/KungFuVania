namespace KungFuVania.Player.Locomotion
{
    public interface ILocomotionState
    {
        void Enter();
        void Tick(float deltaTime);
        void Exit();
    }
}
