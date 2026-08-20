using System;
using UnityEngine;
using VRProject.Valves;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>
    /// Provides a scene-local, fully spatial leak hiss without introducing an external asset.
    /// The discharge valve's existing leak intensity controls the sound and VFX together.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeakResponseSpatialLeakAudio : MonoBehaviour
    {
        const string SourceName = "LeakResponse_3D_LeakSound_Runtime";
        const int SampleRate = 22050;
        const float MaximumVolume = 0.42f;

        ValveWheelClosureController leakStateProvider;
        AudioSource leakSource;
        AudioClip ownedClip;

        public AudioSource LeakSource => leakSource;
        public float CurrentLeakIntensity => leakStateProvider != null
            ? leakStateProvider.LeakIntensity01
            : 0f;

        public void Configure(Transform leakPoint,
            ValveWheelClosureController newLeakStateProvider)
        {
            leakStateProvider = newLeakStateProvider;
            if (leakPoint == null)
            {
                Debug.LogError("[LeakResponse Audio] LeakPoint is missing; 3D leak audio is disabled.", this);
                enabled = false;
                return;
            }

            Transform sourceTransform = leakPoint.Find(SourceName);
            if (sourceTransform == null)
            {
                var sourceObject = new GameObject(SourceName);
                sourceTransform = sourceObject.transform;
                sourceTransform.SetParent(leakPoint, false);
            }

            leakSource = P102RuntimeUtility.GetOrAdd<AudioSource>(sourceTransform.gameObject);
            if (ownedClip == null)
                ownedClip = CreateLeakHissClip();
            leakSource.clip = ownedClip;
            leakSource.loop = true;
            leakSource.playOnAwake = false;
            leakSource.spatialBlend = 1f;
            leakSource.dopplerLevel = 0f;
            leakSource.spread = 25f;
            leakSource.rolloffMode = AudioRolloffMode.Logarithmic;
            leakSource.minDistance = 0.8f;
            leakSource.maxDistance = 14f;
            leakSource.volume = ResolveVolume(CurrentLeakIntensity);
            enabled = true;

            if (leakSource.volume > 0.001f && !leakSource.isPlaying)
                leakSource.Play();
        }

        void Update()
        {
            if (leakSource == null)
                return;

            float targetVolume = ResolveVolume(CurrentLeakIntensity);
            leakSource.volume = Mathf.MoveTowards(
                leakSource.volume, targetVolume, Time.deltaTime * 0.65f);
            if (leakSource.volume > 0.001f)
            {
                if (!leakSource.isPlaying)
                    leakSource.Play();
            }
            else if (leakSource.isPlaying)
            {
                leakSource.Stop();
            }
        }

        void OnDisable()
        {
            if (leakSource != null)
                leakSource.Stop();
        }

        void OnDestroy()
        {
            if (ownedClip != null)
            {
                if (Application.isPlaying)
                    Destroy(ownedClip);
                else
                    DestroyImmediate(ownedClip);
            }
        }

        public static float ResolveVolume(float leakIntensity) =>
            MaximumVolume * Mathf.Sqrt(Mathf.Clamp01(leakIntensity));

        static AudioClip CreateLeakHissClip()
        {
            var clip = AudioClip.Create(
                "LeakResponse Procedural HCl Hiss", SampleRate, 1, SampleRate, false);
            var samples = new float[SampleRate];
            var random = new System.Random(102104);
            float previousInput = 0f;
            float previousOutput = 0f;
            for (int i = 0; i < samples.Length; ++i)
            {
                float input = (float)(random.NextDouble() * 2.0 - 1.0);
                float highPass = 0.86f * (previousOutput + input - previousInput);
                previousInput = input;
                previousOutput = highPass;
                float flutter = 0.86f + 0.14f * Mathf.Sin(2f * Mathf.PI * i / 1500f);
                samples[i] = Mathf.Clamp(highPass * flutter * 0.32f, -1f, 1f);
            }
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }
    }
}
