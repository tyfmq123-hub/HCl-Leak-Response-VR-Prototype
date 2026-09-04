using System.Collections.Generic;
using UnityEngine;
using VRProject.Valves;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>
    /// Owns the LeakResponse Scene's spatial industrial ambience. It is deliberately
    /// independent from task progression so the additively loaded LeakResponse
    /// environment remains audible while PreEntry is the active Stage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeakResponseSpatialLeakAudio : MonoBehaviour
    {
        const string ResourceFolder = "LeakAudio/";

        const string MistClipName = "LR_Flange_Mist_Loop";
        const string DropletsClipName = "LR_Flange_Droplets_Loop";
        const string PumpClipName = "LR_P102_Pump_Loop";
        const string FenceClipName = "LR_Fence_PumpArea_Ambience_Loop";
        const string Alarm01ClipName = "LR_Distant_Alarm_01_Loop";
        const string Alarm02ClipName = "LR_Distant_Alarm_02_Loop";

        const float MistMaximumVolume = 0.06f;
        const float DropletsMaximumVolume = 0.135f;
        const float PumpVolume = 0.055f;
        const float FenceAmbienceVolume = 0.04f;
        const float Alarm01Volume = 0.045f;
        const float Alarm02Volume = 0.035f;
        const float DormantLeakIntensity = 0.7f;
        const float NarrationDuckMultiplier = 0.35f;

        readonly List<AudioSource> allSources = new List<AudioSource>(6);

        ValveWheelClosureController leakStateProvider;
        AudioSource mistSource;
        AudioSource dropletsSource;
        AudioSource pumpSource;
        AudioSource fenceSource;
        AudioSource alarm01Source;
        AudioSource alarm02Source;
        bool configured;
        bool leakStageActive;
        bool narrationDucked;

        public AudioSource LeakSource => mistSource;
        public AudioSource MistSource => mistSource;
        public AudioSource DropletsSource => dropletsSource;
        public AudioSource PumpSource => pumpSource;
        public AudioSource FenceSource => fenceSource;
        public AudioSource Alarm01Source => alarm01Source;
        public AudioSource Alarm02Source => alarm02Source;

        public float CurrentLeakIntensity => leakStageActive && leakStateProvider != null
            ? Mathf.Clamp01(leakStateProvider.LeakIntensity01)
            : DormantLeakIntensity;

        public void Configure(
            Transform mistAnchor,
            Transform dropletsAnchor,
            Transform pumpAnchor,
            Transform fenceAmbienceAnchor,
            Transform alarm01Anchor,
            Transform alarm02Anchor,
            ValveWheelClosureController newLeakStateProvider)
        {
            leakStateProvider = newLeakStateProvider;
            allSources.Clear();

            mistSource = ConfigureLoop(
                mistAnchor, "Audio_Flange_Mist_3D", MistClipName,
                MistMaximumVolume, 1f, 50f, 0.22f, 20f);
            dropletsSource = ConfigureLoop(
                dropletsAnchor != null ? dropletsAnchor : mistAnchor,
                "Audio_Flange_Droplets_3D", DropletsClipName,
                DropletsMaximumVolume, 1f, 50f, 0.20f, 20f);
            pumpSource = ConfigureLoop(
                pumpAnchor, "Audio_P102_Pump_3D", PumpClipName,
                PumpVolume, 1.5f, 20f, 0.04f, 55f);
            fenceSource = ConfigureLoop(
                fenceAmbienceAnchor, "Audio_Fence_PumpArea_Ambience_3D", FenceClipName,
                FenceAmbienceVolume, 2f, 50f, 0.12f, 180f);
            alarm01Source = ConfigureLoop(
                alarm01Anchor, "Audio_Distant_Alarm_01_3D", Alarm01ClipName,
                Alarm01Volume, 6f, 70f, 0.24f, 125f);
            alarm02Source = ConfigureLoop(
                alarm02Anchor, "Audio_Distant_Alarm_02_3D", Alarm02ClipName,
                Alarm02Volume, 6f, 70f, 0.24f, 125f);

            configured = mistSource != null || dropletsSource != null ||
                         pumpSource != null || fenceSource != null ||
                         alarm01Source != null || alarm02Source != null;
            enabled = configured;
            ApplyVolumes(true);
            PlayConfiguredSources();
        }

        // Retained for older callers and focused tests.
        public void Configure(Transform leakPoint,
            ValveWheelClosureController newLeakStateProvider)
        {
            Configure(
                leakPoint, leakPoint, null, null, null, null,
                newLeakStateProvider);
        }

        public void SetLeakStageActive(bool active)
        {
            leakStageActive = active;
            ApplyVolumes(true);
            PlayConfiguredSources();
        }

        /// <summary>
        /// Future short TTS can call this without rebuilding or stopping the ambience.
        /// </summary>
        public void SetNarrationDucked(bool ducked)
        {
            narrationDucked = ducked;
            ApplyVolumes(false);
        }

        void OnEnable()
        {
            if (configured)
            {
                ApplyVolumes(true);
                PlayConfiguredSources();
            }
        }

        void Update()
        {
            if (!configured)
                return;

            ApplyVolumes(false);
            PlayConfiguredSources();
        }

        void OnDisable()
        {
            for (int i = 0; i < allSources.Count; ++i)
            {
                if (allSources[i] != null)
                    allSources[i].Stop();
            }
        }

        void ApplyVolumes(bool immediate)
        {
            float master = narrationDucked ? NarrationDuckMultiplier : 1f;
            SetVolume(mistSource, ResolveVolume(CurrentLeakIntensity) * master, immediate);
            SetVolume(dropletsSource,
                ResolveDropletsVolume(CurrentLeakIntensity) * master, immediate);
            SetVolume(pumpSource, PumpVolume * master, immediate);
            SetVolume(fenceSource, FenceAmbienceVolume * master, immediate);
            SetVolume(alarm01Source, Alarm01Volume * master, immediate);
            SetVolume(alarm02Source, Alarm02Volume * master, immediate);
        }

        static void SetVolume(AudioSource source, float target, bool immediate)
        {
            if (source == null)
                return;

            target = Mathf.Clamp01(target);
            source.volume = immediate
                ? target
                : Mathf.MoveTowards(source.volume, target, Time.deltaTime * 0.5f);
        }

        void PlayConfiguredSources()
        {
            for (int i = 0; i < allSources.Count; ++i)
            {
                AudioSource source = allSources[i];
                if (source != null && source.enabled && source.clip != null &&
                    source.volume > 0.0001f && !source.isPlaying)
                {
                    source.Play();
                }
            }
        }

        AudioSource ConfigureLoop(
            Transform anchor,
            string sourceName,
            string clipName,
            float baseVolume,
            float minDistance,
            float maxDistance,
            float farVolumeFloor,
            float spread)
        {
            if (anchor == null)
            {
                Debug.LogWarning(
                    $"[LeakResponse Audio] Anchor for '{sourceName}' is missing.", this);
                return null;
            }

            AudioClip clip = Resources.Load<AudioClip>(ResourceFolder + clipName);
            if (clip == null)
            {
                Debug.LogError(
                    $"[LeakResponse Audio] Missing clip Resources/{ResourceFolder}{clipName}.", this);
                return null;
            }

            Transform sourceTransform = anchor.Find(sourceName);
            if (sourceTransform == null)
            {
                var sourceObject = new GameObject(sourceName);
                sourceTransform = sourceObject.transform;
                sourceTransform.SetParent(anchor, false);
            }

            AudioSource source =
                P102RuntimeUtility.GetOrAdd<AudioSource>(sourceTransform.gameObject);
            bool clipChanged = source.clip != clip;
            if (clipChanged && source.isPlaying)
                source.Stop();

            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.spread = spread;
            source.priority = 170;
            source.reverbZoneMix = 0.25f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.minDistance = Mathf.Max(0.01f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 0.01f, maxDistance);
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                CreateAudibleDistanceCurve(farVolumeFloor));
            source.volume = Mathf.Clamp01(baseVolume);
            source.enabled = true;

            if (!allSources.Contains(source))
                allSources.Add(source);
            return source;
        }

        public static float ResolveVolume(float leakIntensity) =>
            MistMaximumVolume * Mathf.Sqrt(Mathf.Clamp01(leakIntensity));

        public static float ResolveDropletsVolume(float leakIntensity) =>
            DropletsMaximumVolume * Mathf.Sqrt(Mathf.Clamp01(leakIntensity));

        public static AnimationCurve CreateAudibleDistanceCurve(float farVolumeFloor)
        {
            float floor = Mathf.Clamp(farVolumeFloor, 0.01f, 1f);
            return new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.2f, 0.72f),
                new Keyframe(0.55f, Mathf.Lerp(0.72f, floor, 0.58f)),
                new Keyframe(1f, floor));
        }
    }
}
