using System;
using HClTraining.SharedXR;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Plays Goal 2 TTS wav clips matched to scenario steps and completion outro lines.</summary>
    public sealed class EntrySafetyGoal02TtsPlayer : MonoBehaviour
    {
        [Serializable]
        public sealed class StepClip
        {
            public EntrySafetyStep Step;
            public AudioClip Clip;
        }

        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private StepClip[] _clips;
        [SerializeField] private AudioClip _stopAndPressureConfirmedClip;
        [SerializeField] private AudioClip _safetyCheckCompleteClip;
        [SerializeField] private AudioClip _checkAccessRestrictionClip;

        private EntrySafetyStep? _playingStep;
        private float _playingUntilUnscaledTime;

        public bool IsPlaying =>
            (_audioSource != null && _audioSource.isPlaying) ||
            SharedTrainingPause.UnscaledTime < _playingUntilUnscaledTime;

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
            return PlayClip(FindClip(step), step);
        }

        public float PlayStopAndPressureConfirmed()
        {
            var clip = EntrySafetyNarrationResources.LoadSpecial("18_VerifyPressureDecrease") ??
                       _stopAndPressureConfirmedClip;
            return PlayClip(clip, null);
        }

        public float PlaySafetyCheckComplete()
        {
            var clip = EntrySafetyNarrationResources.LoadSpecial("19_SafetyCheckComplete") ??
                       _safetyCheckCompleteClip;
            return PlayClip(clip, null);
        }

        public float PlayCheckAccessRestriction()
        {
            var clip = EntrySafetyNarrationResources.LoadSpecial("20_MoveToLeakResponse") ??
                       _checkAccessRestrictionClip;
            return PlayClip(clip, null);
        }

        public void Stop()
        {
            if (_audioSource != null)
                _audioSource.Stop();
            _playingStep = null;
            _playingUntilUnscaledTime = 0f;
        }

        private float PlayClip(AudioClip clip, EntrySafetyStep? step)
        {
            if (_audioSource == null)
                return 0f;

            _audioSource.Stop();
            _playingStep = null;
            _playingUntilUnscaledTime = 0f;
            if (clip == null)
                return 0f;

            _audioSource.clip = clip;
            _audioSource.Play();
            _playingStep = step;
            _playingUntilUnscaledTime = SharedTrainingPause.UnscaledTime + clip.length;
            return clip.length;
        }

        private AudioClip FindClip(EntrySafetyStep step)
        {
            // MoveToNextArea uses the explicit outro clip sequence only. The Scene still
            // contains a legacy per-step A-button clip; never let a generic refresh replay it
            // after the handoff has already started.
            if (step == EntrySafetyStep.MoveToNextArea)
                return null;

            var finalCourseClip = EntrySafetyNarrationResources.LoadStep(step);
            if (finalCourseClip != null)
                return finalCourseClip;

            // The authored Scene predates the explicit automatic pressure-wait step.
            // Reuse this serialized slot for the reduced-pressure gaze cue so the
            // Scene AudioClip GUIDs and Stage completion contract remain unchanged.
            if (step == EntrySafetyStep.VerifyPressureDecrease)
                return _stopAndPressureConfirmedClip;

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
}
