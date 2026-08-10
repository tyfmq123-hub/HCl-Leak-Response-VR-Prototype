using System.IO;
using UnityEditor;

[InitializeOnLoad]
public static class PPETTSSetupTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunPPETTSSetup.trigger.txt";

    static PPETTSSetupTrigger()
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

        // Delay once so Unity can import newly added wav assets.
        EditorApplication.delayCall += () => PPETTSSetup.SetupPpeTts();
    }
}
