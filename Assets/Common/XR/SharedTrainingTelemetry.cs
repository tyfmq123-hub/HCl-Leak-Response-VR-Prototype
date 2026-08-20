namespace HClTraining.SharedXR
{
    public enum TrainingStepId
    {
        None = 0,
        PPE = 1,
        EquipmentPreparation = 2,
        PreEntry = 3,
        SuctionValve = 4,
        DischargeValve = 5,
        AbsorbentSock = 6,
        AbsorbentPad = 7,
        Recovery = 8,
        WasteDisposal = 9,
    }

    public interface IStageTrainingTelemetry
    {
        void StepEntered(TrainingStepId step);
        void StepCompleted(TrainingStepId step, int attemptCount = 0, int rejectedCount = 0);
        void ContentViewed(string contentId);
        void BagItemRequested(string equipmentId, bool success);
        void EquipmentUsed(string equipmentId);
    }

    /// <summary>
    /// Stage-safe event bridge. Missing App or storage infrastructure is a no-op,
    /// so standalone Stage development never depends on a server or database.
    /// </summary>
    public static class SharedTrainingTelemetry
    {
        static readonly IStageTrainingTelemetry Null = new NullStageTrainingTelemetry();
        static IStageTrainingTelemetry current = Null;

        public static IStageTrainingTelemetry Current => current;

        public static void Register(IStageTrainingTelemetry telemetry)
        {
            current = telemetry ?? Null;
        }

        public static void Unregister(IStageTrainingTelemetry telemetry)
        {
            if (ReferenceEquals(current, telemetry))
                current = Null;
        }

        sealed class NullStageTrainingTelemetry : IStageTrainingTelemetry
        {
            public void StepEntered(TrainingStepId step) { }
            public void StepCompleted(TrainingStepId step, int attemptCount = 0, int rejectedCount = 0) { }
            public void ContentViewed(string contentId) { }
            public void BagItemRequested(string equipmentId, bool success) { }
            public void EquipmentUsed(string equipmentId) { }
        }
    }
}
