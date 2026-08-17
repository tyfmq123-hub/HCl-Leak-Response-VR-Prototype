using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace JH.EntrySafety
{
    /// <summary>
    /// Title / Start 메뉴 UI. App 연동 시 UnityEvent로 모드 선택·종료를 연결한다.
    /// Stage에서 SceneManager를 직접 호출하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TitleMenuController : MonoBehaviour
    {
        public enum Mode
        {
            Education = 0,
            Training = 1
        }

        [Serializable]
        public sealed class ModeUnityEvent : UnityEvent<Mode> { }

        [Header("Selection Visuals")]
        [SerializeField] Image educationFrame;
        [SerializeField] Image trainingFrame;
        [SerializeField] Image educationIcon;
        [SerializeField] Image trainingIcon;
        [SerializeField] Color selectedIconColor = new Color(1f, 0.45f, 0.1f, 1f);
        [SerializeField] Color idleIconColor = Color.white;

        [Header("Events (App / Integration wire here)")]
        [SerializeField] ModeUnityEvent onModeConfirmed;
        [SerializeField] UnityEvent onEducationSelected;
        [SerializeField] UnityEvent onTrainingSelected;
        [SerializeField] UnityEvent onExitClicked;

        Mode _selected = Mode.Education;

        public Mode SelectedMode => _selected;
        public ModeUnityEvent OnModeConfirmed => onModeConfirmed;
        public UnityEvent OnEducationSelected => onEducationSelected;
        public UnityEvent OnTrainingSelected => onTrainingSelected;
        public UnityEvent OnExitClicked => onExitClicked;

        void Start()
        {
            ApplySelectionVisuals();
        }

        public void SelectEducation()
        {
            _selected = Mode.Education;
            ApplySelectionVisuals();
            onEducationSelected?.Invoke();
            onModeConfirmed?.Invoke(Mode.Education);
        }

        public void SelectTraining()
        {
            _selected = Mode.Training;
            ApplySelectionVisuals();
            onTrainingSelected?.Invoke();
            onModeConfirmed?.Invoke(Mode.Training);
        }

        public void ClickExit()
        {
            onExitClicked?.Invoke();
        }

        void ApplySelectionVisuals()
        {
            // Mode button frames are hover-driven (ColorTint). Selection only drives icons if present.
            var educationSelected = _selected == Mode.Education;
            if (educationIcon != null)
                educationIcon.color = educationSelected ? selectedIconColor : idleIconColor;
            if (trainingIcon != null)
                trainingIcon.color = educationSelected ? idleIconColor : selectedIconColor;
        }

        public void BindVisuals(Image eduFrame, Image trainFrame, Image eduIcon, Image trainIcon)
        {
            educationFrame = eduFrame;
            trainingFrame = trainFrame;
            educationIcon = eduIcon;
            trainingIcon = trainIcon;
            ApplySelectionVisuals();
        }
    }
}
