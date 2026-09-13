using UnityEngine;

namespace KungFuVania.Camera
{
    public struct RoomConfinementReady
    {
        public Collider2D bounds;
    }

    public struct RoomTransitionComplete { }

    public struct BossFightStarted
    {
        public Transform bossTransform;
        public float bossCameraRadius;
    }

    public struct BossFightEnded { }

    public struct CameraShakeRequested
    {
        public Vector2 forceDirection;
        public float? forceOverride;
    }
}
