using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Creates the shared PPE_EquipFeedbackCanvas under PPE_System.
/// </summary>
public static class PPEEquipFeedbackSetup
{
    const string CanvasName = "PPE_EquipFeedbackCanvas";
    const string LiberationSansGuid = "8f586378b4e144a9851e7b34d9b748ee";

    public static void SetupEquipFeedbackUI()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Feedback] No active loaded scene.");
            return;
        }

        var existing = GameObject.Find(CanvasName);
        if (existing != null)
        {
            EnsureFeedbackComponent(existing);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[PPE Feedback] Reused existing PPE_EquipFeedbackCanvas.");
            return;
        }

        var system = GameObject.Find("PPE_System");
        var canvasGo = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create PPE_EquipFeedbackCanvas");
        if (system != null)
            Undo.SetTransformParent(canvasGo.transform, system.transform, "Parent feedback canvas");

        canvasGo.transform.localPosition = new Vector3(0f, 1.2f, 1.2f);
        canvasGo.transform.localRotation = Quaternion.identity;
        canvasGo.transform.localScale = Vector3.one * 0.0012f;

        var canvas = Undo.AddComponent<Canvas>(canvasGo);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        var scaler = Undo.AddComponent<CanvasScaler>(canvasGo);
        scaler.dynamicPixelsPerUnit = 10f;
        scaler.referencePixelsPerUnit = 100f;

        // Non-blocking toast — no GraphicRaycaster.
        Undo.AddComponent<CanvasGroup>(canvasGo);

        var rect = canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(900f, 180f);

        var panelGo = new GameObject("FeedbackPanel", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(panelGo, "Create FeedbackPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var image = panelGo.AddComponent<Image>();
        image.color = new Color(0.05f, 0.07f, 0.1f, 0.72f);
        image.raycastTarget = false;

        var textGo = new GameObject("FeedbackText", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(textGo, "Create FeedbackText");
        textGo.transform.SetParent(panelGo.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 16f);
        textRect.offsetMax = new Vector2(-24f, -16f);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.fontSize = 42f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath(LiberationSansGuid));
        if (font != null)
            tmp.font = font;

        EnsureFeedbackComponent(canvasGo);
        panelGo.SetActive(false);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PPE Feedback] Created PPE_EquipFeedbackCanvas under PPE_System.");
    }

    static void EnsureFeedbackComponent(GameObject canvasGo)
    {
        var ui = canvasGo.GetComponent<PPEEquipFeedbackUI>();
        if (ui == null)
            ui = Undo.AddComponent<PPEEquipFeedbackUI>(canvasGo);

        EditorUtility.SetDirty(ui);
        EditorUtility.SetDirty(canvasGo);
    }
}
