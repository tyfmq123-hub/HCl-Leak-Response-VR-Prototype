using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity Editor menu tool that generates an outdoor concrete floor material
/// with procedural Base Color, Normal and Roughness/Height-style grayscale textures.
/// 
/// Import this script under Assets/Editor, then run:
/// Tools > Plant Utility Tools > Materials > Create Outdoor Concrete Floor Material
/// </summary>
public static class CreateOutdoorConcreteFloorMaterial
{
    private const int Size = 1024;
    private const string OutputFolder = "Assets/PlantUtilityTools/Generated/OutdoorConcrete";

    public static void CreateMaterial()
    {
        Directory.CreateDirectory(OutputFolder);

        Texture2D albedo = null;
        Texture2D height = null;
        Texture2D normal = null;
        Texture2D roughness = null;

        try
        {
            albedo = GenerateAlbedo(Size, Size);
            height = GenerateHeight(Size, Size);
            normal = GenerateNormalFromHeight(height, strength: 2.2f);
            roughness = GenerateRoughness(Size, Size);

            string albedoPath = $"{OutputFolder}/T_OutdoorConcrete_Albedo.png";
            string heightPath = $"{OutputFolder}/T_OutdoorConcrete_Height.png";
            string normalPath = $"{OutputFolder}/T_OutdoorConcrete_Normal.png";
            string roughnessPath = $"{OutputFolder}/T_OutdoorConcrete_Roughness.png";
            string matPath = $"{OutputFolder}/Mat_Outdoor_Concrete_Floor.mat";

            SavePng(albedo, albedoPath);
            SavePng(height, heightPath);
            SavePng(normal, normalPath);
            SavePng(roughness, roughnessPath);

            AssetDatabase.ImportAsset(albedoPath);
            AssetDatabase.ImportAsset(heightPath);
            AssetDatabase.ImportAsset(normalPath);
            AssetDatabase.ImportAsset(roughnessPath);

            ConfigureTextureImporter(albedoPath, TextureImporterType.Default, false);
            ConfigureTextureImporter(heightPath, TextureImporterType.Default, false);
            ConfigureTextureImporter(roughnessPath, TextureImporterType.Default, false);
            ConfigureTextureImporter(normalPath, TextureImporterType.NormalMap, true);

            AssetDatabase.Refresh();

            Texture2D importedAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            Texture2D importedNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
                if (shader != null)
                    Debug.LogWarning("URP Lit shader를 찾지 못해서 Standard shader로 생성했습니다.");
            }

            if (shader == null)
            {
                Debug.LogError("URP Lit / Standard 셰이더를 찾지 못해 Material을 생성할 수 없습니다.");
                return;
            }

            Material material = new Material(shader);
            material.name = "Mat_Outdoor_Concrete_Floor";

            // URP Lit property names
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", importedAlbedo);
                material.SetColor("_BaseColor", Color.white);
            }

            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", importedNormal);
                material.EnableKeyword("_NORMALMAP");
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.0f);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.22f);
            }

            // Standard shader fallback property names
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", importedAlbedo);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.22f);
            }

            if (material.HasProperty("_BumpScale"))
            {
                material.SetFloat("_BumpScale", 0.6f);
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(material, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(material);
                material = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(material, matPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = material;

            Debug.Log($"외부 콘크리트 바닥 Material 생성 완료: {matPath}");
        }
        finally
        {
            DestroyTemporaryTexture(albedo);
            DestroyTemporaryTexture(height);
            DestroyTemporaryTexture(normal);
            DestroyTemporaryTexture(roughness);
        }
    }

    static void DestroyTemporaryTexture(Texture2D texture)
    {
        if (texture == null)
            return;

        Object.DestroyImmediate(texture);
    }

    private static Texture2D GenerateAlbedo(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color baseA = HexToColor("#8A8780");
        Color baseB = HexToColor("#6F6D67");
        Color baseC = HexToColor("#A39F96");

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (float)x / width;
                float ny = (float)y / height;

                float broad = Mathf.PerlinNoise(nx * 5.0f + 13.2f, ny * 5.0f + 4.7f);
                float fine = Mathf.PerlinNoise(nx * 45.0f + 2.1f, ny * 45.0f + 9.6f);
                float micro = RandomHash(x, y) * 0.08f;

                Color c = Color.Lerp(baseB, baseC, broad * 0.65f + fine * 0.25f + micro);
                c = Color.Lerp(c, baseA, 0.45f);

                // Expansion joint-like subtle lines
                float lineX = ThinLine(nx, 0.50f, 0.0025f) * 0.18f;
                float lineY = ThinLine(ny, 0.50f, 0.0025f) * 0.12f;
                c *= 1.0f - lineX - lineY;

                // Small dark speckles
                float speckle = RandomHash(x * 17 + 11, y * 23 + 7);
                if (speckle > 0.985f)
                {
                    c *= 0.45f;
                }

                // Small pale aggregate marks
                float aggregate = RandomHash(x * 31 + 5, y * 29 + 19);
                if (aggregate > 0.992f)
                {
                    c = Color.Lerp(c, Color.white, 0.22f);
                }

                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return tex;
    }

    private static Texture2D GenerateHeight(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (float)x / width;
                float ny = (float)y / height;

                float broad = Mathf.PerlinNoise(nx * 7.0f + 3.5f, ny * 7.0f + 8.1f);
                float fine = Mathf.PerlinNoise(nx * 60.0f + 11.0f, ny * 60.0f + 1.3f);
                float value = 0.46f + broad * 0.18f + fine * 0.08f;

                // subtle cracks
                float crack1 = Mathf.Abs(Mathf.PerlinNoise(nx * 12.0f, ny * 12.0f) - 0.5f);
                if (crack1 < 0.018f)
                {
                    value -= 0.10f;
                }

                float jointX = ThinLine(nx, 0.50f, 0.0035f) * 0.12f;
                float jointY = ThinLine(ny, 0.50f, 0.0035f) * 0.10f;
                value -= jointX + jointY;

                value = Mathf.Clamp01(value);
                tex.SetPixel(x, y, new Color(value, value, value, 1));
            }
        }

        tex.Apply();
        return tex;
    }

    private static Texture2D GenerateNormalFromHeight(Texture2D height, float strength)
    {
        int width = height.width;
        int h = height.height;
        Texture2D normal = new Texture2D(width, h, TextureFormat.RGBA32, false);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float left = height.GetPixel((x - 1 + width) % width, y).r;
                float right = height.GetPixel((x + 1) % width, y).r;
                float down = height.GetPixel(x, (y - 1 + h) % h).r;
                float up = height.GetPixel(x, (y + 1) % h).r;

                Vector3 n = new Vector3((left - right) * strength, (down - up) * strength, 1.0f).normalized;
                Color c = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
                normal.SetPixel(x, y, c);
            }
        }

        normal.Apply();
        return normal;
    }

    private static Texture2D GenerateRoughness(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (float)x / width;
                float ny = (float)y / height;
                float noise = Mathf.PerlinNoise(nx * 18.0f + 21.0f, ny * 18.0f + 15.0f);
                float value = Mathf.Clamp01(0.72f + noise * 0.22f);
                tex.SetPixel(x, y, new Color(value, value, value, 1));
            }
        }

        tex.Apply();
        return tex;
    }

    private static void SavePng(Texture2D texture, string assetPath)
    {
        byte[] bytes = texture.EncodeToPNG();
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
        File.WriteAllBytes(fullPath, bytes);
    }

    private static void ConfigureTextureImporter(string assetPath, TextureImporterType type, bool isNormalMap)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

        importer.textureType = type;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.sRGBTexture = !isNormalMap;
        importer.SaveAndReimport();
    }

    private static Color HexToColor(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out Color color))
        {
            return color;
        }

        return Color.gray;
    }

    private static float ThinLine(float value, float center, float width)
    {
        float d = Mathf.Abs(value - center);
        if (d > width) return 0f;
        return 1f - (d / width);
    }

    private static float RandomHash(int x, int y)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263;
            n = (n ^ (n >> 13)) * 1274126177;
            n = n ^ (n >> 16);
            return (n & 0x7fffffff) / 2147483647f;
        }
    }
}
