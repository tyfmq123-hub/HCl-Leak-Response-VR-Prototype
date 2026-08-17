using UnityEngine;

namespace HClTraining.VRInput
{
    /// <summary>Formats shared VR input tokens as TextMeshPro-compatible rich text.</summary>
    public static class VRInputTextFormatter
    {
        public const string DefaultThemeResourcePath = "App/VRInputVisualTheme";

        static readonly VRInputType[] SupportedInputs =
        {
            VRInputType.A,
            VRInputType.B,
            VRInputType.X,
            VRInputType.Y,
            VRInputType.Trigger,
            VRInputType.Grip,
        };

        static VRInputVisualTheme cachedTheme;
        static bool warnedMissingTheme;

        public static VRInputVisualTheme Theme
        {
            get
            {
                if (cachedTheme != null)
                    return cachedTheme;

                cachedTheme = Resources.Load<VRInputVisualTheme>(DefaultThemeResourcePath);
                if (cachedTheme != null)
                    return cachedTheme;

                cachedTheme = ScriptableObject.CreateInstance<VRInputVisualTheme>();
                cachedTheme.hideFlags = HideFlags.HideAndDontSave;
                if (!warnedMissingTheme)
                {
                    warnedMissingTheme = true;
                    Debug.LogWarning(
                        $"[VRInputTextFormatter] Theme was not found at Resources/{DefaultThemeResourcePath}. " +
                        "Using runtime defaults.");
                }
                return cachedTheme;
            }
        }

        public static string Format(string source) => Format(source, null);

        public static string Format(string source, VRInputVisualTheme theme)
        {
            if (string.IsNullOrEmpty(source))
                return source ?? string.Empty;

            theme = theme != null ? theme : Theme;
            var result = source;
            for (var i = 0; i < SupportedInputs.Length; i++)
            {
                var input = SupportedInputs[i];
                result = result.Replace(GetToken(input), GetColoredDisplayName(input, theme));
            }
            return result;
        }

        /// <summary>
        /// Formats tokens first, then supports existing non-token controller-guide copy.
        /// New content should use <see cref="Format(string)"/> and explicit tokens.
        /// </summary>
        public static string FormatButtonName(string source, VRInputType input, VRInputVisualTheme theme = null)
        {
            if (string.IsNullOrEmpty(source))
                return source ?? string.Empty;

            theme = theme != null ? theme : Theme;
            var formatted = Format(source, theme);
            if (formatted != source)
                return formatted;

            var candidates = GetLegacyNames(input);
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (!source.Contains(candidate))
                    continue;
                var replacement = GetColoredDisplayName(input, theme);
                if (candidate.EndsWith(" 버튼"))
                    replacement += " 버튼";
                return source.Replace(candidate, replacement);
            }
            return source;
        }

        public static string GetToken(VRInputType input) => $"{{{input.ToString().ToUpperInvariant()}}}";

        public static string GetColoredDisplayName(VRInputType input, VRInputVisualTheme theme = null)
        {
            theme = theme != null ? theme : Theme;
            return Colorize(theme.GetDisplayName(input), theme.GetColor(input));
        }

        static string Colorize(string text, Color color) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

        static string[] GetLegacyNames(VRInputType input)
        {
            switch (input)
            {
                case VRInputType.A: return new[] { "A 버튼" };
                case VRInputType.B: return new[] { "B 버튼" };
                case VRInputType.X: return new[] { "X 버튼" };
                case VRInputType.Y: return new[] { "Y 버튼" };
                case VRInputType.Trigger: return new[] { "Trigger 버튼", "TRIGGER", "Trigger" };
                case VRInputType.Grip: return new[] { "Grip 버튼", "GRIP", "Grip" };
                default: return System.Array.Empty<string>();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            cachedTheme = null;
            warnedMissingTheme = false;
        }
    }
}
