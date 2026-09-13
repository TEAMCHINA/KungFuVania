using UnityEngine;

namespace KungFuVania.Camera
{
    public class ParallaxLayer : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float parallaxFactor = 0.5f;

        private Transform cam;
        private float startX;
        private float startCamX;

        private void Start()
        {
            cam = UnityEngine.Camera.main.transform;
            startX = transform.position.x;
            startCamX = cam.position.x;
        }

        private void LateUpdate()
        {
            float offset = (cam.position.x - startCamX) * (1f - parallaxFactor);
            transform.position = new Vector3(startX + offset, transform.position.y, transform.position.z);
        }
    }
}
