using System;

namespace JH.EntrySafety
{
    /// <summary>Tracks continuous hand presence in a scenario zone.</summary>
    public sealed class EntrySafetyZoneDwellTimer
    {
        private readonly float _requiredSeconds;

        public float ElapsedSeconds { get; private set; }
        public bool IsCompleted { get; private set; }
        public float Progress01 => IsCompleted ? 1f : ElapsedSeconds / _requiredSeconds;

        public EntrySafetyZoneDwellTimer(float requiredSeconds = 0.2f)
        {
            if (float.IsNaN(requiredSeconds) || float.IsInfinity(requiredSeconds) || requiredSeconds <= 0f)
                throw new ArgumentException("requiredSeconds must be a positive finite value.", nameof(requiredSeconds));

            _requiredSeconds = requiredSeconds;
        }

        public void Update(bool isInside, float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f || IsCompleted)
                return;

            if (!isInside)
            {
                ElapsedSeconds = 0f;
                return;
            }

            ElapsedSeconds += deltaTime;
            if (ElapsedSeconds >= _requiredSeconds)
            {
                ElapsedSeconds = _requiredSeconds;
                IsCompleted = true;
            }
        }

        public void Reset()
        {
            ElapsedSeconds = 0f;
            IsCompleted = false;
        }
    }
}
