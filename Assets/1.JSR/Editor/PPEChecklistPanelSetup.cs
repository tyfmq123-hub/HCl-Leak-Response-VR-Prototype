using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Creates / repairs a single world-space PPE_ChecklistCanvas near PPE_Storage.
/// </summary>
public static class PPEChecklistPanelSetup
{
    const string CanvasName = "PPE_ChecklistCanvas";
    const string HangulFontPath = "Assets/1.JSR/Resources/PPE_HangulFont.asset";
    const string LiberationSansGuid = "8f586378b4e144a9851e7b34d9b748ee";

    static readonly Color PanelBg = new Color(0.07f, 0.10f, 0.14f, 0.88f);
    static readonly Color TitleYellow = new Color(0.95f, 0.78f, 0.22f, 1f);
    static readonly Color SectionYellow = new Color(0.88f, 0.72f, 0.18f, 1f);
    static readonly Color BodyWhite = new Color(0.94f, 0.95f, 0.97f, 1f);
    static readonly Color StatusGray = new Color(0.72f, 0.76f, 0.80f, 1f);
    static readonly Color IncompleteGray = new Color(0.45f, 0.48f, 0.52f, 1f);

    struct RowSpec
    {
        public string ObjectName;
        public string Label;
        public string ItemId;
        public PPEChecklistPanel.ChecklistSource Source;
    }

    // Matches PPESequenceManager phase lists / TTS prompts.
    static readonly RowSpec[] DonningRows =
    {
        new RowSpec { ObjectName = "InnerGlovesRow", Label = "속장갑", ItemId = PPESequenceManager.IdInnerGloves, Source = PPEChecklistPanel.ChecklistSource.Equipped },
        new RowSpec { ObjectName = "ChemicalSuitRow", Label = "화학보호복", ItemId = PPEEquipmentManager.IdChemicalSuit, Source = PPEChecklistPanel.ChecklistSource.Equipped },
        new RowSpec { ObjectName = "ChemicalBootsRow", Label = "화학보호 장화", ItemId = PPEEquipmentManager.IdChemicalBoots, Source = PPEChecklistPanel.ChecklistSource.Equipped },
        new RowSpec { ObjectName = "FaceRespiratorRow", Label = "전면형 호흡보호구", ItemId = PPEEquipmentManager.IdFaceRespirator, Source = PPEChecklistPanel.ChecklistSource.Equipped },
        new RowSpec { ObjectName = "FaceShieldRow", Label = "안면보호구", ItemId = PPEEquipmentManager.IdFaceShield, Source = PPEChecklistPanel.ChecklistSource.Equipped },
        new RowSpec { ObjectName = "ChemicalGlovesRow", Label = "내화학 보호장갑", ItemId = PPEEquipmentManager.IdChemicalGloves, Source = PPEChecklistPanel.ChecklistSource.Equipped },
    };

    static readonly RowSpec[] EquipmentPreparationRows =
    {
        new RowSpec { ObjectName = "ResponseBagRow", Label = "대응가방 확인", ItemId = PPEEquipmentManager.IdResponseBag, Source = PPEChecklistPanel.ChecklistSource.ResponseBagPrepared },
        new RowSpec { ObjectName = "WasteContainerRow", Label = "유해폐기물 수거용기 준비", ItemId = PPEEquipmentManager.IdWasteContainer, Source = PPEChecklistPanel.ChecklistSource.Prepared },
    };

    public static void SetupChecklistPanel()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Checklist] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== PPE Checklist Panel Setup ===");

        var existing = GameObject.Find(CanvasName);
        if (existing == null)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var t = root.transform.Find(CanvasName);
                if (t != null)
                {
                    existing = t.gameObject;
                    break;
                }

                var panels = root.GetComponentsInChildren<PPEChecklistPanel>(true);
                if (panels != null && panels.Length > 0)
                {
                    existing = panels[0].gameObject;
                    if (existing.name != CanvasName)
                        existing.name = CanvasName;
                    break;
                }
            }
        }

        GameObject canvasGo;
        if (existing != null)
        {
            canvasGo = existing;
            report.AppendLine("Reused existing PPE_ChecklistCanvas (no duplicate).");
        }
        else
        {
            canvasGo = CreateCanvasHierarchy(report);
        }

        WirePanel(canvasGo, report);
        PlaceNearStorage(canvasGo, report);

        EditorUtility.SetDirty(canvasGo);
        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Selection.activeObject = canvasGo;
        Debug.Log(report.ToString());
    }

    static GameObject CreateCanvasHierarchy(StringBuilder report)
    {
        var system = GameObject.Find("PPE_System");
        var canvasGo = new GameObject(CanvasName);
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create PPE_ChecklistCanvas");
        if (system != null)
            Undo.SetTransformParent(canvasGo.transform, system.transform, "Parent checklist canvas");

        var canvas = Undo.AddComponent<Canvas>(canvasGo);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 40;

        var scaler = Undo.AddComponent<CanvasScaler>(canvasGo);
        scaler.dynamicPixelsPerUnit = 10f;
        scaler.referencePixelsPerUnit = 100f;

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(780f, 980f);

        var panel = CreateRect("ChecklistPanel", canvasGo.transform);
        StretchFull(panel);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = PanelBg;
        panelImage.raycastTarget = false;

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 32, 28);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        CreateText(panel, "TitleText", "PPE \uCC29\uC6A9 \uC0C1\uD0DC", 48f, TitleYellow, FontStyles.Bold, 70f);

        var ppeSection = CreateRect("PPESection", panel);
        AddVertical(ppeSection.gameObject, 6, new RectOffset(0, 0, 4, 4));
        PreferHeight(ppeSection, 520f);
        CreateText(ppeSection, "PPESectionLabel", "PPE 착용", 30f, SectionYellow, FontStyles.Bold, 40f);
        foreach (var spec in DonningRows)
            CreateRow(ppeSection, spec);

        var equipmentSection = CreateRect("EquipmentSection", panel);
        AddVertical(equipmentSection.gameObject, 6, new RectOffset(0, 0, 4, 4));
        PreferHeight(equipmentSection, 220f);
        CreateText(equipmentSection, "EquipmentSectionLabel", "대응 장비 준비", 30f, SectionYellow, FontStyles.Bold, 40f);
        foreach (var spec in EquipmentPreparationRows)
            CreateRow(equipmentSection, spec);

        CreateText(panel, "StatusText", "PPE 착용 0/6  |  대응 장비 준비 0/2", 28f, StatusGray, FontStyles.Normal, 48f);

        Undo.AddComponent<PPEChecklistPanel>(canvasGo);
        report.AppendLine("Created hierarchy under PPE_System (or scene root).");
        return canvasGo;
    }

    static void CreateRow(Transform parent, RowSpec spec)
    {
        var row = CreateRect(spec.ObjectName, parent);
        PreferHeight(row, 72f);

        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 18f;
        h.padding = new RectOffset(4, 4, 6, 6);
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlHeight = true;
        h.childControlWidth = true;
        h.childForceExpandHeight = true;
        h.childForceExpandWidth = false;

        var indicator = CreateRect("Indicator", row);
        PreferWidth(indicator, 56f);
        PreferHeight(indicator, 56f);
        var img = indicator.gameObject.AddComponent<Image>();
        img.color = IncompleteGray;
        img.raycastTarget = false;

        var mark = CreateText(indicator, "Mark", "\u25A1", 36f, BodyWhite, FontStyles.Bold, 56f);
        var markRect = mark.rectTransform;
        StretchFull(markRect);
        mark.alignment = TextAlignmentOptions.Center;

        var label = CreateText(row, "Label", spec.Label, 34f, BodyWhite, FontStyles.Normal, 56f);
        var le = label.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minHeight = 56f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
    }

    static void WirePanel(GameObject canvasGo, StringBuilder report)
    {
        var panelComp = canvasGo.GetComponent<PPEChecklistPanel>();
        if (panelComp == null)
            panelComp = Undo.AddComponent<PPEChecklistPanel>(canvasGo);

        var manager = Object.FindAnyObjectByType<PPEEquipmentManager>(FindObjectsInactive.Include);
        var so = new SerializedObject(panelComp);
        so.FindProperty("equipmentManager").objectReferenceValue = manager;

        var rowList = new System.Collections.Generic.List<(RowSpec Spec, Transform RowRoot)>();
        CollectRows(canvasGo.transform, DonningRows, rowList);
        CollectRows(canvasGo.transform, EquipmentPreparationRows, rowList);

        // Ensure missing rows exist under the matching phase section.
        var section = FindDeep(canvasGo.transform, "PPESection");
        if (section != null)
        {
            for (var i = 0; i < DonningRows.Length; i++)
            {
                var spec = DonningRows[i];
                if (FindDeep(canvasGo.transform, spec.ObjectName) != null)
                    continue;

                CreateRow(section, spec);
            }
        }

        var equipmentSection = FindDeep(canvasGo.transform, "EquipmentSection");
        if (equipmentSection != null)
        {
            equipmentSection.gameObject.SetActive(true);
            for (var i = 0; i < EquipmentPreparationRows.Length; i++)
            {
                var spec = EquipmentPreparationRows[i];
                if (FindDeep(canvasGo.transform, spec.ObjectName) != null)
                    continue;

                CreateRow(equipmentSection, spec);
            }
        }

        rowList.Clear();
        CollectRows(canvasGo.transform, DonningRows, rowList);
        CollectRows(canvasGo.transform, EquipmentPreparationRows, rowList);

        var rowsProp = so.FindProperty("rows");
        rowsProp.arraySize = rowList.Count;
        for (var i = 0; i < rowList.Count; i++)
        {
            var (spec, rowRoot) = rowList[i];
            var elem = rowsProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("itemId").stringValue = spec.ItemId;
            elem.FindPropertyRelative("source").enumValueIndex = (int)spec.Source;

            var label = rowRoot.Find("Label")?.GetComponent<TextMeshProUGUI>();
            var indicator = rowRoot.Find("Indicator")?.GetComponent<Image>();
            var mark = rowRoot.Find("Indicator/Mark")?.GetComponent<TextMeshProUGUI>();
            elem.FindPropertyRelative("labelText").objectReferenceValue = label;
            elem.FindPropertyRelative("indicator").objectReferenceValue = indicator;
            elem.FindPropertyRelative("indicatorMark").objectReferenceValue = mark;
        }

        var status = FindDeep(canvasGo.transform, "StatusText")?.GetComponent<TextMeshProUGUI>();
        so.FindProperty("statusText").objectReferenceValue = status;
        so.ApplyModifiedPropertiesWithoutUndo();

        ApplyFonts(canvasGo);
        report.AppendLine($"Wired {rowList.Count} rows. Manager={(manager != null ? manager.name : "NULL")}.");
    }

    static void CollectRows(Transform root, RowSpec[] specs, System.Collections.Generic.List<(RowSpec, Transform)> list)
    {
        for (var i = 0; i < specs.Length; i++)
        {
            var t = FindDeep(root, specs[i].ObjectName);
            if (t != null)
                list.Add((specs[i], t));
        }
    }

    static void PlaceNearStorage(GameObject canvasGo, StringBuilder report)
    {
        var pos = new Vector3(-1.55f, 1.55f, 1.78f);
        var rot = Quaternion.Euler(0f, 180f, 0f);
        var scale = Vector3.one * 0.00135f;

        var storage = GameObject.Find("PPE_Storage");
        if (storage != null)
        {
            var front = storage.transform.position + storage.transform.forward * 0.65f;
            front.y = 1.55f;
            front += -storage.transform.right * 0.9f;
            pos = front;
            rot = Quaternion.LookRotation(-storage.transform.forward, Vector3.up);
        }

        Undo.RecordObject(canvasGo.transform, "Place checklist canvas");
        canvasGo.transform.SetPositionAndRotation(pos, rot);
        canvasGo.transform.localScale = scale;
        report.AppendLine($"Placed at {pos} near PPE_Storage (not parented to camera).");
    }

    static void ApplyFonts(GameObject root)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HangulFontPath);
        if (font == null || font.material == null)
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                AssetDatabase.GUIDToAssetPath(LiberationSansGuid));
        }

        if (font == null)
            return;

        var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (var i = 0; i < tmps.Length; i++)
        {
            tmps[i].font = font;
            if (font.material != null)
                tmps[i].fontSharedMaterial = font.material;
            EditorUtility.SetDirty(tmps[i]);
        }
    }

    static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static TextMeshProUGUI CreateText(
        Transform parent,
        string name,
        string text,
        float fontSize,
        Color color,
        FontStyles style,
        float preferredHeight)
    {
        var rect = CreateRect(name, parent);
        PreferHeight(rect, preferredHeight);
        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    static void AddVertical(GameObject go, float spacing, RectOffset padding)
    {
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = padding;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
    }

    static void PreferHeight(RectTransform rect, float height)
    {
        var le = rect.gameObject.GetComponent<LayoutElement>();
        if (le == null)
            le = rect.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
    }

    static void PreferWidth(RectTransform rect, float width)
    {
        var le = rect.gameObject.GetComponent<LayoutElement>();
        if (le == null)
            le = rect.gameObject.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
