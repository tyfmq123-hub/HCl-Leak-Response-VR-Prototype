using System.IO;
using UnityEditor;

[InitializeOnLoad]
public static class GloveGrabPoseSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunGloveGrabPoseSetup.trigger.txt";

    static GloveGrabPoseSetupTrigger()
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

        GloveGrabPoseSetup.FixChemicalGlovesGrabPose();
    }
}
