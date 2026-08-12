using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// Resolves live XR transforms after App disables the stage XR Origin and rebinds Common XR.
    /// </summary>
    internal static class EntrySafetyAppXrResolve
    {
        public static Transform FindActiveByName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return null;

            var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate != null && candidate.name == objectName && candidate.gameObject.activeInHierarchy)
                    return candidate;
            }

            return null;
        }

        public static Transform PreferLive(Transform current, string objectName)
        {
            if (current != null && current.gameObject.activeInHierarchy)
                return current;
            return FindActiveByName(objectName);
        }

        public static Transform FindActiveLeftControllerVisual()
        {
            var left = FindActiveByName("Left Controller");
            if (left == null)
                return null;
            var visual = left.Find("Visual");
            return visual != null ? visual : left;
        }

        public static Camera PreferLiveMainCamera(Camera current)
        {
            if (current != null && current.isActiveAndEnabled && current.gameObject.activeInHierarchy)
                return current;

            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled && main.gameObject.activeInHierarchy)
                return main;

            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera != null && camera.isActiveAndEnabled && camera.CompareTag("MainCamera"))
                    return camera;
            }

            return current;
        }

        public static Collider PreferLiveCollider(Collider current, string objectName)
        {
            if (current != null && current.enabled && current.gameObject.activeInHierarchy)
                return current;

            var host = FindActiveByName(objectName);
            if (host == null)
                return current;

            var collider = host.GetComponent<Collider>();
            return collider != null ? collider : current;
        }

        /// <summary>
        /// Moves a gameplay object from a disabled stage XR tree onto the live Common XR hierarchy.
        /// </summary>
        public static bool ReparentOnto(Transform subject, Transform parent, bool keepWorldPose)
        {
            if (subject == null || parent == null)
                return false;
            if (subject.parent == parent && subject.gameObject.activeInHierarchy)
                return true;

            subject.SetParent(parent, keepWorldPose);
            return true;
        }
    }
}
