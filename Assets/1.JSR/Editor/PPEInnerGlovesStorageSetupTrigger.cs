using System.IO;
using UnityEditor;

[InitializeOnLoad]
public static class PPEInnerGlovesStorageSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunPPEInnerGlovesStorageSetup.trigger.txt";

    static PPEInnerGlovesStorageSetupTrigger()
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

        PPEInnerGlovesStorageSetup.InstallInnerGlovesIntoRightBay();
    }
}
