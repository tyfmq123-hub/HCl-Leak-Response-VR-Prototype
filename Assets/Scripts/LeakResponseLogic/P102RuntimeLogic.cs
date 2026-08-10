using System.Collections.Generic;

namespace VRProject.LeakResponse.Logic
{
    public sealed class SingleExecutionGate
    {
        public bool HasEntered { get; private set; }

        public bool TryEnter()
        {
            if (HasEntered)
                return false;

            HasEntered = true;
            return true;
        }
    }

    public sealed class ContinuousGazeTimer
    {
        readonly float requiredSeconds;

        public ContinuousGazeTimer(float seconds)
        {
            requiredSeconds = seconds > 0f ? seconds : 0.0001f;
        }

        public float ElapsedSeconds { get; private set; }
        public bool IsComplete { get; private set; }

        public bool Tick(bool isGazingAtTarget, float deltaTime)
        {
            if (IsComplete)
                return false;

            if (!isGazingAtTarget)
            {
                ElapsedSeconds = 0f;
                return false;
            }

            if (deltaTime > 0f)
                ElapsedSeconds += deltaTime;

            if (ElapsedSeconds < requiredSeconds)
                return false;

            ElapsedSeconds = requiredSeconds;
            IsComplete = true;
            return true;
        }

        public void Reset()
        {
            ElapsedSeconds = 0f;
            IsComplete = false;
        }
    }

    public sealed class UniqueDepositGate
    {
        readonly HashSet<int> depositedIds = new HashSet<int>();

        public UniqueDepositGate(int requiredCount)
        {
            RequiredCount = requiredCount > 0 ? requiredCount : 1;
        }

        public int RequiredCount { get; }
        public int DepositedCount => depositedIds.Count;
        public bool CanFinalLock => DepositedCount >= RequiredCount;
        public bool IsLocked { get; private set; }

        public bool TryDeposit(int uniqueId)
        {
            return !IsLocked && uniqueId != 0 && depositedIds.Add(uniqueId);
        }

        public bool TryLock()
        {
            if (IsLocked || !CanFinalLock)
                return false;

            IsLocked = true;
            return true;
        }
    }
}
