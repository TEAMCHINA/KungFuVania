using UnityEngine;
using Unity.Cinemachine;
using KungFuVania.Core;

namespace KungFuVania.Camera
{
    public class CameraManager : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private CameraConfigSO config;

        [Header("Virtual Cameras")]
        [SerializeField] private CinemachineVirtualCamera gameplayVC;
        [SerializeField] private CinemachineVirtualCamera bossVC;
        [SerializeField] private CinemachineVirtualCamera deathVC;

        [Header("Boss Target Group")]
        [SerializeField] private CinemachineTargetGroup bossTargetGroup;

        [Header("Confiner")]
        [SerializeField] private CinemachineConfiner2D confiner;

        [Header("Shake")]
        [SerializeField] private CinemachineImpulseSource impulseSource;

        [Header("Camera Target")]
        [SerializeField] private CameraTarget cameraTarget;

        private Transform bossTransform;

        private void Awake()
        {
            Transform player = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (player != null && cameraTarget != null && config != null)
                cameraTarget.Initialize(config, player);

            if (gameplayVC != null && cameraTarget != null)
                gameplayVC.Follow = cameraTarget.transform;

            if (bossVC != null && bossTargetGroup != null)
                bossVC.Follow = bossTargetGroup.transform;

            EventBus.Subscribe<RoomConfinementReady>(OnRoomConfinementReady);
            EventBus.Subscribe<RoomTransitionComplete>(OnRoomTransitionComplete);
            EventBus.Subscribe<BossFightStarted>(OnBossFightStarted);
            EventBus.Subscribe<BossFightEnded>(OnBossFightEnded);
            EventBus.Subscribe<CameraShakeRequested>(OnCameraShakeRequested);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<RoomConfinementReady>(OnRoomConfinementReady);
            EventBus.Unsubscribe<RoomTransitionComplete>(OnRoomTransitionComplete);
            EventBus.Unsubscribe<BossFightStarted>(OnBossFightStarted);
            EventBus.Unsubscribe<BossFightEnded>(OnBossFightEnded);
            EventBus.Unsubscribe<CameraShakeRequested>(OnCameraShakeRequested);
        }

        private void OnRoomConfinementReady(RoomConfinementReady e)
        {
            if (confiner == null || e.bounds == null) return;
            confiner.BoundingShape2D = e.bounds;
            confiner.InvalidateCache();
        }

        private void OnRoomTransitionComplete(RoomTransitionComplete e) => cameraTarget?.SnapToPlayer();

        private void OnBossFightStarted(BossFightStarted e)
        {
            if (bossTargetGroup == null || bossVC == null) return;
            bossTransform = e.bossTransform;
            bossTargetGroup.AddMember(bossTransform, 1f, e.bossCameraRadius);
            bossVC.Priority = 20;
        }

        private void OnBossFightEnded(BossFightEnded e)
        {
            if (bossTargetGroup == null || bossVC == null) return;
            if (bossTransform != null)
                bossTargetGroup.RemoveMember(bossTransform);
            bossTransform = null;
            bossVC.Priority = 0;
        }

        private void OnCameraShakeRequested(CameraShakeRequested e)
        {
            if (impulseSource == null) return;
            float force = e.forceOverride ?? config.defaultShakeForce;
            impulseSource.GenerateImpulse(force * (Vector3)e.forceDirection);
        }
    }
}
