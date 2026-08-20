using HClTraining.SharedXR;

namespace JH.EntrySafety
{
    /// <summary>
    /// Maps the shared FacilityId contract to PreEntry-local panel labels without
    /// hard-coding a single P-102 path.
    /// </summary>
    public static class EntrySafetyFacilityCatalog
    {
        public static FacilityId ResolveSelectedFacility()
        {
            return Normalize(SharedIncidentState.Current.SelectedFacility);
        }

        public static FacilityId Normalize(FacilityId facility)
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

        public static string ToDisplayName(FacilityId facility)
        {
            switch (Normalize(facility))
            {
                case FacilityId.P101:
                    return "P-101";
                case FacilityId.P103:
                    return "P-103";
                case FacilityId.P104:
                    return "P-104";
                default:
                    return "P-102";
            }
        }

        public static string PressureMeshName(FacilityId facility) =>
            "PressureMesh_" + ToDisplayName(facility);

        public static string TemperatureMeshName(FacilityId facility) =>
            "TemperatureMesh_" + ToDisplayName(facility);

        public static string StatusMeshName(FacilityId facility) =>
            "StatusMesh_" + ToDisplayName(facility);

        public static string StopPressName(FacilityId facility)
        {
            switch (Normalize(facility))
            {
                case FacilityId.P101:
                    return "StopButton_P101_Press";
                case FacilityId.P103:
                    return "StopButton_P103_Press";
                case FacilityId.P104:
                    return "StopButton_P104_Press";
                default:
                    return "StopButton_P102_Press";
            }
        }
    }
}
