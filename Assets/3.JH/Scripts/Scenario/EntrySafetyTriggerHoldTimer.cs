using System;

namespace JH.EntrySafety
{
    /// <summary>Trigger hold time without the gaze exit grace period.</summary>
    public sealed class EntrySafetyTriggerHoldTimer
    {
        private readonly float _requiredHoldSeconds;

        public float ElapsedSeconds { get; private set; }
        public bool IsCompleted { get; private set; }
        public bool IsHolding { get; private set; }
        public float Progress01 => IsCompleted ? 1f : ElapsedSeconds / _requiredHoldSeconds;

        public EntrySafetyTriggerHoldTimer(float requiredHoldSeconds = 3f)
        {
            if (float.IsNaN(requiredHoldSeconds) || float.IsInfinity(requiredHoldSeconds) || requiredHoldSeconds <= 0f)
                throw new ArgumentException("requiredHoldSeconds must be a positive finite value.", nameof(requiredHoldSeconds));

            _requiredHoldSeconds = requiredHoldSeconds;
        }

        public void Update(bool isPressed, float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f || IsCompleted)
                return;

            IsHolding = isPressed;
            if (!isPressed)
            {
                ElapsedSeconds = 0f;
                return;
            }

            ElapsedSeconds += deltaTime;
            if (ElapsedSeconds >= _requiredHoldSeconds)
            {
                ElapsedSeconds = _requiredHoldSeconds;
                IsCompleted = true;
            }
        }

        public void Reset()
        {
            ElapsedSeconds = 0f;
            IsCompleted = false;
            IsHolding = false;
        }
    }
}
