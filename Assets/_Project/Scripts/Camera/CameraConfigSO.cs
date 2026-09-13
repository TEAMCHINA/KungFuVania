using UnityEngine;

namespace KungFuVania.Camera
{
    [CreateAssetMenu(fileName = "CameraConfig", menuName = "KungFuVania/Camera Config", order = 1)]
    public class CameraConfigSO : ScriptableObject
    {
        [Header("Gameplay Follow")]
        public float lookaheadTime = 0.5f;
        public float lookaheadSmoothing = 10f;
        public float horizontalDamping = 0.2f;
        public float verticalDamping = 0.5f;

        [Header("Lazy Vertical")]
        public float verticalSnapThreshold = 2.0f;
        public float verticalSnapDuration = 0.25f;

        [Header("Orthographic Sizes")]
        public float defaultOrthoSize = 6.0f;
        public float bossOrthoSizeMin = 7.0f;

        [Header("Shake")]
        public float defaultShakeForce = 0.3f;
    }
}
