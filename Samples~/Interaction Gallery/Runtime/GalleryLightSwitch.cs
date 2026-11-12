using UnityEngine;

namespace EasyInteraction.Samples.Gallery
{
    public sealed class GalleryLightSwitch : MonoBehaviour
    {
        public Light lightSource;
        public void Toggle()
        {
            if (lightSource)
                lightSource.enabled = !lightSource.enabled;
        }
    }
}
