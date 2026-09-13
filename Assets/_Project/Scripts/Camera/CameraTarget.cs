using UnityEngine;
using KungFuVania.Core;
using KungFuVania.Player;

namespace KungFuVania.Camera
{
    public class CameraTarget : MonoBehaviour
    {
        [SerializeField] private float verticalOffset = 2f;

        private CameraConfigSO config;
        private Transform playerTransform;

        public void Initialize(CameraConfigSO cfg, Transform player)
        {
            config = cfg;
            playerTransform = player;
            SnapToPlayer();
        }

        private void Awake()
        {
            EventBus.Subscribe<OnPlayerLanded>(OnPlayerLanded);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<OnPlayerLanded>(OnPlayerLanded);
        }

        private void Update()
        {
            if (playerTransform == null) return;
            transform.position = new Vector3(
                playerTransform.position.x,
                playerTransform.position.y + verticalOffset,
                transform.position.z
            );
        }

        private void OnPlayerLanded(OnPlayerLanded _) { }

        public void SnapToPlayer()
        {
            StopAllCoroutines();
            if (playerTransform == null) return;
            transform.position = new Vector3(
                playerTransform.position.x,
                playerTransform.position.y + verticalOffset,
                transform.position.z
            );
        }
    }
}
