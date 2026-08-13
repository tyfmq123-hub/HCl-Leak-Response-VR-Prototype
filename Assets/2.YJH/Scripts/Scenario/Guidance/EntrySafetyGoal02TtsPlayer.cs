using System;
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
            Time.unscaledTime < _playingUntilUnscaledTime;

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
            return PlayClip(_stopAndPressureConfirmedClip, null);
        }

        public float PlaySafetyCheckComplete()
        {
            return PlayClip(_safetyCheckCompleteClip, null);
        }

        public float PlayCheckAccessRestriction()
        {
            return PlayClip(_checkAccessRestrictionClip, null);
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
            _playingUntilUnscaledTime = Time.unscaledTime + clip.length;
            return clip.length;
        }

        private AudioClip FindClip(EntrySafetyStep step)
        {
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
