using UnityEngine;

/// <summary>
/// One placement rule shared by held Ghost guidance and release-to-equip.
/// Keeping the calculation here prevents the Ready visual and the actual equip
/// result from disagreeing when rotation validation is enabled.
/// </summary>
public static class PPEPlacementValidation
{
    public readonly struct Result
    {
        public Result(
            bool targetValid,
            bool sequenceValid,
            float distance,
            float maxDistance,
            float angle,
            float maxAngle,
            bool useRotationValidation)
        {
            TargetValid = targetValid;
            SequenceValid = sequenceValid;
            Distance = distance;
            MaxDistance = maxDistance;
            Angle = angle;
            MaxAngle = maxAngle;
            UseRotationValidation = useRotationValidation;
            DistanceValid = targetValid && distance <= maxDistance;
            AngleValid = targetValid && (!useRotationValidation || angle <= maxAngle);
        }

        public bool TargetValid { get; }
        public bool SequenceValid { get; }
        public float Distance { get; }
        public float MaxDistance { get; }
        public float Angle { get; }
        public float MaxAngle { get; }
        public bool UseRotationValidation { get; }
        public bool DistanceValid { get; }
        public bool AngleValid { get; }
        public bool PoseValid => TargetValid && DistanceValid && AngleValid;
        public bool IsValid => PoseValid && SequenceValid;
    }

    public static Result Evaluate(
        Transform placementReference,
        Transform target,
        bool targetValid,
        bool sequenceValid,
        float maxDistance,
        bool useRotationValidation,
        float maxAngle)
    {
        var canMeasure = targetValid && placementReference != null && target != null;
        var distance = canMeasure
            ? Vector3.Distance(placementReference.position, target.position)
            : float.PositiveInfinity;
        var angle = canMeasure
            ? Quaternion.Angle(placementReference.rotation, target.rotation)
            : float.PositiveInfinity;

        return new Result(
            canMeasure,
            sequenceValid,
            distance,
            Mathf.Max(0.01f, maxDistance),
            angle,
            Mathf.Clamp(maxAngle, 0f, 180f),
            useRotationValidation);
    }
}
