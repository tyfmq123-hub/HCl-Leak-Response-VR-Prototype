using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Creates persistent, non-interactive equipment name plates on the PPE room's back wall.
/// Positions are derived from the authored wall and equipment, so labels remain aligned
/// when the room layout is adjusted without adding another saved Scene dependency.
/// </summary>
[DisallowMultipleComponent]
public sealed class PPEEquipmentWallLabels : MonoBehaviour
{
    const string SceneName = "04_PPE";
    public const string PersistentRootName = "PPE_EquipmentWallLabels";
    const string RuntimeRootName = "PPE_EquipmentWallLabels_Runtime";
    const string BackWallName = "Wall_Back";
    const string DeskRootName = "PPE_DeskTop";
    const string PreferredFontPath = "PPE/PPE_HangulFont";
    const string FallbackFontPath = "PPE_HangulFont";
    const float WallClearance = 0.012f;
    const float CanvasScale = 0.001f;
    const float PanelHeightPixels = 136f;

    readonly struct LabelSpec
    {
        public LabelSpec(string targetName, string text, string requiredAncestor,
            float verticalOffset, float widthMeters)
        {
            TargetName = targetName;
            Text = text;
            RequiredAncestor = requiredAncestor;
            VerticalOffset = verticalOffset;
            WidthMeters = widthMeters;
        }

        public string TargetName { get; }
        public string Text { get; }
        public string RequiredAncestor { get; }
        public float VerticalOffset { get; }
        public float WidthMeters { get; }
    }

    static readonly LabelSpec[] Labels =
    {
        new LabelSpec("GasDetectorSlot", "HCl 측정기", DeskRootName, 0.18f, 0.46f),
        new LabelSpec("AbsorbentPadSlot", "흡착패드", DeskRootName, 0.18f, 0.44f),
        new LabelSpec("AbsorbentSockSlot", "흡착삭", DeskRootName, 0.18f, 0.42f),
        new LabelSpec("ResponseBag_HardShell", "대응가방", null, 0.20f, 0.48f),
        new LabelSpec("INT_WasteContainer_01", "폐기물 수거용기", null, 0.20f, 0.66f),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreateFor(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryCreateFor(scene);
    }

    static void TryCreateFor(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded ||
            !string.Equals(scene.name, SceneName, StringComparison.Ordinal))
        {
            return;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; ++i)
        {
            if (string.Equals(roots[i].name, PersistentRootName, StringComparison.Ordinal))
            {
                if (roots[i].GetComponent<PPEEquipmentWallLabels>() == null)
                    roots[i].AddComponent<PPEEquipmentWallLabels>();
                return;
            }

            if (string.Equals(roots[i].name, RuntimeRootName, StringComparison.Ordinal))
                return;
        }

        var root = new GameObject(RuntimeRootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<PPEEquipmentWallLabels>();
    }

    IEnumerator Start()
    {
        // Wait for prefab activation and PPE setup scripts to settle before reading bounds.
        yield return null;

        // 04_PPE already contains carefully positioned, saved labels. In the additive App
        // flow this component used to delete those authored objects and rebuild them from
        // runtime bounds, so the integrated room did not match the Scene view. Keep the
        // authored set intact and only generate labels for older scenes that have none.
        if (HasCompleteAuthoredLabelSet())
            yield break;

        RebuildNow();
    }

    bool HasCompleteAuthoredLabelSet()
    {
        for (int i = 0; i < Labels.Length; ++i)
        {
            Transform label = transform.Find($"NameTag_{Labels[i].TargetName}");
            if (label == null)
                return false;
        }

        return true;
    }

    public void RebuildNow()
    {
        ClearGeneratedLabels();
        BuildLabels(gameObject.scene);
    }

    void ClearGeneratedLabels()
    {
        for (int childIndex = transform.childCount - 1; childIndex >= 0; --childIndex)
        {
            Transform child = transform.GetChild(childIndex);
            if (child == null ||
                !child.name.StartsWith("NameTag_", StringComparison.Ordinal))
            {
                continue;
            }

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    void BuildLabels(Scene scene)
    {
        Transform wall = FindTransform(scene, BackWallName, null);
        if (wall == null || !TryGetBounds(wall, out Bounds wallBounds))
        {
            Debug.LogWarning("[PPE Labels] Wall_Back was not found; equipment labels were not created.", this);
            return;
        }

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>(PreferredFontPath) ??
                             Resources.Load<TMP_FontAsset>(FallbackFontPath);

        for (int i = 0; i < Labels.Length; ++i)
        {
            LabelSpec spec = Labels[i];
            Transform target = FindTransform(scene, spec.TargetName, spec.RequiredAncestor);
            if (target == null)
            {
                Debug.LogWarning($"[PPE Labels] Target '{spec.TargetName}' was not found.", this);
                continue;
            }

            Bounds targetBounds = TryGetBounds(target, out Bounds visibleBounds)
                ? visibleBounds
                : new Bounds(target.position, Vector3.one * 0.05f);
            CreateLabel(spec, targetBounds, wallBounds, font);
        }
    }

    void CreateLabel(LabelSpec spec, Bounds targetBounds, Bounds wallBounds, TMP_FontAsset font)
    {
        bool wallNormalIsX = wallBounds.size.x < wallBounds.size.z;
        Vector3 axis = wallNormalIsX ? Vector3.right : Vector3.forward;
        float wallCoordinate = wallNormalIsX ? wallBounds.center.x : wallBounds.center.z;
        float targetCoordinate = wallNormalIsX ? targetBounds.center.x : targetBounds.center.z;
        float side = Mathf.Sign(targetCoordinate - wallCoordinate);
        if (Mathf.Approximately(side, 0f))
            side = -1f;

        Vector3 roomNormal = axis * side;
        float wallHalfThickness = wallNormalIsX ? wallBounds.extents.x : wallBounds.extents.z;
        Vector3 position = targetBounds.center;
        if (wallNormalIsX)
            position.x = wallBounds.center.x + roomNormal.x * (wallHalfThickness + WallClearance);
        else
            position.z = wallBounds.center.z + roomNormal.z * (wallHalfThickness + WallClearance);
        position.y = Mathf.Clamp(
            targetBounds.max.y + spec.VerticalOffset,
            wallBounds.min.y + 0.18f,
            wallBounds.max.y - 0.18f);

        var labelObject = new GameObject(
            $"NameTag_{spec.TargetName}", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.SetPositionAndRotation(position, Quaternion.LookRotation(-roomNormal, Vector3.up));
        rect.localScale = Vector3.one * CanvasScale;
        rect.sizeDelta = new Vector2(spec.WidthMeters / CanvasScale, PanelHeightPixels);

        Canvas canvas = labelObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 45;
        CanvasScaler scaler = labelObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        var backgroundObject = new GameObject(
            "Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.SetParent(rect, false);
        Stretch(backgroundRect);
        Image background = backgroundObject.GetComponent<Image>();
        background.color = new Color(0.018f, 0.075f, 0.125f, 0.96f);
        background.raycastTarget = false;

        var accentObject = new GameObject(
            "Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform accentRect = accentObject.GetComponent<RectTransform>();
        accentRect.SetParent(backgroundRect, false);
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0.022f, 1f);
        accentRect.offsetMin = Vector2.zero;
        accentRect.offsetMax = Vector2.zero;
        Image accent = accentObject.GetComponent<Image>();
        accent.color = new Color(0.02f, 0.72f, 0.67f, 1f);
        accent.raycastTarget = false;

        var textObject = new GameObject(
            "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(backgroundRect, false);
        Stretch(textRect);
        textRect.offsetMin = new Vector2(26f, 8f);
        textRect.offsetMax = new Vector2(-16f, -8f);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = spec.Text;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 30f;
        text.fontSizeMax = 46f;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        if (font != null && font.material != null)
        {
            font.TryAddCharacters(spec.Text);
            text.font = font;
            text.fontSharedMaterial = font.material;
        }
    }

    static Transform FindTransform(Scene scene, string targetName, string requiredAncestor)
    {
        Transform preferredMatch = FindTransformInScene(scene, targetName, requiredAncestor);
        if (preferredMatch != null)
            return preferredMatch;

        // Integration mode can re-home PPE runtime objects under 01_App while the
        // authored wall remains in 04_PPE. Search every loaded Scene before falling
        // back to proximity selection.
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; ++sceneIndex)
        {
            Scene loadedScene = SceneManager.GetSceneAt(sceneIndex);
            if (loadedScene == scene)
                continue;

            Transform match = FindTransformInScene(
                loadedScene, targetName, requiredAncestor);
            if (match != null)
                return match;
        }

        // PPE setup can detach the visible slot from the prefab root while keeping it
        // in 04_PPE. In that case the Scene-local active candidate is still the correct
        // wall reference even though its PPE_DeskTop ancestor no longer exists.
        Transform sceneLocalCandidate = FindBestSceneLocalCandidate(scene, targetName);
        if (sceneLocalCandidate != null)
            return sceneLocalCandidate;

        if (string.IsNullOrWhiteSpace(requiredAncestor))
            return null;

        Transform anchor = FindFirstAcrossLoadedScenes(requiredAncestor);
        if (anchor == null)
            return null;

        Transform closest = null;
        float closestDistance = float.PositiveInfinity;
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; ++sceneIndex)
        {
            Scene loadedScene = SceneManager.GetSceneAt(sceneIndex);
            if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                continue;

            GameObject[] roots = loadedScene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; ++rootIndex)
            {
                Transform[] candidates = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (int candidateIndex = 0; candidateIndex < candidates.Length; ++candidateIndex)
                {
                    Transform candidate = candidates[candidateIndex];
                    if (!string.Equals(
                            candidate.name.Trim(), targetName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    float distance = (candidate.position - anchor.position).sqrMagnitude;
                    if (distance >= closestDistance)
                        continue;

                    closest = candidate;
                    closestDistance = distance;
                }
            }
        }

        return closest;
    }

    static Transform FindTransformInScene(
        Scene scene, string targetName, string requiredAncestor)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; ++rootIndex)
        {
            Transform[] candidates = roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int candidateIndex = 0; candidateIndex < candidates.Length; ++candidateIndex)
            {
                Transform candidate = candidates[candidateIndex];
                if (!string.Equals(candidate.name.Trim(), targetName, StringComparison.Ordinal) ||
                    !HasAncestor(candidate, requiredAncestor))
                {
                    continue;
                }

                return candidate;
            }
        }

        return null;
    }

    static Transform FindFirstAcrossLoadedScenes(string targetName)
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; ++sceneIndex)
        {
            Transform match = FindTransformInScene(
                SceneManager.GetSceneAt(sceneIndex), targetName, null);
            if (match != null)
                return match;
        }

        return null;
    }

    static Transform FindBestSceneLocalCandidate(Scene scene, string targetName)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        Transform inactiveFallback = null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; ++rootIndex)
        {
            Transform[] candidates = roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int candidateIndex = 0; candidateIndex < candidates.Length; ++candidateIndex)
            {
                Transform candidate = candidates[candidateIndex];
                if (!string.Equals(candidate.name.Trim(), targetName, StringComparison.Ordinal))
                    continue;

                if (candidate.gameObject.activeInHierarchy)
                    return candidate;

                inactiveFallback ??= candidate;
            }
        }

        return inactiveFallback;
    }

    static bool HasAncestor(Transform transform, string requiredAncestor)
    {
        if (string.IsNullOrWhiteSpace(requiredAncestor))
            return true;

        for (Transform current = transform.parent; current != null; current = current.parent)
        {
            if (string.Equals(current.name.Trim(), requiredAncestor, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; ++i)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
            return true;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; ++i)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
