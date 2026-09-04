using UnityEngine;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class PPEStateController : MonoBehaviour
    {
        TrainingSessionState session;
        PersistentEquipmentInventory inventory;
        ChemicalGlovesWearableVisual wearable;
        PPEEquipmentManager boundEquipmentManager;

        public void Initialize(
            TrainingSessionState state,
            PersistentEquipmentInventory equipmentInventory,
            Transform commonXrRoot)
        {
            session = state;
            inventory = equipmentInventory;
            wearable = commonXrRoot != null
                ? commonXrRoot.GetComponentInChildren<ChemicalGlovesWearableVisual>(true)
                : null;
        }

        public void BindPpeStage()
        {
            UnbindPpeStage();
            boundEquipmentManager = PPEEquipmentManager.Instance != null
                ? PPEEquipmentManager.Instance
                : FindAnyObjectByType<PPEEquipmentManager>(FindObjectsInactive.Include);
            if (boundEquipmentManager != null)
                boundEquipmentManager.StateChanged += OnPpeEquipmentChanged;
            OnPpeEquipmentChanged();
        }

        public void UnbindPpeStage()
        {
            if (boundEquipmentManager != null)
                boundEquipmentManager.StateChanged -= OnPpeEquipmentChanged;
            boundEquipmentManager = null;
        }

        public void ApplyPersistentState()
        {
            if (wearable == null)
                wearable = FindAnyObjectByType<ChemicalGlovesWearableVisual>(FindObjectsInactive.Include);
            if (wearable != null && session != null)
                ApplyGloveVisualState(
                    session.LeftChemicalGloveWorn,
                    session.RightChemicalGloveWorn);

        }

        public void CommitPpeResult()
        {
            PPELoadoutData.CommitPpeSceneResultFromManagers();
            inventory?.SyncFromLoadout();
            ApplyPersistentState();
        }

        void OnPpeEquipmentChanged()
        {
            var glovesEquipped = boundEquipmentManager != null &&
                                 boundEquipmentManager.IsEquipped(PPEEquipmentManager.IdChemicalGloves);
            if (wearable != null)
                ApplyGloveVisualState(glovesEquipped, glovesEquipped);

        }

        void ApplyGloveVisualState(bool leftWorn, bool rightWorn)
        {
            if (wearable == null)
                return;

            if (session != null && session.CurrentStage == TrainingStage.PPE)
            {
                wearable.ApplyPpeRoomState(leftWorn, rightWorn);
                return;
            }

            wearable.ApplyPersistentState(leftWorn, rightWorn);
        }

        void OnDestroy() => UnbindPpeStage();
    }
}
