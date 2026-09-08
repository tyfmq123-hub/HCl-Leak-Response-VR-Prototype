using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HClTraining.PPE
{
    /// <summary>
    /// Controls the process-diagram highlight layers on the PPE room whiteboard.
    /// Public no-argument methods are intended for UnityEvent, Timeline, and TTS cues.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PPEProcessDiagramController : MonoBehaviour
    {
        static readonly string[] PreparationItemIds =
        {
            PPEEquipmentManager.IdInnerGloves,
            PPEEquipmentManager.IdChemicalSuit,
            PPEEquipmentManager.IdChemicalBoots,
            PPEEquipmentManager.IdFaceRespirator,
            PPEEquipmentManager.IdFaceShield,
            PPEEquipmentManager.IdChemicalGloves,
            PPEEquipmentManager.IdResponseBag,
            PPEEquipmentManager.IdWasteContainer,
        };

        static readonly string[] PreparationLabels =
        {
            "속장갑",
            "화학보호복",
            "안전장화",
            "호흡보호구",
            "안면보호구",
            "외부 보호장갑",
            "대응가방 확인",
            "수거용기",
        };

        const string LeakSituationText =
            "주의. HCl 이송 배관의 플랜지 접합부에서 염산 누출이 발생했습니다.\n" +
            "미스트와 액적이 주변으로 확산 중입니다. 보호구 없이 절대 진입하지 마십시오.";

        const string PreparationInstructionText =
            "현장 진입 전 속장갑, 화학보호복, 안전장화, 호흡보호구를 순서대로 착용하십시오.\n" +
            "오른쪽 장비 보관대를 보며 안면보호구와 외부 보호장갑을 착용한 뒤 대응가방과 수거용기를 준비하십시오.";

        [Header("Full-size overlay CanvasGroups")]
        [SerializeField] CanvasGroup p102Path;
        [SerializeField] CanvasGroup suctionValve;
        [SerializeField] CanvasGroup pump;
        [SerializeField] CanvasGroup dischargeValve;
        [SerializeField] CanvasGroup leakFlange;
        [SerializeField] CanvasGroup flowArrows;

        [Header("Description")]
        [SerializeField] TMP_Text descriptionText;

        [Header("Highlight")]
        [SerializeField, Range(0f, 1f)] float supportingPathAlpha = 0.72f;

        [Header("HCL scene preview")]
        [SerializeField] bool playPreviewOnStart = true;
        [SerializeField, Min(0.1f)] float overviewSeconds = 2f;
        [SerializeField, Min(0.1f)] float highlightSeconds = 3f;
        [SerializeField, Min(0.1f)] float leakLocationSeconds = 7f;
        [SerializeField, Min(0.1f)] float preparationSeconds = 9f;

        [Header("Preparation order board")]
        [SerializeField] Vector3 orderBoardPosition = new Vector3(0f, 1.62f, 2.64f);
        [SerializeField] Vector2 orderBoardSize = new Vector2(1920f, 1080f);
        [SerializeField, Min(0.0001f)] float orderBoardScale = 0.0016f;
        [SerializeField, Min(0.05f)] float orderModelMaxSize = 0.33f;

        Coroutine previewRoutine;
        Canvas diagramCanvas;
        GameObject descriptionPanelRoot;
        GameObject preparationBoardRoot;
        readonly List<Mesh> runtimeBakedMeshes = new List<Mesh>();
        readonly List<TMP_Text> completionMarks = new List<TMP_Text>();
        readonly List<bool> completionMarkStates = new List<bool>();
        PPESequenceManager sequence;
        PPETTSManager narration;

        void Awake()
        {
            diagramCanvas = GetComponent<Canvas>();
            if (descriptionText != null && descriptionText.transform.parent != null)
                descriptionPanelRoot = descriptionText.transform.parent.gameObject;

            BuildPreparationOrderBoard();
            ShowOverview();
        }

        void Start()
        {
            sequence = PPESequenceManager.Resolve();
            RefreshCompletionMarks();
            narration = PPETTSManager.Instance;
            if (narration != null && narration.PlaysIntroOnStart)
            {
                narration.CueStarted += OnNarrationCueStarted;
                return;
            }
            if (playPreviewOnStart)
                PlayPreviewSequence();
        }

        void OnNarrationCueStarted(PPETTSCue cue)
        {
            if (cue == PPETTSCue.EmergencyIntro || cue == PPETTSCue.WearEssentialPPE)
                HighlightLeakFlange();
            else if (cue == PPETTSCue.WearInOrder || cue == PPETTSCue.WearInnerGloves)
                ShowPreparationOrderBoard();
        }

        void LateUpdate()
        {
            RefreshCompletionMarks();
        }

        public void ShowOverview()
        {
            ShowDiagramVisuals();
            ResetHighlights();
            ShowLeakSituation();
        }

        public void ShowLeakSituation()
        {
            SetDescription(LeakSituationText);
        }

        public void ShowPreparationInstructions()
        {
            SetDescription(PreparationInstructionText);
        }

        public void ShowPreparationOrderBoard()
        {
            ShowPreparationInstructions();
            ResetHighlights();

            if (diagramCanvas != null)
                diagramCanvas.enabled = false;
            if (descriptionPanelRoot != null)
                descriptionPanelRoot.SetActive(false);
            if (preparationBoardRoot != null)
                preparationBoardRoot.SetActive(true);
        }

        public void HighlightP102Path()
        {
            SetHighlight(p102Path, false);
        }

        public void HighlightSuctionValve()
        {
            SetHighlight(suctionValve, true);
        }

        public void HighlightPump()
        {
            SetHighlight(pump, true);
        }

        public void HighlightDischargeValve()
        {
            SetHighlight(dischargeValve, true);
        }

        public void HighlightLeakFlange()
        {
            SetHighlight(leakFlange, true);
        }

        public void HighlightFlowDirection()
        {
            SetHighlight(flowArrows, true);
        }

        public void ClearHighlights()
        {
            ResetHighlights();
            ShowLeakSituation();
        }

        public void ShowAllHighlights()
        {
            ShowDiagramVisuals();
            ResetHighlights();
            SetAlpha(p102Path, 1f);
            SetAlpha(suctionValve, 1f);
            SetAlpha(pump, 1f);
            SetAlpha(dischargeValve, 1f);
            SetAlpha(leakFlange, 1f);
            SetAlpha(flowArrows, 1f);
            ShowPreparationInstructions();
        }

        void ResetHighlights()
        {
            SetAlpha(p102Path, 0f);
            SetAlpha(suctionValve, 0f);
            SetAlpha(pump, 0f);
            SetAlpha(dischargeValve, 0f);
            SetAlpha(leakFlange, 0f);
            SetAlpha(flowArrows, 0f);
        }

        public void SetPulseEnabled(bool enabled)
        {
            // Compatibility entry point for previously assigned UnityEvents.
            // Highlights are intentionally static to avoid VR discomfort.
        }

        public void PlayPreviewSequence()
        {
            StopPreviewSequence();
            previewRoutine = StartCoroutine(PreviewSequence());
        }

        public void StopPreviewSequence()
        {
            if (previewRoutine != null)
            {
                StopCoroutine(previewRoutine);
                previewRoutine = null;
            }

            ShowOverview();
        }

        IEnumerator PreviewSequence()
        {
            ShowOverview();
            yield return new WaitForSecondsRealtime(Mathf.Max(4f, overviewSeconds));

            HighlightFlowDirection();
            yield return new WaitForSecondsRealtime(highlightSeconds);

            HighlightP102Path();
            yield return new WaitForSecondsRealtime(highlightSeconds);

            HighlightLeakFlange();
            yield return new WaitForSecondsRealtime(Mathf.Max(7f, leakLocationSeconds));

            ShowPreparationOrderBoard();
            yield return new WaitForSecondsRealtime(Mathf.Max(12f, preparationSeconds));

            previewRoutine = null;
        }

        void SetHighlight(CanvasGroup target, bool keepP102PathVisible)
        {
            ShowDiagramVisuals();
            ResetHighlights();
            if (keepP102PathVisible && p102Path != null && target != p102Path)
                SetAlpha(p102Path, supportingPathAlpha);

            SetAlpha(target, 1f);
        }

        void ShowDiagramVisuals()
        {
            if (diagramCanvas != null)
                diagramCanvas.enabled = true;
            if (descriptionPanelRoot != null)
                descriptionPanelRoot.SetActive(true);
            if (preparationBoardRoot != null)
                preparationBoardRoot.SetActive(false);
        }

        void BuildPreparationOrderBoard()
        {
            if (preparationBoardRoot != null)
                return;

            preparationBoardRoot = new GameObject(
                "PPE_PreparationOrderBoard",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            preparationBoardRoot.layer = 5;

            var boardRect = preparationBoardRoot.GetComponent<RectTransform>();
            var boardMount = transform.parent;
            if (boardMount != null)
            {
                boardRect.SetParent(boardMount, false);
                boardRect.localPosition = orderBoardPosition;
                boardRect.localRotation = Quaternion.identity;
            }
            else
            {
                boardRect.position = orderBoardPosition;
                boardRect.rotation = Quaternion.identity;
            }

            boardRect.localScale = Vector3.one * orderBoardScale;
            boardRect.sizeDelta = orderBoardSize;

            var boardCanvas = preparationBoardRoot.GetComponent<Canvas>();
            boardCanvas.renderMode = RenderMode.WorldSpace;
            boardCanvas.overrideSorting = true;
            boardCanvas.sortingOrder = 22;

            var boardScaler = preparationBoardRoot.GetComponent<CanvasScaler>();
            boardScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            boardScaler.referencePixelsPerUnit = 100f;
            boardScaler.dynamicPixelsPerUnit = 10f;

            CreateBoardText(
                boardRect,
                "PreparationOrder_Title",
                "현장 진입 전 준비 순서",
                new Vector2(0f, 470f),
                new Vector2(1780f, 90f),
                58f,
                TextAlignmentOptions.Center);

            var equipables = FindObjectsByType<PPEEquipable>(FindObjectsInactive.Include);
            for (var i = 0; i < PreparationLabels.Length; i++)
            {
                var column = i % 4;
                var row = i / 4;
                var cellX = -720f + column * 480f;
                var cellY = row == 0 ? 220f : -240f;

                CreateBoardText(
                    boardRect,
                    $"PreparationOrder_{i + 1:00}_Label",
                    $"<color=#1565C0><b>{i + 1:00}</b></color>\n{PreparationLabels[i]}",
                    new Vector2(cellX, cellY - 130f),
                    new Vector2(380f, 150f),
                    44f,
                    TextAlignmentOptions.Center);

                var completionMark = CreateBoardText(
                    boardRect,
                    $"PreparationOrder_{i + 1:00}_Complete",
                    string.Empty,
                    new Vector2(cellX + 135f, cellY + 128f),
                    new Vector2(110f, 110f),
                    72f,
                    TextAlignmentOptions.Center);
                completionMark.color = new Color(0.02f, 0.62f, 0.34f, 1f);
                completionMark.fontWeight = FontWeight.Bold;
                completionMark.enableAutoSizing = false;
                completionMarks.Add(completionMark);
                completionMarkStates.Add(false);

                var source = ResolvePreparationSource(PreparationItemIds[i], equipables);
                var modelPosition = boardRect.TransformPoint(new Vector3(cellX, cellY + 80f, -110f));
                CreateDisplayModel(
                    source,
                    PreparationItemIds[i],
                    PreparationLabels[i],
                    modelPosition,
                    boardRect.rotation);
            }

            preparationBoardRoot.SetActive(false);
        }

        void RefreshCompletionMarks()
        {
            if (completionMarks.Count != PreparationItemIds.Length ||
                completionMarkStates.Count != PreparationItemIds.Length)
                return;

            if (sequence == null)
                sequence = PPESequenceManager.Resolve();

            for (var i = 0; i < PreparationItemIds.Length; i++)
            {
                var completed = sequence != null &&
                                PPESequenceManager.TryGetStep(PreparationItemIds[i], out var step) &&
                                sequence.IsStepCompleted(step);
                if (completionMarkStates[i] == completed)
                    continue;

                completionMarks[i].text = completed ? "✓" : string.Empty;
                completionMarkStates[i] = completed;
            }
        }

        TMP_Text CreateBoardText(
            RectTransform parent,
            string objectName,
            string text,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            var textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.layer = 5;

            var rect = textObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var label = textObject.GetComponent<TextMeshProUGUI>();
            if (descriptionText != null && descriptionText.font != null)
                label.font = descriptionText.font;
            label.text = text;
            label.color = new Color(0.03f, 0.03f, 0.03f, 1f);
            label.fontSize = fontSize;
            label.fontWeight = FontWeight.Medium;
            label.alignment = alignment;
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(30f, fontSize - 10f);
            label.fontSizeMax = fontSize;
            label.raycastTarget = false;
            return label;
        }

        static GameObject ResolvePreparationSource(string itemId, PPEEquipable[] equipables)
        {
            if (itemId == PPEEquipmentManager.IdWasteContainer)
            {
                var waste = FindAnyObjectByType<WasteContainerPrepareInteractable>(FindObjectsInactive.Include);
                return waste != null ? waste.gameObject : null;
            }

            PPEEquipable best = null;
            var bestRendererCount = -1;
            for (var i = 0; i < equipables.Length; i++)
            {
                var item = equipables[i];
                if (item == null || item.ItemId != itemId)
                    continue;

                var rendererCount = item.GetComponentsInChildren<Renderer>(true).Length;
                if (rendererCount <= bestRendererCount)
                    continue;

                best = item;
                bestRendererCount = rendererCount;
            }

            return best != null ? best.gameObject : null;
        }

        void CreateDisplayModel(
            GameObject source,
            string itemId,
            string label,
            Vector3 targetPosition,
            Quaternion boardRotation)
        {
            if (source == null)
            {
                Debug.LogWarning($"[PPE Whiteboard] Display source missing: {label}", this);
                return;
            }

            var sourceRenderers = source.GetComponentsInChildren<Renderer>(true);
            var displayRoot = new GameObject($"PreparationOrder_{label}_Model");
            var geometryRoot = new GameObject("Visuals");
            geometryRoot.transform.SetParent(displayRoot.transform, false);

            var copiedRenderers = new List<Renderer>();
            for (var i = 0; i < sourceRenderers.Length; i++)
            {
                var sourceRenderer = sourceRenderers[i];
                if (!ShouldCopyPreparationRenderer(sourceRenderer, itemId))
                    continue;

                Mesh mesh = null;

                if (sourceRenderer is SkinnedMeshRenderer skinned)
                {
                    mesh = new Mesh { name = $"{sourceRenderer.name}_BoardBake" };
                    skinned.BakeMesh(mesh);
                    runtimeBakedMeshes.Add(mesh);
                }
                else if (sourceRenderer is MeshRenderer)
                {
                    var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                    if (sourceFilter != null)
                        mesh = sourceFilter.sharedMesh;
                }

                if (mesh == null)
                    continue;

                var visual = new GameObject(sourceRenderer.name);
                var filter = visual.AddComponent<MeshFilter>();
                var renderer = visual.AddComponent<MeshRenderer>();
                filter.sharedMesh = mesh;
                renderer.sharedMaterials = GetDisplayMaterials(sourceRenderer);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                // Copy the mesh relative to its equipment root. Using the renderer's
                // world pose also copied the cabinet placement rotation, which made
                // front-facing equipment appear sideways on Wall_Left.
                var pairMember = FindPairMemberRoot(sourceRenderer.transform, source.transform, itemId);
                var sourceSpace = pairMember != null ? pairMember : source.transform;
                var relativePose = sourceSpace.worldToLocalMatrix * sourceRenderer.transform.localToWorldMatrix;
                visual.transform.SetParent(geometryRoot.transform, false);
                visual.transform.localPosition = relativePose.GetPosition();
                visual.transform.localRotation = GetPairMemberCorrection(itemId) * relativePose.rotation;
                visual.transform.localScale = relativePose.lossyScale;

                if (pairMember != null)
                {
                    // The two physical glove objects have different shelf offsets.
                    // Re-center each mesh and place both on one exact vertical line
                    // instead of copying those staggered cabinet coordinates.
                    var memberBounds = renderer.bounds;
                    var memberCenter = geometryRoot.transform.InverseTransformPoint(memberBounds.center);
                    var halfSpacing = Mathf.Max(0.04f, memberBounds.size.x * 0.55f);
                    var isLeft = pairMember.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0;
                    var signedSpacing = isLeft ? -halfSpacing : halfSpacing;
                    var desiredCenter = new Vector3(signedSpacing, 0f, 0f);
                    visual.transform.localPosition += desiredCenter - memberCenter;
                }
                copiedRenderers.Add(renderer);
            }

            if (copiedRenderers.Count == 0)
            {
                Debug.LogWarning($"[PPE Whiteboard] No renderable mesh found: {label}", source);
                Destroy(displayRoot);
                return;
            }

            var bounds = copiedRenderers[0].bounds;
            for (var i = 1; i < copiedRenderers.Count; i++)
                bounds.Encapsulate(copiedRenderers[i].bounds);

            geometryRoot.transform.localPosition = -bounds.center;
            var maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            displayRoot.transform.localScale = Vector3.one * (orderModelMaxSize / Mathf.Max(0.001f, maxDimension));
            displayRoot.transform.position = targetPosition;
            // PPE display models were authored for a +Z-facing board. Wall_Left faces
            // -X, so rotate the copied composition with the board instead of preserving
            // its original world direction.
            displayRoot.transform.rotation = boardRotation * GetPreparationDisplayRotation(itemId);
            displayRoot.transform.SetParent(preparationBoardRoot.transform, true);
        }

        static Quaternion GetPreparationDisplayRotation(string itemId)
        {
            if (string.Equals(itemId, PPEEquipmentManager.IdInnerGloves, StringComparison.Ordinal) ||
                string.Equals(itemId, PPEEquipmentManager.IdChemicalGloves, StringComparison.Ordinal))
            {
                // Present both glove pairs with the exact same front-facing board
                // rotation and pair layout. Keeping a separate InnerGloves axis
                // path exposed its fingertips to the trainee instead of the hand.
                return Quaternion.Euler(0f, 180f, 90f);
            }

            if (string.Equals(itemId, PPEEquipmentManager.IdChemicalSuit, StringComparison.Ordinal) ||
                string.Equals(itemId, PPEEquipmentManager.IdFaceRespirator, StringComparison.Ordinal) ||
                string.Equals(itemId, PPEEquipmentManager.IdResponseBag, StringComparison.Ordinal) ||
                string.Equals(itemId, PPEEquipmentManager.IdFaceShield, StringComparison.Ordinal) ||
                string.Equals(itemId, PPEEquipmentManager.IdWasteContainer, StringComparison.Ordinal))
            {
                return Quaternion.Euler(0f, 180f, 0f);
            }

            // Boots, response bag, and waste container are authored front-on in
            // their own +Z equipment space. The board rotation maps that front to
            // Wall_Left's -X-facing surface.
            return Quaternion.identity;
        }

        static Quaternion GetPairMemberCorrection(string itemId)
        {
            if (string.Equals(itemId, PPEEquipmentManager.IdInnerGloves, StringComparison.Ordinal))
            {
                // InnerGlove OBJ is authored in the XZ palm plane (Y is its
                // thickness axis). Rotate that palm plane onto the board before
                // applying the same pair presentation used by item 07.
                return Quaternion.Euler(90f, 0f, 0f);
            }

            return Quaternion.identity;
        }

        static Transform FindPairMemberRoot(Transform rendererTransform, Transform sourceRoot, string itemId)
        {
            if (!IsPairItem(itemId))
                return null;

            for (var current = rendererTransform; current != null && current != sourceRoot; current = current.parent)
            {
                var objectName = current.name;
                var isPhysical = objectName.IndexOf("Physical", StringComparison.OrdinalIgnoreCase) >= 0;
                var hasSide = objectName.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              objectName.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isPhysical && hasSide)
                    return current;
            }

            return null;
        }

        static bool IsPairItem(string itemId)
        {
            return string.Equals(itemId, PPEEquipmentManager.IdInnerGloves, StringComparison.Ordinal) ||
                   string.Equals(itemId, PPEEquipmentManager.IdChemicalGloves, StringComparison.Ordinal) ||
                   string.Equals(itemId, PPEEquipmentManager.IdChemicalBoots, StringComparison.Ordinal);
        }

        static bool ShouldCopyPreparationRenderer(Renderer renderer, string itemId)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            var pairItem = IsPairItem(itemId);
            var physicalPairMember = false;
            for (var current = renderer.transform; current != null; current = current.parent)
            {
                var objectName = current.name;
                if (objectName.IndexOf("Physical", StringComparison.OrdinalIgnoreCase) >= 0)
                    physicalPairMember = true;
                if (objectName.IndexOf("Ghost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("Guide", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("Preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("Wearable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objectName.IndexOf("Equipped", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            // Pair roots also keep their original combined shelf model. Copying that
            // together with the left/right physical members produces two full sets.
            return !pairItem || physicalPairMember;
        }

        static Material[] GetDisplayMaterials(Renderer sourceRenderer)
        {
            var sourceMaterials = sourceRenderer.sharedMaterials;
            var materials = new List<Material>(sourceMaterials.Length);
            for (var i = 0; i < sourceMaterials.Length; i++)
            {
                var material = sourceMaterials[i];
                if (material == null)
                    continue;

                var materialName = material.name;
                if (materialName.Contains("Outline") || materialName.Contains("Highlight"))
                    continue;

                materials.Add(material);
            }

            return materials.ToArray();
        }

        void OnDestroy()
        {
            if (narration != null) narration.CueStarted -= OnNarrationCueStarted;
            for (var i = 0; i < runtimeBakedMeshes.Count; i++)
            {
                if (runtimeBakedMeshes[i] != null)
                    Destroy(runtimeBakedMeshes[i]);
            }
        }

        void SetDescription(string description)
        {
            if (descriptionText != null)
                descriptionText.text = description;
        }

        static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (group == null)
                return;

            group.alpha = alpha;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
    }
}
