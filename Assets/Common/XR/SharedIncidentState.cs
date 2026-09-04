namespace HClTraining.SharedXR
{
    public enum FacilityRunState
    {
        Unknown = 0,
        Running = 1,
        Stopped = 2,
    }

    public enum LeakState
    {
        None = 0,
        Active = 1,
        Isolated = 2,
    }

    public interface ISharedIncidentState
    {
        FacilityRunState FacilityRunState { get; }
        LeakState LeakState { get; }

        bool SetFacilityRunState(FacilityRunState state);
        bool SetLeakState(LeakState state);
    }

    /// <summary>
    /// Cross-assembly bridge for the fixed P-102 incident facts. Stage code never
    /// references another Stage's scene objects through this contract.
    /// </summary>
    public static class SharedIncidentState
    {
        static readonly ISharedIncidentState Null = new NullIncidentState();
        static ISharedIncidentState current = Null;

        public static ISharedIncidentState Current => current;

        public static void Register(ISharedIncidentState state)
        {
            current = state ?? Null;
        }

        public static void Unregister(ISharedIncidentState state)
        {
            if (ReferenceEquals(current, state))
                current = Null;
        }

        sealed class NullIncidentState : ISharedIncidentState
        {
            public FacilityRunState FacilityRunState => FacilityRunState.Running;
            public LeakState LeakState => LeakState.Active;

            public bool SetFacilityRunState(FacilityRunState state) => false;
            public bool SetLeakState(LeakState state) => false;
        }
    }
}
