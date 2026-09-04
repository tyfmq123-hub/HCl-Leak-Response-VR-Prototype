#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders transparent PNG thumbnails from response-bag equipment FBX/prefabs
/// for use in ResponseBagGearPanel showcase cards.
/// </summary>
public class ResponseBagThumbnailRenderer : EditorWindow
{
    public const string OutputFolder = "Assets/1.JSR/Art/UI/ResponseBag/Thumbnails";
    public const string ResourcesThumbsFolder = "Assets/1.JSR/Resources/Thumbnails";

    const int TextureSize = 1024;

    [SerializeField] GameObject gasSource;
    [SerializeField] GameObject sockSource;
    [SerializeField] GameObject padSource;

    [Header("Gas detector pose")]
    // Upright + screen toward camera.
    [SerializeField] Vector3 gasEuler = new Vector3(-90f, -90f, 0f);
    [SerializeField] Vector3 gasOffset = new Vector3(0f, 0.02f, 0f);
    [SerializeField] float gasFitFill = 0.78f;
    [SerializeField] float gasCameraDist = 1.6f;

    [Header("Absorbent sock pose")]
    [SerializeField] Vector3 sockEuler = new Vector3(18f, -20f, 55f);
    [SerializeField] Vector3 sockOffset = Vector3.zero;
    [SerializeField] float sockFitFill = 0.78f;
    [SerializeField] float sockCameraDist = 1.6f;

    [Header("Absorbent pad pose")]
    [SerializeField] Vector3 padEuler = new Vector3(78f, 12f, 0f);
    [SerializeField] Vector3 padOffset = Vector3.zero;
    [SerializeField] float padFitFill = 0.72f;
    [SerializeField] float padCameraDist = 1.6f;

    public static void Open()
    {
        var win = GetWindow<ResponseBagThumbnailRenderer>("Bag Thumbnails");
        win.minSize = new Vector2(420f, 520f);
        win.AutoFindSources();
        win.Show();
    }

    public static void RenderDefaultsMenu()
    {
        RenderAllBatch();
        EditorUtility.DisplayDialog(
            "PPE Thumbnails",
            "3D model thumbnails written to:\n" + OutputFolder + "\nand Resources/PPE/Thumbnails.",
            "OK");
    }

    /// <summary>
    /// Batch / CI entry: Unity.exe -batchmode -executeMethod ResponseBagThumbnailRenderer.RenderAllBatch -quit
    /// </summary>
    public static void RenderAllBatch()
    {
        var win = CreateInstance<ResponseBagThumbnailRenderer>();
        try
        {
            win.AutoFindSources();
            win.RenderAllSilent();
        }
        finally
        {
            DestroyImmediate(win);
        }
    }

    /// <summary>Re-render only the gas detector thumbnail (upright front).</summary>
    public static void RenderGasBatch()
    {
        var win = CreateInstance<ResponseBagThumbnailRenderer>();
        try
        {
            win.AutoFindSources();
            EnsureFolders();
            win.RenderOne(
                win.gasSource,
                "ResponseBag_GasDetector.png",
                win.gasEuler,
                win.gasOffset,
                win.gasFitFill,
                win.gasCameraDist);
            AssetDatabase.Refresh();
            ConfigureSpriteImports();
            CopyIntoResources();
            AssetDatabase.Refresh();
            Debug.Log("[PPE Thumbnails] Gas detector re-rendered upright.");
        }
        finally
        {
            DestroyImmediate(win);
        }
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "카드 UI는 PNG Image를 쓰지만, 이 도구가 FBX 3D 모델을 스튜디오 카메라로 렌더해서 PNG를 만듭니다.\n" +
            "(캔버스에 MeshRenderer를 다시 넣지 않습니다 — clip이 안정적입니다.)",
            MessageType.Info);

        EditorGUILayout.LabelField("Sources (prefab / FBX)", EditorStyles.boldLabel);
        gasSource = (GameObject)EditorGUILayout.ObjectField("Gas Detector", gasSource, typeof(GameObject), true);
        sockSource = (GameObject)EditorGUILayout.ObjectField("Absorbent Sock", sockSource, typeof(GameObject), true);
        padSource = (GameObject)EditorGUILayout.ObjectField("Absorbent Pad", padSource, typeof(GameObject), true);

        if (GUILayout.Button("Auto-Find Sources"))
            AutoFindSources();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Gas pose", EditorStyles.boldLabel);
        gasEuler = EditorGUILayout.Vector3Field("Euler", gasEuler);
        gasOffset = EditorGUILayout.Vector3Field("Offset", gasOffset);
        gasFitFill = EditorGUILayout.Slider("Fill", gasFitFill, 0.4f, 0.95f);

        EditorGUILayout.LabelField("Sock pose", EditorStyles.boldLabel);
        sockEuler = EditorGUILayout.Vector3Field("Euler", sockEuler);
        sockOffset = EditorGUILayout.Vector3Field("Offset", sockOffset);
        sockFitFill = EditorGUILayout.Slider("Fill", sockFitFill, 0.4f, 0.95f);

        EditorGUILayout.LabelField("Pad pose", EditorStyles.boldLabel);
        padEuler = EditorGUILayout.Vector3Field("Euler", padEuler);
        padOffset = EditorGUILayout.Vector3Field("Offset", padOffset);
        padFitFill = EditorGUILayout.Slider("Fill", padFitFill, 0.4f, 0.95f);

        EditorGUILayout.Space(12);
        if (GUILayout.Button("Render All Thumbnails", GUILayout.Height(36)))
            RenderAll();
    }

    void AutoFindSources()
    {
        if (gasSource == null)
            gasSource = FindAsset("ENV_HClDetector_01");
        if (sockSource == null)
            sockSource = FindAsset("INT_AbsorbentSock_Straight_01");
        if (padSource == null)
            padSource = FindAsset("INT_AbsorbentPad_01");
    }

    static GameObject FindAsset(string nameContains)
    {
        var guids = AssetDatabase.FindAssets(nameContains + " t:Model");
        if (guids == null || guids.Length == 0)
            guids = AssetDatabase.FindAssets(nameContains + " t:Prefab");
        if (guids == null || guids.Length == 0)
            return null;
        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    void RenderAll()
    {
        RenderAllSilent();
        EditorUtility.DisplayDialog(
            "PPE Thumbnails",
            "3D model thumbnails written to:\n" + OutputFolder + "\nand copied to Resources/PPE/Thumbnails.",
            "OK");
    }

    void RenderAllSilent()
    {
        AutoFindSources();
        EnsureFolders();

        RenderOne(
            gasSource,
            "ResponseBag_GasDetector.png",
            gasEuler,
            gasOffset,
            gasFitFill,
            gasCameraDist);
        RenderOne(
            sockSource,
            "ResponseBag_AbsorbentSock.png",
            sockEuler,
            sockOffset,
            sockFitFill,
            sockCameraDist);
        RenderOne(
            padSource,
            "ResponseBag_AbsorbentPad.png",
            padEuler,
            padOffset,
            padFitFill,
            padCameraDist);

        AssetDatabase.Refresh();
        ConfigureSpriteImports();
        CopyIntoResources();
        AssetDatabase.Refresh();
        Debug.Log("[PPE Thumbnails] 3D render complete → " + OutputFolder);
    }

    static void EnsureFolders()
    {
        EnsureFolder("Assets/Art");
        EnsureFolder("Assets/1.JSR/Art/UI");
        EnsureFolder("Assets/1.JSR/Art/UI/ResponseBag");
        EnsureFolder(OutputFolder);
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/PPE");
        EnsureFolder(ResourcesThumbsFolder);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    void RenderOne(
        GameObject source,
        string fileName,
        Vector3 euler,
        Vector3 offset,
        float fill,
        float cameraDist)
    {
        if (source == null)
        {
            Debug.LogWarning("[PPE Thumbnails] Missing source for " + fileName);
            return;
        }

        byte[] bytes = null;

        // 1) Preferred: studio camera render of the actual FBX mesh.
        bytes = TryStudioRender(source, euler, offset, fill, cameraDist);

        // 2) Fallback: Unity asset 3D preview icon (still a real mesh preview, not albedo UV).
        if (!HasEnoughOpaquePixels(bytes))
        {
            Debug.LogWarning(
                "[PPE Thumbnails] Studio render empty for " + fileName +
                " — falling back to AssetPreview 3D icon.");
            bytes = TryAssetPreviewRender(source);
        }

        if (bytes == null || bytes.Length < 64)
        {
            Debug.LogError("[PPE Thumbnails] Failed to render " + fileName);
            return;
        }

        var abs = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder, fileName)
            .Replace('\\', '/');
        File.WriteAllBytes(abs, bytes);
        Debug.Log("[PPE Thumbnails] Wrote " + abs + " (" + bytes.Length + " bytes)");
    }

    byte[] TryStudioRender(
        GameObject source,
        Vector3 euler,
        Vector3 offset,
        float fill,
        float cameraDist)
    {
        var previewScene = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        Camera cam = null;
        Light key = null;
        Light fillLight = null;
        Light rim = null;
        RenderTexture rt = null;

        try
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (instance == null)
                instance = Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(instance, previewScene);

            StripForPreview(instance);
            // X/Y set the product face; Z is an on-screen upright roll around camera forward.
            instance.transform.rotation = Quaternion.Euler(euler.x, euler.y, 0f);
            if (Mathf.Abs(euler.z) > 0.01f)
                instance.transform.Rotate(0f, 0f, euler.z, Space.World);
            instance.transform.position = Vector3.zero;
            instance.transform.localScale = Vector3.one;

            var camGo = new GameObject("ThumbCam");
            SceneManager.MoveGameObjectToScene(camGo, previewScene);
            cam = camGo.AddComponent<Camera>();
            // Critical: camera must target the preview scene or Render() draws nothing.
            cam.scene = previewScene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.13f, 0f);
            cam.orthographic = true;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.forceIntoRenderTexture = true;
            cam.enabled = true;

            key = CreateLight(previewScene, "Key", new Vector3(35f, -40f, 0f), 1.35f);
            fillLight = CreateLight(previewScene, "Fill", new Vector3(20f, 50f, 0f), 0.55f);
            rim = CreateLight(previewScene, "Rim", new Vector3(-25f, 180f, 0f), 0.4f);
            // Preview-scene lights also need to belong to that scene (already via CreateLight).

            var bounds = CalculateBounds(instance);
            instance.transform.position = -bounds.center + offset;
            bounds = CalculateBounds(instance);

            var maxDim = Mathf.Max(bounds.size.x, bounds.size.y, 0.001f);
            cam.orthographicSize = (maxDim * 0.5f) / Mathf.Max(0.05f, fill);
            cam.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, -Mathf.Max(0.5f, cameraDist)),
                Quaternion.identity);

            rt = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
            };
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0);
            PunchNearBlackToTransparent(tex);
            tex.Apply(false, false);
            RenderTexture.active = prev;

            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[PPE Thumbnails] Studio render exception: " + ex.Message);
            return null;
        }
        finally
        {
            if (rt != null)
            {
                if (cam != null)
                    cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            if (instance != null)
                Object.DestroyImmediate(instance);
            if (cam != null)
                Object.DestroyImmediate(cam.gameObject);
            if (key != null)
                Object.DestroyImmediate(key.gameObject);
            if (fillLight != null)
                Object.DestroyImmediate(fillLight.gameObject);
            if (rim != null)
                Object.DestroyImmediate(rim.gameObject);

            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    static byte[] TryAssetPreviewRender(GameObject source)
    {
        AssetPreview.SetPreviewTextureCacheSize(64);
        Texture2D preview = null;
        for (var i = 0; i < 80; i++)
        {
            preview = AssetPreview.GetAssetPreview(source);
            if (preview != null && !AssetPreview.IsLoadingAssetPreview(source.GetEntityId()))
                break;
            // Pump editor so preview generation can finish.
            System.Threading.Thread.Sleep(25);
        }

        preview = AssetPreview.GetAssetPreview(source);
        if (preview == null)
            preview = AssetPreview.GetMiniThumbnail(source);
        if (preview == null)
            return null;

        // Upscale into a transparent 1024 canvas, centered.
        var canvas = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        var clear = new Color32[TextureSize * TextureSize];
        for (var i = 0; i < clear.Length; i++)
            clear[i] = new Color32(0, 0, 0, 0);
        canvas.SetPixels32(clear);

        var srcPixels = preview.GetPixels32();
        var sw = preview.width;
        var sh = preview.height;
        var scale = Mathf.Min((TextureSize * 0.82f) / sw, (TextureSize * 0.82f) / sh);
        var dw = Mathf.Max(1, Mathf.RoundToInt(sw * scale));
        var dh = Mathf.Max(1, Mathf.RoundToInt(sh * scale));
        var ox = (TextureSize - dw) / 2;
        var oy = (TextureSize - dh) / 2;

        for (var y = 0; y < dh; y++)
        {
            var sy = Mathf.Clamp((y * sh) / dh, 0, sh - 1);
            for (var x = 0; x < dw; x++)
            {
                var sx = Mathf.Clamp((x * sw) / dw, 0, sw - 1);
                var c = srcPixels[sy * sw + sx];
                // AssetPreview often has gray checker/bg — punch near-gray to transparent.
                if (c.a < 8)
                    continue;
                var max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                var min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                if (max - min < 12 && max > 90 && max < 200)
                    continue;
                canvas.SetPixel(ox + x, oy + y, c);
            }
        }

        canvas.Apply(false, false);
        var png = canvas.EncodeToPNG();
        Object.DestroyImmediate(canvas);
        return png;
    }

    static bool HasEnoughOpaquePixels(byte[] pngBytes)
    {
        if (pngBytes == null || pngBytes.Length < 256)
            return false;

        // Cheap heuristic: near-empty transparent PNGs from failed GPU reads are tiny (~15–25 KB).
        // Successful 1024 mesh shots are usually >> 80 KB.
        if (pngBytes.Length < 80000)
            return false;
        return true;
    }

    static void PunchNearBlackToTransparent(Texture2D tex)
    {
        if (tex == null)
            return;
        var pixels = tex.GetPixels32();
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            // Clear pure/near-black backdrop so cards show the dark inset, not a black plane.
            if (c.r < 18 && c.g < 18 && c.b < 18)
                pixels[i] = new Color32(0, 0, 0, 0);
        }

        tex.SetPixels32(pixels);
    }

    static Light CreateLight(Scene scene, string name, Vector3 euler, float intensity)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        go.transform.rotation = Quaternion.Euler(euler);
        return light;
    }

    static void StripForPreview(GameObject root)
    {
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb != null)
                Object.DestroyImmediate(mb);
        }

        foreach (var col in root.GetComponentsInChildren<Collider>(true))
        {
            if (col != null)
                Object.DestroyImmediate(col);
        }

        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb != null)
                Object.DestroyImmediate(rb);
        }

        // Drop any leftover outline materials.
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0)
                continue;
            var kept = new List<Material>(mats.Length);
            for (var i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null)
                    continue;
                if (m.name.IndexOf("Outline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (m.shader != null &&
                    m.shader.name.IndexOf("Outline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                kept.Add(m);
            }

            r.sharedMaterials = kept.ToArray();
            r.enabled = true;
        }
    }

    static Bounds CalculateBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = default;
        var has = false;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            if (!has)
            {
                bounds = r.bounds;
                has = true;
            }
            else
                bounds.Encapsulate(r.bounds);
        }

        if (!has)
            bounds = new Bounds(root.transform.position, Vector3.one * 0.2f);
        return bounds;
    }

    static void ConfigureSpriteImports()
    {
        ConfigureOne(OutputFolder + "/ResponseBag_GasDetector.png");
        ConfigureOne(OutputFolder + "/ResponseBag_AbsorbentSock.png");
        ConfigureOne(OutputFolder + "/ResponseBag_AbsorbentPad.png");
    }

    static void ConfigureOne(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 1024;

        // Unity 6+: spriteMeshType lives on TextureImporterSettings, not TextureImporter.
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
    }

    static void CopyIntoResources()
    {
        CopyOne("ResponseBag_GasDetector.png");
        CopyOne("ResponseBag_AbsorbentSock.png");
        CopyOne("ResponseBag_AbsorbentPad.png");
    }

    static void CopyOne(string fileName)
    {
        var src = OutputFolder + "/" + fileName;
        var dst = ResourcesThumbsFolder + "/" + fileName;
        if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), src)))
            return;
        AssetDatabase.DeleteAsset(dst);
        AssetDatabase.CopyAsset(src, dst);
        ConfigureOne(dst);
    }
}
#endif
