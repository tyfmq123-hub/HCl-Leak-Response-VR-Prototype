using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VRProject.LeakResponse.Editor
{
    internal static class P102PretendardFontAssetGenerator
    {
        const string SourceFontPath = "Assets/3.SSH/Font/Pretendard-SemiBold.otf";
        const string ResourceFolderPath = "Assets/3.SSH/Resources/P102";
        const string FontAssetPath = ResourceFolderPath + "/Pretendard-SemiBold SDF.asset";

        static void EnsureFontAssetOnce()
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
                return;

            UnityEngine.Object occupiedAsset = AssetDatabase.LoadMainAssetAtPath(FontAssetPath);
            if (occupiedAsset != null)
            {
                Debug.LogError(
                    $"[P102 Font] '{FontAssetPath}' exists but is not a TMP_FontAsset. It was not overwritten.",
                    occupiedAsset);
                return;
            }

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                Debug.LogError($"[P102 Font] Required source font '{SourceFontPath}' is missing or not imported as Font.");
                return;
            }

            EnsureFolder(ResourceFolderPath);
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                90,
                9,
                GlyphRenderMode.SDFAA,
                2048,
                2048,
                AtlasPopulationMode.Dynamic,
                true);
            if (fontAsset == null)
            {
                Debug.LogError($"[P102 Font] TMP failed to create a font asset from '{SourceFontPath}'.", sourceFont);
                return;
            }

            fontAsset.name = "Pretendard-SemiBold SDF";
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            Texture2D atlasTexture = fontAsset.atlasTexture;
            if (atlasTexture != null && !EditorUtility.IsPersistent(atlasTexture))
                AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
            Material material = fontAsset.material;
            if (material != null && !EditorUtility.IsPersistent(material))
                AssetDatabase.AddObjectToAsset(material, fontAsset);

            string preloadCharacters = BuildPreloadCharacters();
            if (!fontAsset.TryAddCharacters(preloadCharacters, out string missingCharacters, true))
            {
                Debug.LogError(
                    $"[P102 Font] Pretendard font asset was created, but these required characters are missing: " +
                    $"'{missingCharacters}'.", fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();

            TMP_FontAsset saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (saved == null || saved.sourceFontFile != sourceFont ||
                saved.atlasPopulationMode != AtlasPopulationMode.Dynamic ||
                !saved.isMultiAtlasTexturesEnabled)
            {
                Debug.LogError(
                    $"[P102 Font] Saved asset validation failed for '{FontAssetPath}'.", saved);
                return;
            }

            Debug.Log(
                $"[P102 Font] Created '{FontAssetPath}' from '{SourceFontPath}' with Dynamic / Multi Atlas enabled.",
                saved);
        }

        static string BuildPreloadCharacters()
        {
            var unique = new HashSet<char>();
            var builder = new StringBuilder();
            const string source =
                "누출 위치를 바라보십시오 확인 중 흡입측 밸브 폐쇄 토출측 0123456789%P-102SUCDis";
            for (int i = 0; i < source.Length; ++i)
            {
                char character = source[i];
                if (!char.IsControl(character) && unique.Add(character))
                    builder.Append(character);
            }
            return builder.ToString();
        }

        static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; ++i)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
