using System;
using HClTraining.SharedXR;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Plays Goal 1 TTS wav clips matched to scenario steps.</summary>
    public sealed class EntrySafetyGoal01TtsPlayer : MonoBehaviour
    {
        [Serializable]
        public sealed class StepClip
        {
            public EntrySafetyStep Step;
            public AudioClip Clip;
        }

        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private StepClip[] _clips;

        private EntrySafetyStep? _playingStep;
        private int _playRequestedFrame = -2;

        public bool IsPlaying =>
            _audioSource != null && _audioSource.clip != null &&
            (_audioSource.isPlaying || Time.frameCount <= _playRequestedFrame + 1 ||
             SharedTrainingPause.IsPaused || AudioListener.pause);

        public float GetClipLength(EntrySafetyStep step)
        {
            var clip = FindClip(step);
            return clip != null ? clip.length : 0f;
        }

        public bool IsPlayingStep(EntrySafetyStep step)
        {
            return IsPlaying && _playingStep.HasValue && _playingStep.Value == step;
        }

        public float Play(EntrySafetyStep step)
        {
            if (_audioSource == null)
                return 0f;

            var clip = FindClip(step);
            _audioSource.Stop();
            _playingStep = null;
            _playRequestedFrame = -2;

            if (clip == null)
                return 0f;

            _audioSource.clip = clip;
            _audioSource.ignoreListenerPause = false;
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            _audioSource.Play();
            _playingStep = step;
            _playRequestedFrame = Time.frameCount;
            return clip.length;
        }

        public void Stop()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.clip = null;
            }
            _playingStep = null;
            _playRequestedFrame = -2;
        }

        private AudioClip FindClip(EntrySafetyStep step)
        {
            var finalCourseClip = EntrySafetyNarrationResources.LoadStep(step);
            if (finalCourseClip != null)
                return finalCourseClip;

            if (_clips == null)
                return null;

            for (var i = 0; i < _clips.Length; i++)
            {
                var entry = _clips[i];
                if (entry != null && entry.Step == step)
                    return entry.Clip;
            }

            return null;
        }
    }

    /// <summary>
    /// Central resource map for the final-course PreEntry narration.
    /// Scene-assigned clips remain as a fallback so older standalone scenes still work.
    /// </summary>
    internal static class EntrySafetyNarrationResources
    {
        private const string Root = "TrainingNarration/PreEntry/";

        public static AudioClip LoadStep(EntrySafetyStep step)
        {
            var fileName = FileNameForStep(step);
            return string.IsNullOrEmpty(fileName)
                ? null
                : Resources.Load<AudioClip>(Root + fileName);
        }

        public static AudioClip LoadSpecial(string fileName)
        {
            return string.IsNullOrEmpty(fileName)
                ? null
                : Resources.Load<AudioClip>(Root + fileName);
        }

        private static string FileNameForStep(EntrySafetyStep step)
        {
            switch (step)
            {
                case EntrySafetyStep.SceneIntro: return "01_SceneIntro";
                case EntrySafetyStep.EquipDetector: return "02_EquipDetector";
                case EntrySafetyStep.MeasureInitialHcl: return "03_MeasureInitialHcl";
                case EntrySafetyStep.ConfirmInitialHcl: return "04_ConfirmInitialHcl";
                case EntrySafetyStep.CheckInitialWind: return "05_CheckInitialWind";
                case EntrySafetyStep.WaitWindChange: return "06_WaitWindChange";
                case EntrySafetyStep.CheckChangedWind: return "07_CheckChangedWind";
                case EntrySafetyStep.MeasureReducedHcl: return "08_MeasureReducedHcl";
                case EntrySafetyStep.ConfirmReducedHcl: return "09_ConfirmReducedHcl";
                case EntrySafetyStep.StoreDetector: return "10_StoreDetector";
                case EntrySafetyStep.MoveToPT: return "11_MoveToPanel";
                case EntrySafetyStep.CheckPressure: return "12_CheckPressure";
                case EntrySafetyStep.CheckTemperature: return "13_CheckTemperature";
                case EntrySafetyStep.CheckRunState: return "14_CheckRunState";
                case EntrySafetyStep.StopPump: return "15_StopPump";
                case EntrySafetyStep.VerifyStopIndicator: return "16_VerifyStopIndicator";
                case EntrySafetyStep.WaitPressureDecrease: return "17_WaitPressureDecrease";
                case EntrySafetyStep.VerifyPressureDecrease: return "18_VerifyPressureDecrease";
                default: return null;
            }
        }
    }
}
