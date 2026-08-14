using System;
using System.Collections;
using System.Collections.Generic;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class TrainingPauseController : MonoBehaviour
    {
        readonly Dictionary<LocomotionProvider, bool> locomotionStates =
            new Dictionary<LocomotionProvider, bool>();

        StageLifecycleAdapter lifecycle;
        SharedXRInputContract sharedInput;
        Transform commonXrRoot;
        float previousTimeScale = 1f;
        bool previousAudioPause;

        public static bool IsTrainingPaused => SharedTrainingPause.IsPaused;
        public static event Action<bool> PauseChanged
        {
            add => SharedTrainingPause.Changed += value;
            remove => SharedTrainingPause.Changed -= value;
        }
        public bool IsPaused => SharedTrainingPause.IsPaused;

        public void Initialize(
            StageLifecycleAdapter lifecycleAdapter,
            SharedXRInputContract input,
            Transform xrRoot)
        {
            lifecycle = lifecycleAdapter;
            sharedInput = input;
            commonXrRoot = xrRoot;
        }

        public void PauseTraining()
        {
            if (IsTrainingPaused)
                return;

            SharedTrainingPause.SetPaused(true);
            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            sharedInput?.SetGameplayInputSuppressed(true);
            lifecycle?.SetMenuPaused(true);

            locomotionStates.Clear();
            if (commonXrRoot != null)
            {
                var providers = commonXrRoot.GetComponentsInChildren<LocomotionProvider>(true);
                for (var i = 0; i < providers.Length; i++)
                {
                    var provider = providers[i];
                    if (provider == null)
                        continue;
                    locomotionStates[provider] = provider.enabled;
                    provider.enabled = false;
                }
            }
        }

        public void ResumeTraining()
        {
            if (!IsTrainingPaused)
                return;

            foreach (var pair in locomotionStates)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            locomotionStates.Clear();

            lifecycle?.SetMenuPaused(false);
            sharedInput?.SetGameplayInputSuppressed(false);
            AudioListener.pause = previousAudioPause;
            Time.timeScale = previousTimeScale;
            SharedTrainingPause.SetPaused(false);
        }

        void OnDestroy()
        {
            ResumeTraining();
        }

    }

    [DisallowMultipleComponent]
    public sealed class TransitionInputLock : MonoBehaviour
    {
        readonly Dictionary<Behaviour, bool> previousStates = new Dictionary<Behaviour, bool>();
        Transform commonXrRoot;

        public bool IsLocked { get; private set; }
        public event Action<bool> LockChanged;

        public void Initialize(Transform xrRoot) => commonXrRoot = xrRoot;

        public bool TryLock()
        {
            if (IsLocked)
                return false;

            IsLocked = true;
            previousStates.Clear();
            if (commonXrRoot != null)
            {
                var behaviours = commonXrRoot.GetComponentsInChildren<Behaviour>(true);
                for (var i = 0; i < behaviours.Length; i++)
                {
                    var behaviour = behaviours[i];
                    if (behaviour == null || !IsInputBehaviour(behaviour.GetType()))
                        continue;
                    previousStates[behaviour] = behaviour.enabled;
                    behaviour.enabled = false;
                }
            }

            SetButtonsInteractable(false);
            LockChanged?.Invoke(true);
            return true;
        }

        public void Unlock(bool restorePreviousStates = true)
        {
            if (!IsLocked)
                return;

            if (restorePreviousStates)
            {
                foreach (var pair in previousStates)
                {
                    if (pair.Key != null)
                        pair.Key.enabled = pair.Value;
                }
            }
            previousStates.Clear();
            IsLocked = false;
            SetButtonsInteractable(true);
            LockChanged?.Invoke(false);
        }

        static bool IsInputBehaviour(Type type)
        {
            var name = type.Name;
            return name.IndexOf("Interactor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("InputActionManager", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("InputModule", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void SetButtonsInteractable(bool interactable)
        {
            var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null)
                    buttons[i].interactable = interactable;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class ScreenFadeController : MonoBehaviour
    {
        CanvasGroup group;
        Image image;
        Coroutine fadeRoutine;

        public float Alpha => group != null ? group.alpha : 0f;

        public void Initialize(Camera camera)
        {
            if (group != null)
                return;

            var root = new GameObject("TransitionFadeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root.transform.SetParent(camera != null ? camera.transform : transform, false);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(4f, 4f);
            rect.localPosition = new Vector3(0f, 0f, 0.11f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.1f;

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = short.MaxValue;
            group = root.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = false;

            var panel = new GameObject("Black", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            image = panel.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = true;
        }

        public IEnumerator FadeOut(float duration = 0.25f) => FadeTo(1f, duration);
        public IEnumerator FadeIn(float duration = 0.3f) => FadeTo(0f, duration);

        IEnumerator FadeTo(float target, float duration)
        {
            if (group == null)
                yield break;

            var start = group.alpha;
            var elapsed = 0f;
            group.blocksRaycasts = true;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(start, target, duration <= 0f ? 1f : elapsed / duration);
                yield return null;
            }
            group.alpha = target;
            group.blocksRaycasts = target > 0.001f;
        }

        public void SetImmediate(float alpha)
        {
            if (group == null)
                return;
            group.alpha = Mathf.Clamp01(alpha);
            group.blocksRaycasts = group.alpha > 0.001f;
        }
    }
}
