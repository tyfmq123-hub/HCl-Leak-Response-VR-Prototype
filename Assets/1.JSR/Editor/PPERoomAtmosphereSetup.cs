using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Brightens the PPE preparation room: wall/floor materials, ambient fill,
/// directional tweak, and mixed realtime ceiling light panels.
/// Does not move PPE / cabinet / checklist / response bag.
/// </summary>
public static class PPERoomAtmosphereSetup
{
    const string MaterialsFolder = "Assets/1.JSR/Materials/PPE/Environment";
    const string AtmosphereRootName = "PPE_RoomAtmosphere";

    static readonly string[] WallNames =
    {
        "Wall_Back",
        "Wall_Front",
        "Wall_Left_Front",
        "Wall_Left_Back",
        "Wall_Left_Top",
        "Wall_Right",
        // Legacy front-door names (pre Move Doorway To Left Wall)
        "Wall_Front_Left",
        "Wall_Front_Right",
        "Wall_Front_Top",
        "Wall_Left",
    };

    // Soft light warm-neutral upper wall
    static readonly Color WallUpper = new Color(0.88f, 0.89f, 0.90f, 1f);
    // Muted safety blue-gray lower band
    static readonly Color WallLower = new Color(0.62f, 0.70f, 0.78f, 1f);
    // Cleaner industrial floor (slight cool cast, low gloss)
    static readonly Color FloorColor = new Color(0.72f, 0.74f, 0.76f, 1f);
    // Bright ceiling
    static readonly Color CeilingColor = new Color(0.94f, 0.95f, 0.96f, 1f);
    // Neutral-white panel emission
    static readonly Color LightPanelColor = new Color(0.96f, 0.97f, 0.98f, 1f);
    static readonly Color LightPanelEmission = new Color(1.6f, 1.65f, 1.7f, 1f);
    // Soft fill light color
    static readonly Color FillLightColor = new Color(0.96f, 0.98f, 1f, 1f);

    public static void BrightenRoomAtmosphere()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Atmosphere] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== PPE Room Atmosphere Brighten ===");

        EnsureFolder(MaterialsFolder);
        var wallUpper = EnsureLitMaterial("PPE_Wall_Upper", WallUpper, 0.18f, 0f, false, Color.black);
        var wallLower = EnsureLitMaterial("PPE_Wall_Lower", WallLower, 0.22f, 0f, false, Color.black);
        var floor = EnsureLitMaterial("PPE_Floor_Clean", FloorColor, 0.28f, 0f, false, Color.black);
        var ceiling = EnsureLitMaterial("PPE_Ceiling", CeilingColor, 0.12f, 0f, false, Color.black);
        var lightPanel = EnsureLitMaterial("PPE_CeilingLightPanel", LightPanelColor, 0.35f, 0f, true, LightPanelEmission);

        AssignMaterial("Floor", floor, report);
        AssignMaterial("Ceiling", ceiling, report);
        foreach (var wall in WallNames)
            AssignMaterial(wall, wallUpper, report);

        var root = EnsureAtmosphereRoot(report);
        CreateLowerWallBands(root.transform, wallLower, report);
        CreateCeilingLights(root.transform, lightPanel, report);
        TuneDirectionalLight(report);
        TuneAmbient(report);

        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Debug.Log(report.ToString());
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        var parts = path.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    static Material EnsureLitMaterial(
        string name,
        Color baseColor,
        float smoothness,
        float metallic,
        bool emissive,
        Color emission)
    {
        var path = $"{MaterialsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("URP/Lit");

        if (mat == null)
        {
            mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = shader;
        }

        mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", baseColor);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Glossiness", smoothness);

        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emission);
            mat.SetColor("_EmissionColorLDR", emission);
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
        }

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static void AssignMaterial(string objectName, Material mat, StringBuilder report)
    {
        var go = GameObject.Find(objectName);
        if (go == null)
        {
            report.AppendLine($"  SKIP (missing): {objectName}");
            return;
        }

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null)
            renderer = go.GetComponentInChildren<MeshRenderer>();
        if (renderer == null)
        {
            report.AppendLine($"  SKIP (no MeshRenderer): {objectName}");
            return;
        }

        Undo.RecordObject(renderer, "Assign PPE atmosphere material");
        renderer.sharedMaterial = mat;
        EditorUtility.SetDirty(renderer);
        report.AppendLine($"  Material -> {objectName}: {mat.name}");
    }

    static GameObject EnsureAtmosphereRoot(StringBuilder report)
    {
        var existing = GameObject.Find(AtmosphereRootName);
        if (existing != null)
        {
            report.AppendLine("Reused PPE_RoomAtmosphere root.");
            return existing;
        }

        var go = new GameObject(AtmosphereRootName);
        Undo.RegisterCreatedObjectUndo(go, "Create PPE_RoomAtmosphere");
        go.transform.position = Vector3.zero;
        report.AppendLine("Created PPE_RoomAtmosphere root.");
        return go;
    }

    static void CreateLowerWallBands(Transform root, Material mat, StringBuilder report)
    {
        // Door on left wall; front wall is solid. Visual-only wainscot (~0.95 m).
        DestroyBandIfPresent(root, "WallBand_Left");
        DestroyBandIfPresent(root, "WallBand_Front_Left");
        DestroyBandIfPresent(root, "WallBand_Front_Right");
        EnsureBand(root, "WallBand_Back", new Vector3(0f, 0.475f, 2.90f), new Vector3(6.9f, 0.95f, 0.04f), mat);
        EnsureBand(root, "WallBand_Right", new Vector3(3.40f, 0.475f, 0f), new Vector3(0.04f, 0.95f, 5.9f), mat);
        EnsureBand(root, "WallBand_Front", new Vector3(0f, 0.475f, -2.90f), new Vector3(6.9f, 0.95f, 0.04f), mat);
        EnsureBand(root, "WallBand_Left_Front", new Vector3(-3.40f, 0.475f, -1.8f), new Vector3(0.04f, 0.95f, 2.35f), mat);
        EnsureBand(root, "WallBand_Left_Back", new Vector3(-3.40f, 0.475f, 1.8f), new Vector3(0.04f, 0.95f, 2.35f), mat);
        report.AppendLine("Lower wall bands: soft blue-gray (visual only, left doorway).");
    }

    static void DestroyBandIfPresent(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null)
            Undo.DestroyObjectImmediate(t.gameObject);
    }

    static void EnsureBand(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var t = parent.Find(name);
        GameObject go;
        if (t != null)
        {
            go = t.gameObject;
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.name = name;
            Undo.SetTransformParent(go.transform, parent, "Parent wall band");
            var col = go.GetComponent<Collider>();
            if (col != null)
                Undo.DestroyObjectImmediate(col);
        }

        Undo.RecordObject(go.transform, "Place wall band");
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Undo.RecordObject(renderer, "Assign wall band mat");
            renderer.sharedMaterial = mat;
            // Avoid darkening shadows on decorative band in VR
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }
    }

    static void CreateCeilingLights(Transform root, Material panelMat, StringBuilder report)
    {
        var lightsRoot = root.Find("CeilingLights");
        GameObject lightsGo;
        if (lightsRoot != null)
        {
            lightsGo = lightsRoot.gameObject;
        }
        else
        {
            lightsGo = new GameObject("CeilingLights");
            Undo.RegisterCreatedObjectUndo(lightsGo, "Create CeilingLights");
            Undo.SetTransformParent(lightsGo.transform, root, "Parent ceiling lights");
        }

        // 2x2 grid across ~7x6 room, underside ~ y=2.92
        Vector3[] positions =
        {
            new Vector3(-1.6f, 2.92f, -1.3f),
            new Vector3(1.6f, 2.92f, -1.3f),
            new Vector3(-1.6f, 2.92f, 1.2f),
            new Vector3(1.6f, 2.92f, 1.2f),
        };

        for (var i = 0; i < positions.Length; i++)
        {
            var name = $"CeilingLightPanel_{i + 1}";
            var existing = lightsGo.transform.Find(name);
            GameObject panel;
            if (existing != null)
            {
                panel = existing.gameObject;
            }
            else
            {
                panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(panel, "Create " + name);
                panel.name = name;
                Undo.SetTransformParent(panel.transform, lightsGo.transform, "Parent light panel");
                var col = panel.GetComponent<Collider>();
                if (col != null)
                    Undo.DestroyObjectImmediate(col);
            }

            Undo.RecordObject(panel.transform, "Place ceiling panel");
            panel.transform.localPosition = positions[i];
            panel.transform.localRotation = Quaternion.identity;
            panel.transform.localScale = new Vector3(1.9f, 0.06f, 1.1f);

            var renderer = panel.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = panelMat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var lightT = panel.transform.Find("FillLight");
            Light light;
            if (lightT != null)
            {
                light = lightT.GetComponent<Light>();
            }
            else
            {
                var lightGo = new GameObject("FillLight");
                Undo.RegisterCreatedObjectUndo(lightGo, "Create FillLight");
                Undo.SetTransformParent(lightGo.transform, panel.transform, "Parent fill light");
                lightGo.transform.localPosition = new Vector3(0f, -0.12f, 0f);
                light = Undo.AddComponent<Light>(lightGo);
            }

            Undo.RecordObject(light, "Configure fill light");
            light.type = LightType.Point;
            light.color = FillLightColor;
            light.intensity = 1.35f;
            light.range = 7.5f;
            light.shadows = LightShadows.None; // Even, VR-friendly, no harsh shadows
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.renderMode = LightRenderMode.Auto;
        }

        report.AppendLine("Ceiling lights: 4 rect panels + realtime point fills (no shadows).");
    }

    static void TuneDirectionalLight(StringBuilder report)
    {
        var light = Object.FindAnyObjectByType<Light>();
        Light directional = null;
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
        for (var i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
            {
                directional = lights[i];
                break;
            }
        }

        if (directional == null)
        {
            report.AppendLine("WARNING: No Directional Light found.");
            return;
        }

        Undo.RecordObject(directional, "Tune directional light");
        directional.color = new Color(0.95f, 0.97f, 1f, 1f); // Soft cool-neutral white
        directional.intensity = 1.25f;
        directional.shadows = LightShadows.Soft;
        directional.shadowStrength = 0.45f; // Softer, less moody
        EditorUtility.SetDirty(directional);
        report.AppendLine("Directional Light: cool-white, intensity 1.25, soft shadows 0.45.");
    }

    static void TuneAmbient(StringBuilder report)
    {
        // Flat ambient for even VR readability (no dark skybox corner sinks).
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.65f, 0.68f, 1f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 0.85f;
        report.AppendLine("Ambient: Flat fill (0.62, 0.65, 0.68) for even room brightness.");
    }
}
