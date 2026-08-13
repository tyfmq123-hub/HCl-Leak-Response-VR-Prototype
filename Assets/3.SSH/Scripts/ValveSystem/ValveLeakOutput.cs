using System.Reflection;
using UnityEngine;

namespace VRProject.Valves
{
    [DisallowMultipleComponent]
    public sealed class ValveLeakOutput : MonoBehaviour
    {
        [SerializeField] ParticleSystem leakParticle;
        [Tooltip("Optional existing controller. SetLeakIntensity01(float), StartLeak(), and StopLeak() are used when present.")]
        [SerializeField] MonoBehaviour existingLeakController;
        [SerializeField, Min(0f)] float minimumEmissionRate;
        [SerializeField, Min(0f)] float maximumEmissionRate = 100f;
        [SerializeField, Min(0f)] float minimumStartSpeed = 0.1f;
        [SerializeField, Min(0f)] float maximumStartSpeed = 2f;
        [SerializeField, Min(0f)] float startThreshold = 0.001f;
        [SerializeField] AudioSource leakAudio;
        [SerializeField, Range(0f, 1f)] float maximumAudioVolume = 1f;

        float intensity01;
        MethodInfo setIntensityMethod;
        MethodInfo startLeakMethod;
        MethodInfo stopLeakMethod;
        bool warnedAboutUnsupportedController;

        public float Intensity01 => intensity01;

        void Awake()
        {
            CacheExistingControllerMethods();
        }

        public void SetLeakIntensity01(float value)
        {
            intensity01 = Mathf.Clamp01(value);
            CacheExistingControllerMethods();

            if (setIntensityMethod != null)
                setIntensityMethod.Invoke(existingLeakController, new object[] { intensity01 });

            if (leakParticle != null)
            {
                ParticleSystem.EmissionModule emission = leakParticle.emission;
                emission.rateOverTimeMultiplier = Mathf.Lerp(minimumEmissionRate, maximumEmissionRate, intensity01);
                ParticleSystem.MainModule main = leakParticle.main;
                main.startSpeedMultiplier = Mathf.Lerp(minimumStartSpeed, maximumStartSpeed, intensity01);
            }

            bool shouldRun = intensity01 > startThreshold;
            if (shouldRun)
            {
                if (startLeakMethod != null) startLeakMethod.Invoke(existingLeakController, null);
                if (leakParticle != null && !leakParticle.isPlaying) leakParticle.Play(true);
                if (leakAudio != null)
                {
                    leakAudio.volume = intensity01 * maximumAudioVolume;
                    if (!leakAudio.isPlaying) leakAudio.Play();
                }
            }
            else
            {
                if (stopLeakMethod != null) stopLeakMethod.Invoke(existingLeakController, null);
                if (leakParticle != null && leakParticle.isPlaying)
                    leakParticle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (leakAudio != null && leakAudio.isPlaying) leakAudio.Stop();
            }

            if (existingLeakController != null && setIntensityMethod == null && leakParticle == null &&
                !warnedAboutUnsupportedController)
            {
                warnedAboutUnsupportedController = true;
                Debug.LogWarning($"[{nameof(ValveLeakOutput)}] '{existingLeakController.GetType().Name}' has no SetLeakIntensity01(float), and no ParticleSystem is assigned. Only StartLeak/StopLeak can be used.", this);
            }
        }

        void CacheExistingControllerMethods()
        {
            if (existingLeakController == null || setIntensityMethod != null || startLeakMethod != null || stopLeakMethod != null)
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            System.Type type = existingLeakController.GetType();
            setIntensityMethod = type.GetMethod("SetLeakIntensity01", flags, null, new[] { typeof(float) }, null);
            startLeakMethod = type.GetMethod("StartLeak", flags, null, System.Type.EmptyTypes, null);
            stopLeakMethod = type.GetMethod("StopLeak", flags, null, System.Type.EmptyTypes, null);
        }
    }
}
