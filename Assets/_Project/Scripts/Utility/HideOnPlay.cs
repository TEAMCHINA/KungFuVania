using UnityEngine;

namespace KungFuVania.Utility
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class HideOnPlay : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<SpriteRenderer>().enabled = false;
        }
    }
}
