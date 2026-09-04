using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PlantUtilityTools.Editor.BlenderFBX
{
    internal static class BlenderSidecarMaterialTool
    {
        private const string GeneratedRoot = "Assets/PlantUtilityTools/Generated/BlenderFBX";

        [Serializable]
        private sealed class MaterialSidecar
        {
            public int version;
            public string fbx;
            public MaterialRecord[] materials;
        }

        [Serializable]
        private sealed class MaterialRecord
        {
            public string name;
            public float[] baseColor;
            public float metallic;
            public float roughness;
        }

        private enum MaterialMode
        {
            UrpLit,
            CelShader
        }

        private static void ApplyUrpLit()
        {
            Apply(MaterialMode.UrpLit);
        }

        private static bool ValidateApplyUrpLit()
        {
            return TryGetSelectedFbxPath(out _);
        }

        private static void ApplyCelShader()
        {
            Apply(MaterialMode.CelShader);
        }

        private static bool ValidateApplyCelShader()
        {
            return TryGetSelectedFbxPath(out _);
        }

        private static void Apply(MaterialMode mode)
        {
            if (!TryGetSelectedFbxPath(out string fbxPath))
            {
                EditorUtility.DisplayDialog("Blender FBX Materials", "Project 창에서 FBX 파일 하나를 선택해 주세요.", "확인");
                return;
            }

            string sidecarPath = Path.ChangeExtension(fbxPath, null) + ".materials.json";
            TextAsset sidecarAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(sidecarPath);
            if (sidecarAsset == null)
            {
                EditorUtility.DisplayDialog(
                    "Blender FBX Materials",
                    $"재질 사이드카 파일을 찾을 수 없습니다.\n{sidecarPath}",
                    "확인");
                return;
            }

            MaterialSidecar sidecar;
            try
            {
                sidecar = JsonUtility.FromJson<MaterialSidecar>(sidecarAsset.text);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Blender FBX Materials", "재질 JSON을 파싱하지 못했습니다. Console을 확인해 주세요.", "확인");
                return;
            }

            if (sidecar == null || sidecar.materials == null)
            {
                EditorUtility.DisplayDialog("Blender FBX Materials", "재질 JSON의 materials 배열이 올바르지 않습니다.", "확인");
                return;
            }

            Shader shader = FindShader(mode);
            if (shader == null)
                return;

            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                EditorUtility.DisplayDialog("Blender FBX Materials", "선택한 에셋의 ModelImporter를 가져올 수 없습니다.", "확인");
                return;
            }

            string modelName = SanitizeFileName(Path.GetFileNameWithoutExtension(fbxPath));
            string modeFolder = mode == MaterialMode.UrpLit ? "URPLit" : "CelShader";
            string outputFolder = $"{GeneratedRoot}/{modeFolder}/{modelName}";
            EnsureAssetFolder(outputFolder);

            int completedCount = 0;
            foreach (MaterialRecord record in sidecar.materials)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.name))
                    continue;

                string materialPath = $"{outputFolder}/{SanitizeFileName(record.name)}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                else
                {
                    material.shader = shader;
                }

                ApplyValues(material, record, mode);
                EditorUtility.SetDirty(material);

                var sourceIdentifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), record.name);
                importer.AddRemap(sourceIdentifier, material);
                completedCount++;
            }

            AssetDatabase.SaveAssets();
            importer.SaveAndReimport();

            EditorUtility.DisplayDialog(
                "Blender FBX Materials",
                $"{completedCount}개 재질을 적용했습니다.\n생성 위치: {outputFolder}",
                "확인");
        }

        private static Shader FindShader(MaterialMode mode)
        {
            if (mode == MaterialMode.CelShader)
            {
                Shader celShader = Shader.Find("Plant Utility Tools/Cel Lit");
                if (celShader == null)
                    EditorUtility.DisplayDialog("Blender FBX Materials", "Shader 'Plant Utility Tools/Cel Lit'을 찾을 수 없습니다.", "확인");
                return celShader;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                EditorUtility.DisplayDialog("Blender FBX Materials", "URP Lit 또는 Standard Shader를 찾을 수 없습니다.", "확인");
            return shader;
        }

        private static void ApplyValues(Material material, MaterialRecord record, MaterialMode mode)
        {
            Color baseColor = ReadColor(record.baseColor);
            float metallic = Mathf.Clamp01(record.metallic);
            float roughness = Mathf.Clamp01(record.roughness);

            if (mode == MaterialMode.UrpLit)
            {
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", baseColor);
                else if (material.HasProperty("_Color"))
                    material.SetColor("_Color", baseColor);

                if (material.HasProperty("_Metallic"))
                    material.SetFloat("_Metallic", metallic);
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", 1f - roughness);
            }
            else
            {
                material.SetColor("_BaseColor", baseColor);
                material.SetColor("_ShadeColor", new Color(baseColor.r * 0.55f, baseColor.g * 0.55f, baseColor.b * 0.55f, baseColor.a));
                material.SetFloat("_ShadowThreshold", 0.5f);
                material.SetFloat("_ShadowSmoothness", 0.04f);
                material.SetFloat("_SpecularStrength", Mathf.Lerp(0.05f, 0.35f, metallic));
                material.SetFloat("_SpecularSize", 1f - roughness);
            }

            material.enableInstancing = true;
        }

        private static Color ReadColor(float[] values)
        {
            if (values == null || values.Length < 3)
                return Color.white;

            float alpha = values.Length > 3 ? values[3] : 1f;
            return new Color(
                Mathf.Clamp01(values[0]),
                Mathf.Clamp01(values[1]),
                Mathf.Clamp01(values[2]),
                Mathf.Clamp01(alpha));
        }

        private static bool TryGetSelectedFbxPath(out string path)
        {
            path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path)
                && string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidCharacter, '_');
            return value;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
