using System;
using HClTraining.SharedXR;

namespace HClTraining.App
{
    /// <summary>
    /// Runtime facts that must survive Stage transitions. Scene objects, valves,
    /// effects and authored interaction progress remain owned by their Stage.
    /// </summary>
    [Serializable]
    public sealed class IncidentSessionState : ISharedIncidentState
    {
        public FacilityRunState FacilityRunState { get; private set; } = FacilityRunState.Running;
        public LeakState LeakState { get; private set; } = LeakState.Active;

        public void Reset()
        {
            FacilityRunState = FacilityRunState.Running;
            LeakState = LeakState.Active;
        }

        public bool SetFacilityRunState(FacilityRunState state)
        {
            if (FacilityRunState == state)
                return false;
            FacilityRunState = state;
            return true;
        }

        public bool SetLeakState(LeakState state)
        {
            if (LeakState == state)
                return false;
            LeakState = state;
            return true;
        }
    }
}
