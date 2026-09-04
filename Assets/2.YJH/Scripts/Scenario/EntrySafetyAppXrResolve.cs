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

            var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
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

        public static Transform PreferCommonController(Transform current, string controllerName)
        {
            // Additive PreEntry contains its own authored Left/Right Controller objects.
            // They can remain active briefly while the App rig is already tracking, so
            // the generic "current is active" rule can keep the zone bound to the wrong
            // hand. Always prefer the controller under Common XR Origin when it exists.
            if (current != null && current.gameObject.activeInHierarchy &&
                HasNamedAncestor(current, "Common XR Origin"))
                return current;

            var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate != null && candidate.name == controllerName &&
                    candidate.gameObject.activeInHierarchy &&
                    HasNamedAncestor(candidate, "Common XR Origin"))
                    return candidate;
            }

            if (current != null && current.gameObject.activeInHierarchy)
                return current;
            return FindActiveByName(controllerName);
        }

        private static bool HasNamedAncestor(Transform candidate, string ancestorName)
        {
            for (var current = candidate; current != null; current = current.parent)
            {
                if (current.name == ancestorName)
                    return true;
            }
            return false;
        }

        public static Transform FindActiveLeftControllerVisual()
        {
            var left = FindActiveByName("Left Controller");
            if (left == null)
                return null;

            // Integrated App uses the shared controller model container (Visuals).
            // A standalone PreEntry scene still falls back to its authored hand container (Visual).
            var sharedVisuals = left.Find("Visuals");
            if (sharedVisuals != null && sharedVisuals.gameObject.activeInHierarchy)
                return sharedVisuals;

            var authoredVisual = left.Find("Visual");
            if (authoredVisual != null && authoredVisual.gameObject.activeInHierarchy)
                return authoredVisual;
            return left;
        }

        public static Camera PreferLiveMainCamera(Camera current)
        {
            if (current != null && current.isActiveAndEnabled && current.gameObject.activeInHierarchy)
                return current;

            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled && main.gameObject.activeInHierarchy)
                return main;

            var named = FindActiveByName("Main Camera");
            if (named != null)
            {
                var namedCamera = named.GetComponent<Camera>();
                if (namedCamera != null && namedCamera.isActiveAndEnabled)
                    return namedCamera;
            }

            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
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
