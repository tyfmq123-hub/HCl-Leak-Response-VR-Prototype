using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PPEHangulFontSetupTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunPPEHangulFontSetup.trigger.txt";
    const string AssetPath = "Assets/Resources/PPE/PPE_HangulFont.asset";
    const string PretendardPath = "Assets/Fonts/PPE/Pretendard-SemiBold.otf";

    static PPEHangulFontSetupTrigger()
    {
        EditorApplication.delayCall += TryRun;
    }

    static void TryRun()
    {
        if (!File.Exists(TriggerPath))
            return;

        try
        {
            File.Delete(TriggerPath);
            if (File.Exists(TriggerPath + ".meta"))
                File.Delete(TriggerPath + ".meta");
        }
        catch
        {
            // ignored
        }

        if (!File.Exists(PretendardPath))
        {
            Debug.LogWarning($"[PPE Hangul Font] Missing source font: {PretendardPath}");
            return;
        }

        // Always rebuild from Pretendard when trigger is present.
        PPEHangulFontSetup.CreateHangulFontAsset(overwriteWithoutPrompt: true);
        Debug.Log($"[PPE Hangul Font] Trigger finished. Asset: {AssetPath}");
    }
}
