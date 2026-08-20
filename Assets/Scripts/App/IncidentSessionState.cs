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
        public FacilityId SelectedFacility { get; private set; } = FacilityId.P102;
        public FacilityRunState FacilityRunState { get; private set; } = FacilityRunState.Running;
        public LeakState LeakState { get; private set; } = LeakState.Active;

        public void Reset(FacilityId facility)
        {
            SelectedFacility = NormalizeFacility(facility);
            FacilityRunState = FacilityRunState.Running;
            LeakState = LeakState.Active;
        }

        public bool SelectFacility(FacilityId facility)
        {
            facility = NormalizeFacility(facility);
            if (SelectedFacility == facility)
                return false;

            SelectedFacility = facility;
            FacilityRunState = FacilityRunState.Running;
            LeakState = LeakState.Active;
            return true;
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

        static FacilityId NormalizeFacility(FacilityId facility)
        {
            switch (facility)
            {
                case FacilityId.P101:
                case FacilityId.P102:
                case FacilityId.P103:
                case FacilityId.P104:
                    return facility;
                default:
                    return FacilityId.P102;
            }
        }
    }
}
