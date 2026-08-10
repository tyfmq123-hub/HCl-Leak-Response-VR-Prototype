using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Shows only the physical marker needed for the active initial training step.</summary>
    public sealed class EntrySafetyStepVisuals : MonoBehaviour
    {
        [SerializeField] private GameObject _windsockMarker;
        [SerializeField] private GameObject _equipmentZoneMarker;

        public void SetStep(EntrySafetyStep step)
        {
            SetActive(_windsockMarker, step == EntrySafetyStep.CheckInitialWind || step == EntrySafetyStep.CheckChangedWind);
            SetActive(_equipmentZoneMarker, false);
        }

        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value)
                target.SetActive(value);
        }
    }
}
