using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Creates a Dynamic Hangul TMP font under Resources for device builds.
/// Prefers project Pretendard and correctly saves material + atlas sub-assets.
/// </summary>
public static class PPEHangulFontSetup
{
    const string ResourcesFolder = "Assets/Resources/PPE";
    const string AssetPath = ResourcesFolder + "/PPE_HangulFont.asset";
    const string PretendardPath = "Assets/1.JSR/Fonts/PPE/Pretendard-SemiBold.otf";
    const string PretendardResourcesPath = "Assets/1.JSR/Resources/Pretendard-SemiBold.otf";

    public static void CreateHangulFontAsset()
    {
        CreateHangulFontAsset(overwriteWithoutPrompt: false);
    }

    public static void CreateHangulFontAsset(bool overwriteWithoutPrompt)
    {
        EnsureFolders();

        AssetDatabase.ImportAsset(PretendardPath, ImportAssetOptions.ForceUpdate);
        if (File.Exists(PretendardResourcesPath))
            AssetDatabase.ImportAsset(PretendardResourcesPath, ImportAssetOptions.ForceUpdate);

        var sourceFont = FindSourceFont();
        if (sourceFont == null)
        {
            EditorUtility.DisplayDialog(
                "PPE Hangul Font",
                "한글용 폰트를 찾지 못했습니다.\n\n" +
                $"예상 경로: {PretendardPath}",
                "OK");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath) != null)
        {
            if (!overwriteWithoutPrompt &&
                !EditorUtility.DisplayDialog(
                    "PPE Hangul Font",
                    $"{AssetPath} 가 이미 있습니다. 덮어쓸까요?",
                    "Overwrite",
                    "Cancel"))
                return;

            AssetDatabase.DeleteAsset(AssetPath);
        }

        var fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic);

        if (fontAsset == null)
        {
            Debug.LogError("[PPE Hangul Font] TMP_FontAsset.CreateFontAsset failed.");
            return;
        }

        fontAsset.name = "PPE_HangulFont";

        var seed =
            "내산성 화학보호복장화전면형호흡보호구안면보호장갑을를착용했습니다." +
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ";
        fontAsset.TryAddCharacters(seed);

        AssetDatabase.CreateAsset(fontAsset, AssetPath);

        // Critical: CreateAsset alone often drops material/atlas refs — keep them as sub-assets.
        if (fontAsset.material != null)
        {
            fontAsset.material.name = "PPE_HangulFont Atlas Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        if (fontAsset.atlasTextures != null)
        {
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                var tex = fontAsset.atlasTextures[i];
                if (tex == null)
                    continue;
                tex.name = $"PPE_HangulFont Atlas {i}";
                AssetDatabase.AddObjectToAsset(tex, fontAsset);
            }
        }

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

        var saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (saved != null && saved.material == null)
            Debug.LogError("[PPE Hangul Font] Saved asset still has null material.", saved);
        else
            Debug.Log($"[PPE Hangul Font] Created {AssetPath} from '{sourceFont.name}'. material={(saved != null && saved.material != null)}", saved);

        Selection.activeObject = saved;
        EditorGUIUtility.PingObject(saved);

        if (!overwriteWithoutPrompt)
        {
            EditorUtility.DisplayDialog(
                "PPE Hangul Font",
                $"생성 완료:\n{AssetPath}\n소스: {sourceFont.name}",
                "OK");
        }
    }

    static void EnsureFolders()
    {
        if (!Directory.Exists("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!Directory.Exists(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "PPE");
    }

    static Font FindSourceFont()
    {
        var pretendard = AssetDatabase.LoadAssetAtPath<Font>(PretendardPath);
        if (pretendard != null)
            return pretendard;

        pretendard = AssetDatabase.LoadAssetAtPath<Font>(PretendardResourcesPath);
        if (pretendard != null)
            return pretendard;

        var guids = AssetDatabase.FindAssets("t:Font", new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var lower = path.ToLowerInvariant();
            if (lower.Contains("pretendard") ||
                lower.Contains("malgun") ||
                lower.Contains("nanum") ||
                lower.Contains("noto") ||
                lower.Contains("gothic") ||
                lower.Contains("hangul") ||
                lower.Contains("kr"))
            {
                var projectFont = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (projectFont != null)
                    return projectFont;
            }
        }

        return Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR" },
            48);
    }
}
