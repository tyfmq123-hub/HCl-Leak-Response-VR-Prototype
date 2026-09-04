using System.Collections;
using System.Collections.Generic;
using HClTraining.App;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

/// <summary>
/// Shared world-space feedback that shows a short Korean equip confirmation.
/// One canvas for the whole scene — not per PPE item.
/// </summary>
[DisallowMultipleComponent]
public class PPEEquipFeedbackUI : MonoBehaviour
{
    const string MessageSuffix = " 착용했습니다.";
    const string ResourcesFontPath = "PPE/PPE_HangulFont";
    [Header("References")]
    [SerializeField] Canvas feedbackCanvas;
    [SerializeField] RectTransform feedbackPanel;
    [SerializeField] TextMeshProUGUI feedbackText;
    [SerializeField] CanvasGroup canvasGroup;

    [Header("Font")]
    [Tooltip("Optional Hangul-capable TMP font. Preferred over OS/dynamic fallbacks.")]
    [SerializeField] TMP_FontAsset preferredHangulFont;

    [Header("Timing")]
    [SerializeField] [Min(0.1f)] float displayDuration = 2f;
    [SerializeField] [Min(0f)] float fadeOutDuration = 0.35f;

    [Header("Display")]
    [SerializeField]
    [Tooltip("Temporarily hide the subtitle HUD. Keep subtitle copy and callers; turn this on when TTS/hints are wired together.")]
    bool showSubtitleHud;

    Camera xrCamera;
    Coroutine hideRoutine;
    TMP_FontAsset hangulFont;
    PPEEquipmentManager subscribedManager;
    bool wantsSubscription;
    bool loggedMissingHangulFont;

    void Awake()
    {
        ResolveReferences();
        EnsureHangulFont();
        CacheCamera();
        HideImmediate();
    }

    void OnEnable()
    {
        wantsSubscription = true;
        TrySubscribe();
    }

    void Start()
    {
        // Manager may Awake after this component enables.
        TrySubscribe();
    }

    void OnDisable()
    {
        wantsSubscription = false;
        Unsubscribe();

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }
    }

    void OnDestroy()
    {
        wantsSubscription = false;
        Unsubscribe();
    }

    void LateUpdate()
    {
        // Retry until manager exists (script execution order / scene load).
        if (wantsSubscription && subscribedManager == null)
            TrySubscribe();

        UpdatePlacement();
    }

    void TrySubscribe()
    {
        if (!wantsSubscription || !isActiveAndEnabled)
            return;

        var manager = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : FindAnyObjectByType<PPEEquipmentManager>();

        if (manager == null)
            return;

        if (subscribedManager == manager)
            return;

        Unsubscribe();
        manager.EquipmentSucceeded += OnEquipmentSucceeded;
        subscribedManager = manager;
    }

    void Unsubscribe()
    {
        if (subscribedManager == null)
            return;

        subscribedManager.EquipmentSucceeded -= OnEquipmentSucceeded;
        subscribedManager = null;
    }

    void OnEquipmentSucceeded(PPEEquipable item)
    {
        if (item == null)
            return;

        // TTS already shows timed Korean subtitles for equip steps — skip short duplicate toast.
        if (PPETTSManager.Instance != null)
            return;

        var name = string.IsNullOrWhiteSpace(item.DisplayName) ? item.ItemId : item.DisplayName;
        ShowMessage(BuildEquipMessage(name));
    }

    static string BuildEquipMessage(string displayName)
    {
        return AppendObjectParticle(displayName) + MessageSuffix;
    }

    /// <summary>Appends 을/를 based on Hangul batchim (final consonant).</summary>
    static string AppendObjectParticle(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "를";

        var last = name[name.Length - 1];
        if (last >= 0xAC00 && last <= 0xD7A3)
        {
            var jong = (last - 0xAC00) % 28;
            return name + (jong == 0 ? "를" : "을");
        }

        return name + "를";
    }

    /// <summary>Shows <paramref name="message"/> and restarts the hide timer.</summary>
    public void ShowMessage(string message)
    {
        ShowMessage(message, displayDuration);
    }

    /// <summary>Shows <paramref name="message"/> for <paramref name="durationSeconds"/> (e.g. TTS clip length).</summary>
    public void ShowMessage(string message, float durationSeconds)
    {
        if (!showSubtitleHud)
            return;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        if (!isActiveAndEnabled)
            return;

        ResolveReferences();
        EnsureHangulFont();
        CacheCamera();

        if (feedbackText != null)
        {
            // Force dynamic atlas to include these Hangul glyphs when possible.
            if (hangulFont != null && !string.IsNullOrEmpty(message))
                hangulFont.TryAddCharacters(message);

            ApplyFontToText(hangulFont);
            feedbackText.text = message ?? string.Empty;
            feedbackText.ForceMeshUpdate(true);
        }
        else
        {
            Debug.LogWarning("[PPE Feedback] FeedbackText is missing — cannot show equip message.", this);
        }

        Debug.Log(
            $"[PPE Feedback] {message} (font={(hangulFont != null ? hangulFont.name : "null")}, mat={(hangulFont != null && hangulFont.material != null)})",
            this);

        if (feedbackPanel != null)
            feedbackPanel.gameObject.SetActive(true);

        if (feedbackCanvas != null)
            feedbackCanvas.enabled = true;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (hideRoutine != null)
            StopCoroutine(hideRoutine);

        var hold = Mathf.Max(0.35f, durationSeconds);
        hideRoutine = StartCoroutine(HideAfterDelay(hold));
    }

    IEnumerator HideAfterDelay()
    {
        yield return HideAfterDelay(displayDuration);
    }

    IEnumerator HideAfterDelay(float holdSeconds)
    {
        yield return new WaitForSeconds(holdSeconds);

        if (canvasGroup != null && fadeOutDuration > 0f)
        {
            var start = canvasGroup.alpha;
            var elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, 0f, elapsed / fadeOutDuration);
                yield return null;
            }

            canvasGroup.alpha = 0f;
        }

        HideImmediate();
        hideRoutine = null;
    }

    public void HideImmediate()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        if (feedbackPanel != null)
            feedbackPanel.gameObject.SetActive(false);
    }

    void UpdatePlacement()
    {
        CacheCamera();
        if (xrCamera == null || feedbackCanvas == null)
            return;

        XRSubtitleHudLayout.Apply(feedbackCanvas, transform as RectTransform, xrCamera);
    }

    void CacheCamera()
    {
        // During integrated scene loading the authored PPE camera can be active in Awake,
        // then gets disabled after references are rebound to the shared App XR camera.
        // Never keep pushing that inactive camera back into a Screen Space Camera canvas.
        xrCamera = XRSubtitleHudLayout.ResolveActiveCamera(xrCamera);
    }

    void ResolveReferences()
    {
        if (feedbackCanvas == null)
            feedbackCanvas = GetComponent<Canvas>();

        if (feedbackPanel == null)
        {
            var panel = transform.Find("FeedbackPanel");
            if (panel != null)
                feedbackPanel = panel as RectTransform;
        }

        if (feedbackText == null && feedbackPanel != null)
        {
            var text = feedbackPanel.Find("FeedbackText");
            if (text != null)
                feedbackText = text.GetComponent<TextMeshProUGUI>();
        }

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (feedbackCanvas != null)
            XRSubtitleHudLayout.Apply(feedbackCanvas, transform as RectTransform, xrCamera);

        ConfigureSubtitleLayout();
    }

    void ConfigureSubtitleLayout()
    {
        if (feedbackPanel != null)
            XRSubtitleHudLayout.ConfigurePanel(feedbackPanel);

        if (feedbackText == null)
            return;

        XRSubtitleHudLayout.ConfigureText(feedbackText);
    }

    void EnsureHangulFont()
    {
        if (feedbackText == null)
            return;

        if (hangulFont != null && hangulFont.material != null)
        {
            ApplyFontToText(hangulFont);
            return;
        }

        if (preferredHangulFont != null && preferredHangulFont.material != null)
        {
            hangulFont = preferredHangulFont;
            ApplyFontToText(hangulFont);
            return;
        }

        var fromResources = Resources.Load<TMP_FontAsset>(ResourcesFontPath);
        if (fromResources != null && fromResources.material != null)
        {
            hangulFont = fromResources;
            ApplyFontToText(hangulFont);
            return;
        }

        // Rebuild at runtime if saved TMP asset has a broken/null material.
        hangulFont = CreateDynamicHangulFontAsset();
        if (hangulFont != null && hangulFont.material != null)
        {
            ApplyFontToText(hangulFont);
            return;
        }

        if (!loggedMissingHangulFont)
        {
            loggedMissingHangulFont = true;
            Debug.LogWarning(
                "[PPE Feedback] Hangul font unavailable or missing material. " +
                "Run Tools > PPE > Create Hangul TMP Font Asset.",
                this);
        }
    }

    void ApplyFontToText(TMP_FontAsset font)
    {
        if (feedbackText == null || font == null)
            return;

        feedbackText.font = font;
        if (font.material != null)
            feedbackText.fontSharedMaterial = font.material;
    }

    static TMP_FontAsset CreateDynamicHangulFontAsset()
    {
        // Prefer project Pretendard placed under Resources for builds/Quest.
        var pretendard = Resources.Load<Font>("PPE/Pretendard-SemiBold");
        Font source = pretendard != null ? pretendard : FindHangulOsFont();
        if (source == null)
            return null;

        var asset = TMP_FontAsset.CreateFontAsset(
            source,
            90,
            9,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic);

        if (asset == null)
            return null;

        asset.name = "PPE_Hangul_Dynamic";
        return asset;
    }

    static Font FindHangulOsFont()
    {
        var candidates = new List<string>
        {
            // Windows editor / PC VR
            "Malgun Gothic",
            "맑은 고딕",
            "Malgun Gothic Semilight",
            // macOS
            "Apple SD Gothic Neo",
            "AppleGothic",
            // Android / Quest — Noto CJK is commonly present
            "Noto Sans CJK KR",
            "Noto Sans CJK JP",
            "Noto Sans CJK SC",
            "NotoSansCJK-Regular",
            "Noto Sans KR",
            "Source Han Sans K",
            "DroidSansFallback",
            "NanumGothic",
            "Nanum Gothic",
        };

        var font = Font.CreateDynamicFontFromOSFont(candidates.ToArray(), 48);
        if (font != null && !string.IsNullOrEmpty(font.name))
            return font;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Direct system font files on many Android/Quest images.
        string[] paths =
        {
            "/system/fonts/NotoSansCJK-Regular.ttc",
            "/system/fonts/NotoSansCJKkr-Regular.otf",
            "/system/fonts/NotoSansKR-Regular.otf",
            "/system/fonts/DroidSansFallback.ttf",
        };

        for (int i = 0; i < paths.Length; i++)
        {
            if (!System.IO.File.Exists(paths[i]))
                continue;

            try
            {
                var pathFont = new Font(paths[i]);
                if (pathFont != null)
                    return pathFont;
            }
            catch
            {
                // Try next path.
            }
        }
#endif

        return null;
    }
}
