using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Read-only diagnostic: never saves or rebuilds scenes. Run with -executeMethod.
public static class IntegrationVisualAudit
{
    public static void Run()
    {
        string folder = Path.GetFullPath("Logs/VisualAudit");
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        foreach (string stage in new[] { "04_PPE", "05_PreEntry", "06_LeakResponse" })
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + stage + ".unity");
            var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach (var t in transforms.Where(t => t.name.Contains("HazWasteContainer") || t.name == "FallingDrops" || t.name.Contains("WindsockGaze") || t.name.Contains("RecoveryTongs")))
            {
                report.AppendLine($"{stage}: {PathOf(t)} active={t.gameObject.activeInHierarchy} pos={t.position:F4} rot={t.rotation.eulerAngles:F2} scale={t.lossyScale:F4} localPos={t.localPosition:F4} localRot={t.localEulerAngles:F2}");
                foreach (var r in t.GetComponents<Renderer>())
                    report.AppendLine($"  renderer enabled={r.enabled} bounds={r.bounds} materials={string.Join(",", r.sharedMaterials.Select(m => m ? m.name + ":" + m.shader.name : "NULL"))}");
                foreach (var p in t.GetComponents<ParticleSystem>())
                    report.AppendLine($"  particles emission={p.emission.enabled} rate={p.emission.rateOverTimeMultiplier} startSize={p.main.startSizeMultiplier} lifetime={p.main.startLifetimeMultiplier} speed={p.main.startSpeedMultiplier} max={p.main.maxParticles}");
            }
            var body = transforms.FirstOrDefault(t => t.name == "HazWasteContainer_Body");
            if (body != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                var bounds = body.GetComponentsInChildren<Renderer>().First().bounds;
                foreach (var r in body.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                for (int side = 0; side < 4; side++)
                    Render(folder + "/" + stage + "_bin_" + side + ".png", bounds.center, Quaternion.Euler(0, side * 90, 0) * new Vector3(0, 0.55f, -2.1f));
            }
        }
        File.WriteAllText(Path.Combine(folder, "scene-audit.txt"), report.ToString());
        Debug.Log("[Integration Visual Audit] " + folder);
    }

    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

    public static void Render(string path, Vector3 target, Vector3 offset)
    {
        var go = new GameObject("AuditCamera");
        var camera = go.AddComponent<Camera>();
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.transform.SetPositionAndRotation(target + offset, Quaternion.LookRotation(-offset));
        camera.fieldOfView = 50;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 150;
        camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
        var rt = new RenderTexture(1200, 1200, 24) { antiAliasing = 4 };
        rt.Create();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(go);
    }
}
