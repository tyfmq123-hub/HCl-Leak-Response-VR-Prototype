using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BlenderSidecarMaterialTool
{
    [Serializable]
    private sealed class MaterialRecord
    {
        public string name;
        public float[] baseColor;
        public float metallic;
        public float roughness;
    }

    [Serializable]
    private sealed class MaterialSidecar
    {
        public int version;
        public string fbx;
        public MaterialRecord[] materials;
    }

    private enum MaterialMode
    {
        UrpLit,
        CelShader
    }

    private const string RootOutputFolder =
        "Assets/GeneratedMaterials/BlenderFBX";

    private const string UrpLitShaderName =
        "Universal Render Pipeline/Lit";

    private const string CelShaderName =
        "Custom/Blender Cel Shader";

    // ---------------------------------------------------------------------
    // 기존 JSON sidecar 방식
    // ---------------------------------------------------------------------

    [MenuItem("Tools/Blender FBX/Apply Sidecar Materials - URP Lit")]
    private static void ApplySidecarUrpLit()
    {
        ApplySidecarMaterials(MaterialMode.UrpLit);
    }

    [MenuItem("Tools/Blender FBX/Apply Sidecar Materials - Cel Shader")]
    private static void ApplySidecarCelShader()
    {
        ApplySidecarMaterials(MaterialMode.CelShader);
    }

    // ---------------------------------------------------------------------
    // Tripo / AI 모델 Base Color 이미지 방식
    // ---------------------------------------------------------------------

    [MenuItem("Tools/Blender FBX/Apply BaseColor Texture - URP Lit")]
    private static void ApplyBaseColorTextureUrpLit()
    {
        ApplyBaseColorTexture(MaterialMode.UrpLit);
    }

    [MenuItem("Tools/Blender FBX/Apply BaseColor Texture - Cel Shader")]
    private static void ApplyBaseColorTextureCelShader()
    {
        ApplyBaseColorTexture(MaterialMode.CelShader);
    }

    [MenuItem(
        "Tools/Blender FBX/Apply Sidecar Materials - URP Lit",
        true
    )]
    private static bool ValidateSidecarUrpLit()
    {
        return HasSelectedFbx();
    }

    [MenuItem(
        "Tools/Blender FBX/Apply Sidecar Materials - Cel Shader",
        true
    )]
    private static bool ValidateSidecarCelShader()
    {
        return HasSelectedFbx();
    }

    [MenuItem(
        "Tools/Blender FBX/Apply BaseColor Texture - URP Lit",
        true
    )]
    private static bool ValidateBaseColorUrpLit()
    {
        return HasSelectedFbx();
    }

    [MenuItem(
        "Tools/Blender FBX/Apply BaseColor Texture - Cel Shader",
        true
    )]
    private static bool ValidateBaseColorCelShader()
    {
        return HasSelectedFbx();
    }

    // ---------------------------------------------------------------------
    // JSON sidecar 처리
    // ---------------------------------------------------------------------

    private static void ApplySidecarMaterials(MaterialMode mode)
    {
        if (!TryGetSelectedFbx(out string fbxPath))
            return;

        string jsonPath = Path.ChangeExtension(
            fbxPath,
            ".materials.json"
        ).Replace("\\", "/");

        TextAsset sidecarAsset =
            AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);

        if (sidecarAsset == null)
        {
            EditorUtility.DisplayDialog(
                "재질 JSON 없음",
                $"FBX와 같은 폴더에 아래 파일이 필요합니다.\n\n" +
                $"{Path.GetFileNameWithoutExtension(fbxPath)}.materials.json",
                "확인"
            );
            return;
        }

        MaterialSidecar sidecar;

        try
        {
            sidecar =
                JsonUtility.FromJson<MaterialSidecar>(sidecarAsset.text);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "JSON 오류",
                $"재질 JSON을 읽는 중 오류가 발생했습니다.\n\n" +
                exception.Message,
                "확인"
            );
            return;
        }

        if (sidecar == null ||
            sidecar.materials == null ||
            sidecar.materials.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "JSON 오류",
                "재질 JSON 안에 유효한 materials 데이터가 없습니다.",
                "확인"
            );
            return;
        }

        if (!TryGetImporterAndShader(
                fbxPath,
                mode,
                out ModelImporter importer,
                out Shader targetShader))
        {
            return;
        }

        string outputFolder =
            GetOutputFolder(fbxPath, mode, "Sidecar");

        EnsureFolder(outputFolder);

        int appliedCount = 0;

        foreach (MaterialRecord record in sidecar.materials)
        {
            if (record == null ||
                string.IsNullOrWhiteSpace(record.name))
            {
                continue;
            }

            Color color = ReadColor(record.baseColor);

            string materialPath =
                $"{outputFolder}/{Sanitize(record.name)}.mat";

            Material material =
                LoadOrCreateMaterial(materialPath, targetShader);

            ApplySidecarValues(
                material,
                color,
                record.metallic,
                record.roughness,
                mode
            );

            RemapMaterial(importer, record.name, material);
            appliedCount++;
        }

        CompleteImport(
            importer,
            fbxPath,
            appliedCount,
            mode,
            outputFolder,
            "Sidecar 재질"
        );
    }

    // ---------------------------------------------------------------------
    // Base Color 이미지 처리
    // ---------------------------------------------------------------------

    private static void ApplyBaseColorTexture(MaterialMode mode)
    {
        if (!TryGetSelectedFbx(out string fbxPath))
            return;

        string texturePath = FindBaseColorTexturePath(fbxPath);

        if (string.IsNullOrWhiteSpace(texturePath))
        {
            EditorUtility.DisplayDialog(
                "Base Color 이미지 없음",
                "FBX와 같은 폴더에서 Base Color 이미지를 찾지 못했습니다.\n\n" +
                "지원 이름 예시:\n" +
                "모델명_basecolor.jpg\n" +
                "모델명_base_color.png\n" +
                "모델명_albedo.png\n" +
                "모델명_diffuse.png\n\n" +
                "FBX와 이미지 파일을 같은 폴더에 넣어주세요.",
                "확인"
            );
            return;
        }

        if (!TryGetImporterAndShader(
                fbxPath,
                mode,
                out ModelImporter importer,
                out Shader targetShader))
        {
            return;
        }

        Texture2D baseColorTexture =
            AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

        if (baseColorTexture == null)
        {
            EditorUtility.DisplayDialog(
                "텍스처 오류",
                $"이미지를 Texture2D로 불러오지 못했습니다.\n\n{texturePath}",
                "확인"
            );
            return;
        }

        ConfigureBaseColorTexture(texturePath);

        // FBX 내부에 포함된 원본 재질 슬롯을 수집합니다.
        Material[] embeddedMaterials =
            AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<Material>()
                .Where(material => material != null)
                .ToArray();

        if (embeddedMaterials.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "FBX 재질 슬롯 없음",
                "선택한 FBX에서 재질 슬롯을 찾지 못했습니다.\n\n" +
                "Tripo에서 Texture/Material 포함 옵션으로 다시 내보냈는지 확인하세요.",
                "확인"
            );
            return;
        }

        string outputFolder =
            GetOutputFolder(fbxPath, mode, "BaseColorTexture");

        EnsureFolder(outputFolder);

        int appliedCount = 0;

        foreach (Material sourceMaterial in embeddedMaterials)
        {
            string sourceName = string.IsNullOrWhiteSpace(sourceMaterial.name)
                ? Path.GetFileNameWithoutExtension(fbxPath)
                : sourceMaterial.name;

            string materialPath =
                $"{outputFolder}/{Sanitize(sourceName)}.mat";

            Material material =
                LoadOrCreateMaterial(materialPath, targetShader);

            ApplyTextureValues(
                material,
                baseColorTexture,
                mode
            );

            RemapMaterial(importer, sourceName, material);
            appliedCount++;
        }

        CompleteImport(
            importer,
            fbxPath,
            appliedCount,
            mode,
            outputFolder,
            $"Base Color 텍스처\n{Path.GetFileName(texturePath)}"
        );
    }

    private static string FindBaseColorTexturePath(string fbxPath)
    {
        string folder = Path.GetDirectoryName(fbxPath)
            ?.Replace("\\", "/");

        if (string.IsNullOrWhiteSpace(folder))
            return null;

        string modelName =
            Path.GetFileNameWithoutExtension(fbxPath);

        string[] supportedExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".tga",
            ".tif",
            ".tiff"
        };

        string[] preferredSuffixes =
        {
            "_basecolor",
            "_base_color",
            "-basecolor",
            "-base_color",
            "_albedo",
            "-albedo",
            "_diffuse",
            "-diffuse",
            "_color",
            "-color"
        };

        foreach (string suffix in preferredSuffixes)
        {
            foreach (string extension in supportedExtensions)
            {
                string candidate =
                    $"{folder}/{modelName}{suffix}{extension}";

                if (File.Exists(candidate))
                    return candidate;
            }
        }

        // 이름이 정확히 일치하지 않아도 같은 폴더에서
        // basecolor / albedo / diffuse 파일을 탐색합니다.
        string absoluteFolder =
            Path.GetFullPath(folder);

        if (!Directory.Exists(absoluteFolder))
            return null;

        string[] files = Directory.GetFiles(absoluteFolder);

        foreach (string file in files)
        {
            string extension =
                Path.GetExtension(file).ToLowerInvariant();

            if (!supportedExtensions.Contains(extension))
                continue;

            string lowerName =
                Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

            if (!lowerName.Contains("basecolor") &&
                !lowerName.Contains("base_color") &&
                !lowerName.Contains("albedo") &&
                !lowerName.Contains("diffuse"))
            {
                continue;
            }

            string unityPath =
                file.Replace("\\", "/");

            int assetsIndex =
                unityPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);

            if (assetsIndex >= 0)
                return unityPath.Substring(assetsIndex);
        }

        return null;
    }

    private static void ConfigureBaseColorTexture(string texturePath)
    {
        TextureImporter textureImporter =
            AssetImporter.GetAtPath(texturePath) as TextureImporter;

        if (textureImporter == null)
            return;

        bool changed = false;

        if (!textureImporter.sRGBTexture)
        {
            textureImporter.sRGBTexture = true;
            changed = true;
        }

        if (textureImporter.textureType != TextureImporterType.Default)
        {
            textureImporter.textureType = TextureImporterType.Default;
            changed = true;
        }

        if (changed)
            textureImporter.SaveAndReimport();
    }

    // ---------------------------------------------------------------------
    // 공통 처리
    // ---------------------------------------------------------------------

    private static bool TryGetSelectedFbx(out string fbxPath)
    {
        fbxPath = AssetDatabase.GetAssetPath(Selection.activeObject);

        if (string.IsNullOrWhiteSpace(fbxPath) ||
            !fbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog(
                "FBX 선택 필요",
                "Project 창에서 FBX 파일 자체를 선택한 뒤 실행하세요.",
                "확인"
            );
            return false;
        }

        return true;
    }

    private static bool TryGetImporterAndShader(
        string fbxPath,
        MaterialMode mode,
        out ModelImporter importer,
        out Shader shader)
    {
        importer =
            AssetImporter.GetAtPath(fbxPath) as ModelImporter;

        shader = FindTargetShader(mode);

        if (importer == null)
        {
            EditorUtility.DisplayDialog(
                "FBX 오류",
                "선택한 FBX의 ModelImporter를 찾지 못했습니다.",
                "확인"
            );
            return false;
        }

        return shader != null;
    }

    private static Shader FindTargetShader(MaterialMode mode)
    {
        if (mode == MaterialMode.CelShader)
        {
            Shader celShader = Shader.Find(CelShaderName);

            if (celShader == null)
            {
                EditorUtility.DisplayDialog(
                    "Cel Shader 없음",
                    $"프로젝트에서 아래 셰이더를 찾지 못했습니다.\n\n" +
                    $"{CelShaderName}\n\n" +
                    "BlenderCelShader.shader 파일을 " +
                    "Assets/Shaders 폴더에 넣은 뒤 다시 실행하세요.",
                    "확인"
                );
            }

            return celShader;
        }

        Shader urpLitShader = Shader.Find(UrpLitShaderName);

        if (urpLitShader != null)
            return urpLitShader;

        Shader standardShader = Shader.Find("Standard");

        if (standardShader == null)
        {
            EditorUtility.DisplayDialog(
                "Shader 오류",
                "URP/Lit 또는 Standard 셰이더를 찾지 못했습니다.",
                "확인"
            );
        }

        return standardShader;
    }

    private static Material LoadOrCreateMaterial(
        string materialPath,
        Shader shader)
    {
        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            material.shader = shader;
        }

        return material;
    }

    private static void ApplySidecarValues(
        Material material,
        Color color,
        float metallic,
        float roughness,
        MaterialMode mode)
    {
        float clampedMetallic = Mathf.Clamp01(metallic);
        float smoothness = Mathf.Clamp01(1f - roughness);

        SetMaterialColor(material, color);
        SetMaterialSurfaceValues(
            material,
            clampedMetallic,
            smoothness,
            mode
        );

        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
    }

    private static void ApplyTextureValues(
        Material material,
        Texture2D baseColorTexture,
        MaterialMode mode)
    {
        // 텍스처 고유 색이 그대로 보이도록 흰색 틴트를 사용합니다.
        SetMaterialColor(material, Color.white);

        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", baseColorTexture);

        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", baseColorTexture);

        SetMaterialSurfaceValues(
            material,
            0f,
            0.45f,
            mode
        );

        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
    }

    private static void SetMaterialColor(
        Material material,
        Color color)
    {
        material.color = color;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private static void SetMaterialSurfaceValues(
        Material material,
        float metallic,
        float smoothness,
        MaterialMode mode)
    {
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);

        if (mode != MaterialMode.CelShader)
            return;

        if (material.HasProperty("_CelThreshold"))
            material.SetFloat("_CelThreshold", 0.5f);

        if (material.HasProperty("_CelSoftness"))
            material.SetFloat("_CelSoftness", 0.04f);

        if (material.HasProperty("_ShadowStrength"))
            material.SetFloat("_ShadowStrength", 0.55f);

        if (material.HasProperty("_SpecularSize"))
            material.SetFloat("_SpecularSize", 0.08f);

        if (material.HasProperty("_SpecularStrength"))
            material.SetFloat("_SpecularStrength", 0.16f);
    }

    private static void RemapMaterial(
        ModelImporter importer,
        string sourceName,
        Material material)
    {
        var sourceId =
            new AssetImporter.SourceAssetIdentifier(
                typeof(Material),
                sourceName
            );

        importer.RemoveRemap(sourceId);
        importer.AddRemap(sourceId, material);
    }

    private static void CompleteImport(
        ModelImporter importer,
        string fbxPath,
        int appliedCount,
        MaterialMode mode,
        string outputFolder,
        string sourceDescription)
    {
        importer.materialImportMode =
            ModelImporterMaterialImportMode.ImportStandard;

        EditorUtility.SetDirty(importer);
        AssetDatabase.SaveAssets();

        importer.SaveAndReimport();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string modeName = mode == MaterialMode.UrpLit
            ? "URP Lit"
            : "Cel Shader";

        EditorUtility.DisplayDialog(
            "적용 완료",
            $"{modeName} 재질 {appliedCount}개를 생성하고 " +
            $"FBX에 연결했습니다.\n\n" +
            $"입력:\n{sourceDescription}\n\n" +
            $"생성 위치:\n{outputFolder}",
            "확인"
        );

        Selection.activeObject =
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fbxPath);

        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    private static string GetOutputFolder(
        string fbxPath,
        MaterialMode mode,
        string sourceType)
    {
        string modelName =
            Sanitize(Path.GetFileNameWithoutExtension(fbxPath));

        string modeFolder = mode == MaterialMode.UrpLit
            ? "URPLit"
            : "CelShader";

        return
            $"{RootOutputFolder}/{sourceType}/{modeFolder}/{modelName}";
    }

    private static bool HasSelectedFbx()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);

        return !string.IsNullOrWhiteSpace(path) &&
               path.EndsWith(
                   ".fbx",
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private static Color ReadColor(float[] values)
    {
        if (values == null || values.Length < 3)
            return Color.gray;

        float alpha = values.Length >= 4
            ? values[3]
            : 1f;

        return new Color(
            values[0],
            values[1],
            values[2],
            alpha
        );
    }

    private static void EnsureFolder(string fullPath)
    {
        string[] parts = fullPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";

            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value
            .Replace('/', '_')
            .Replace('\\', '_')
            .Trim();
    }
}
