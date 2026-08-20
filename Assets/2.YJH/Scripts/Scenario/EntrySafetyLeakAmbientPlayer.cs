using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// Plays a 3D leak ambience from the leak side of the no-access boundary so it
    /// is audible before the trainee enters the leak zone. Does not own App audio.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntrySafetyLeakAmbientPlayer : MonoBehaviour
    {
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _leakClip;
        [SerializeField] private Transform _leakOrigin;
        [SerializeField] private Vector3 _fallbackWorldPosition = new Vector3(0f, 1.15f, -6.4f);
        [SerializeField, Range(0f, 1f)] private float _volume = 0.42f;
        [SerializeField, Min(1f)] private float _minDistance = 4f;
        [SerializeField, Min(2f)] private float _maxDistance = 28f;

        private AudioClip _generatedClip;
        private GameObject _originObject;

        private void Awake()
        {
            EnsureOrigin();
            EnsureSource();
        }

        private void OnEnable()
        {
            Play();
        }

        private void OnDisable()
        {
            if (_audioSource != null)
                _audioSource.Stop();
        }

        private void OnDestroy()
        {
            if (_generatedClip != null)
            {
                if (Application.isPlaying)
                    Destroy(_generatedClip);
                else
                    DestroyImmediate(_generatedClip);
            }

            if (_originObject != null && Application.isPlaying)
                Destroy(_originObject);
        }

        public void Play()
        {
            EnsureOrigin();
            EnsureSource();
            if (_audioSource == null || _audioSource.isPlaying)
                return;

            _audioSource.clip = _leakClip != null ? _leakClip : _generatedClip;
            if (_audioSource.clip == null)
                return;

            _audioSource.Play();
        }

        private void EnsureOrigin()
        {
            if (_leakOrigin != null)
                return;

            _originObject = new GameObject("PreEntryLeakAmbientOrigin");
            _leakOrigin = _originObject.transform;
            _leakOrigin.SetParent(transform, true);
            _leakOrigin.position = _fallbackWorldPosition;
        }

        private void EnsureSource()
        {
            EnsureOrigin();
            if (_audioSource == null && _leakOrigin != null)
                _audioSource = _leakOrigin.GetComponent<AudioSource>();
            if (_audioSource == null && _leakOrigin != null)
                _audioSource = _leakOrigin.gameObject.AddComponent<AudioSource>();
            if (_audioSource == null)
                return;

            _audioSource.playOnAwake = false;
            _audioSource.loop = true;
            _audioSource.spatialBlend = 1f;
            _audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            _audioSource.minDistance = _minDistance;
            _audioSource.maxDistance = Mathf.Max(_minDistance + 1f, _maxDistance);
            _audioSource.volume = _volume;
            _audioSource.dopplerLevel = 0f;
            if (_leakOrigin != null)
                _audioSource.transform.position = _leakOrigin.position;

            if (_leakClip == null && _generatedClip == null)
                _generatedClip = CreateHissClip();
        }

        private static AudioClip CreateHissClip()
        {
            const int sampleRate = 22050;
            const float seconds = 1.5f;
            var sampleCount = Mathf.RoundToInt(sampleRate * seconds);
            var samples = new float[sampleCount];
            var previous = 0f;
            for (var i = 0; i < sampleCount; i++)
            {
                var white = (Random.value * 2f) - 1f;
                previous = (previous * 0.86f) + (white * 0.14f);
                var envelope = 0.22f + (0.08f * Mathf.Sin((i / (float)sampleRate) * 7.5f));
                samples[i] = Mathf.Clamp(previous * envelope, -0.4f, 0.4f);
            }

            var clip = AudioClip.Create("PreEntryLeakHiss", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
