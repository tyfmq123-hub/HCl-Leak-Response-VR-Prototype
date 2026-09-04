using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Single PPE-room source for the currently used XR Origin.
/// Always returns an active, enabled Origin. Never keeps a disabled or destroyed cache.
/// App additive load: Common XR Origin is active and the PPE-scene Origin is disabled.
/// Standalone Editor Play of 04_PPE: the PPE-scene Origin is active.
/// </summary>
public static class PPEActiveXrOrigin
{
    static XROrigin cachedOrigin;
    static Transform cachedOriginTransform;
    static string loggedSelection;

    public static XROrigin Resolve()
    {
        if (IsUsable(cachedOrigin))
            return cachedOrigin;

        cachedOrigin = null;
        cachedOriginTransform = null;

        XROrigin best = null;
        var bestScore = int.MinValue;
        var origins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude);
        for (var i = 0; i < origins.Length; i++)
        {
            var origin = origins[i];
            if (!IsUsable(origin))
                continue;

            var score = Score(origin);
            if (best == null || score > bestScore)
            {
                best = origin;
                bestScore = score;
            }
        }

        cachedOrigin = best;
        if (cachedOrigin != null)
        {
            cachedOriginTransform = OriginTransform(cachedOrigin);
            LogSelection(cachedOrigin);
        }

        return cachedOrigin;
    }

    public static Transform ResolveOriginTransform()
    {
        var origin = Resolve();
        if (origin == null)
            return null;

        if (cachedOriginTransform != null && cachedOriginTransform.gameObject.activeInHierarchy)
            return cachedOriginTransform;

        cachedOriginTransform = OriginTransform(origin);
        return cachedOriginTransform;
    }

    public static bool IsUsable(XROrigin origin)
    {
        return origin != null
            && origin.isActiveAndEnabled
            && origin.gameObject.activeInHierarchy;
    }

    static Transform OriginTransform(XROrigin origin)
    {
        if (origin == null)
            return null;
        return origin.Origin != null ? origin.Origin.transform : origin.transform;
    }

    static int Score(XROrigin origin)
    {
        var score = 0;
        var camera = origin.Camera;
        if (camera != null && camera.isActiveAndEnabled && camera.gameObject.activeInHierarchy)
            score += 10;
        if (Camera.main != null && camera == Camera.main)
            score += 5;
        var name = origin.gameObject.name;
        if (name.IndexOf("Common", System.StringComparison.OrdinalIgnoreCase) >= 0)
            score += 3;
        return score;
    }

    static void LogSelection(XROrigin origin)
    {
        if (origin == null)
            return;

        var key = origin.gameObject.scene.name + "/" + origin.name + "/" + origin.GetEntityId();
        if (loggedSelection == key)
            return;
        loggedSelection = key;
        Debug.Log(
            "[PPE XR Origin] Active = " + origin.name +
            " scene='" + origin.gameObject.scene.name + "'",
            origin);
    }
}
