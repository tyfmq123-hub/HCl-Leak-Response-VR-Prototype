using System.IO;
using UnityEditor;

[InitializeOnLoad]
public static class VrHandPrefabRestoreTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunVrHandPrefabRestore.trigger.txt";

    static VrHandPrefabRestoreTrigger()
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

        VrHandPrefabRestore.FillHandPrefabsFromFbx();
    }
}
