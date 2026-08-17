using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRProject.ObjectInstallation;

namespace VRProject.LeakResponse.Narration
{
    public enum P102AbsorbentKind
    {
        Sock,
        Pad,
    }

    [DisallowMultipleComponent]
    public sealed class P102RecoveryItemState : MonoBehaviour
    {
        PreplacedGhostInstallItem installItem;
        P102AbsorbentKind kind;
        bool recoveryUnlocked;
        bool deposited;
        bool heldByTongs;

        public PreplacedGhostInstallItem InstallItem => installItem;
        public P102AbsorbentKind Kind => kind;
        public bool IsRecoveryEligible => installItem != null && installItem.IsInstalled &&
                                           recoveryUnlocked && !deposited && !heldByTongs;
        public bool CanBeDeposited => installItem != null && installItem.IsInstalled &&
                                      recoveryUnlocked && !deposited && heldByTongs;
        public bool IsDeposited => deposited;
        public bool IsHeldByTongs => heldByTongs;

        public void Configure(PreplacedGhostInstallItem item, P102AbsorbentKind absorbentKind)
        {
            installItem = item;
            kind = absorbentKind;
            recoveryUnlocked = false;
            deposited = false;
            heldByTongs = false;
            DisableDirectGrab();
        }

        public void UnlockForRecovery()
        {
            recoveryUnlocked = true;
            DisableDirectGrab();
            // Recovery uses the tongs' proximity test, not physical collision. Installed
            // absorbents must not behave like steps and lift the CharacterController.
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }
        }

        public void DisableDirectGrab()
        {
            XRGrabInteractable grab = installItem != null ? installItem.GrabInteractable : null;
            if (grab != null)
                grab.enabled = false;
        }

        public void SetHeldByTongs(bool held)
        {
            heldByTongs = held && !deposited;
        }

        public bool MarkDeposited()
        {
            if (deposited || installItem == null || !installItem.IsInstalled)
                return false;

            deposited = true;
            heldByTongs = false;
            return true;
        }
    }

    [DisallowMultipleComponent]
    public sealed class P102RecoveryRuntime : MonoBehaviour
    {
        const float DirectContactDistance = 0.08f;
        const float DirectContactWarningCooldown = 6f;

        readonly List<P102RecoveryItemState> items = new List<P102RecoveryItemState>();

        HClAbsorptionVisualController absorptionController;
        P102LeakNarrationController narration;
        PreplacedGhostInstallGroup sockInstallGroup;
        PreplacedGhostInstallGroup padInstallGroup;
        XRBaseInteractor[] contactInteractors = Array.Empty<XRBaseInteractor>();
        float nextDirectContactWarningTime;

        public IReadOnlyList<P102RecoveryItemState> Items => items;
        public bool IsRecoveryUnlocked { get; private set; }

        public void Configure(PreplacedGhostInstallGroup sockGroup, PreplacedGhostInstallGroup padGroup,
            HClAbsorptionVisualController absorption, P102LeakNarrationController narrationController)
        {
            Unsubscribe();
            absorptionController = absorption;
            narration = narrationController;
            sockInstallGroup = sockGroup;
            padInstallGroup = padGroup;
            items.Clear();
            AddGroupItems(sockGroup, P102AbsorbentKind.Sock);
            AddGroupItems(padGroup, P102AbsorbentKind.Pad);

            if (absorptionController != null)
                absorptionController.AbsorptionCompleted += OnAbsorptionCompleted;
            if (narration != null)
                narration.CueCompleted += OnCueCompleted;
            if (sockInstallGroup != null)
                sockInstallGroup.Installed += OnInstallGroupCompleted;
            if (padInstallGroup != null)
                padInstallGroup.Installed += OnInstallGroupCompleted;

            if (absorptionController != null && absorptionController.IsAbsorptionComplete)
                OnAbsorptionCompleted();
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        void Update()
        {
            if (!IsRecoveryUnlocked || narration == null ||
                Time.unscaledTime < nextDirectContactWarningTime)
                return;

            for (int interactorIndex = 0; interactorIndex < contactInteractors.Length; ++interactorIndex)
            {
                XRBaseInteractor interactor = contactInteractors[interactorIndex];
                if (interactor == null || !interactor.isActiveAndEnabled)
                    continue;

                Vector3 contactPoint = interactor.transform.position;
                for (int itemIndex = 0; itemIndex < items.Count; ++itemIndex)
                {
                    P102RecoveryItemState item = items[itemIndex];
                    if (item == null || !item.IsRecoveryEligible ||
                        DistanceSqrToItem(item, contactPoint) > DirectContactDistance * DirectContactDistance)
                        continue;

                    narration.EnqueueRepeatable(P102LeakNarrationCue.DirectContactAttempted);
                    nextDirectContactWarningTime = Time.unscaledTime + DirectContactWarningCooldown;
                    return;
                }
            }
        }

        public bool HasRemaining(P102AbsorbentKind kind)
        {
            for (int i = 0; i < items.Count; ++i)
            {
                P102RecoveryItemState item = items[i];
                if (item != null && item.Kind == kind && !item.IsDeposited)
                    return true;
            }

            return false;
        }

        void OnAbsorptionCompleted()
        {
            if (IsRecoveryUnlocked)
                return;

            IsRecoveryUnlocked = true;
            for (int i = 0; i < items.Count; ++i)
                items[i]?.UnlockForRecovery();
            ResolveDirectInteractors();
        }

        void OnCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.AbsorptionCompleted)
                narration.EnqueueOnce(P102LeakNarrationCue.ContainerAndLidGhostActivated);
        }

        void AddGroupItems(PreplacedGhostInstallGroup group, P102AbsorbentKind kind)
        {
            if (group == null || group.Items == null)
                return;

            for (int i = 0; i < group.Items.Length; ++i)
            {
                PreplacedGhostInstallItem installItem = group.Items[i];
                if (installItem == null)
                    continue;

                P102RecoveryItemState state = P102RuntimeUtility.GetOrAdd<P102RecoveryItemState>(installItem.gameObject);
                state.Configure(installItem, kind);
                if (!items.Contains(state))
                    items.Add(state);
            }
        }

        void OnInstallGroupCompleted(PreplacedGhostInstallGroup group)
        {
            if (group == null || group.Items == null)
                return;

            for (int i = 0; i < group.Items.Length; ++i)
            {
                PreplacedGhostInstallItem installItem = group.Items[i];
                if (installItem == null)
                    continue;
                installItem.GetComponent<P102RecoveryItemState>()?.DisableDirectGrab();
            }
        }

        void Unsubscribe()
        {
            if (absorptionController != null)
                absorptionController.AbsorptionCompleted -= OnAbsorptionCompleted;
            if (narration != null)
                narration.CueCompleted -= OnCueCompleted;
            if (sockInstallGroup != null)
                sockInstallGroup.Installed -= OnInstallGroupCompleted;
            if (padInstallGroup != null)
                padInstallGroup.Installed -= OnInstallGroupCompleted;
        }

        void ResolveDirectInteractors()
        {
            XRBaseInteractor[] candidates = Resources.FindObjectsOfTypeAll<XRBaseInteractor>();
            var sceneInteractors = new List<XRBaseInteractor>(candidates.Length);
            for (int i = 0; i < candidates.Length; ++i)
            {
                XRBaseInteractor candidate = candidates[i];
                if ((candidate is XRDirectInteractor || candidate is NearFarInteractor) &&
                    candidate.gameObject.scene.IsValid())
                    sceneInteractors.Add(candidate);
            }
            contactInteractors = sceneInteractors.ToArray();
        }

        static float DistanceSqrToItem(P102RecoveryItemState item, Vector3 worldPoint)
        {
            Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < colliders.Length; ++i)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled)
                    continue;
                float candidate = (collider.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude;
                if (candidate < nearest)
                    nearest = candidate;
            }
            return float.IsPositiveInfinity(nearest)
                ? (item.transform.position - worldPoint).sqrMagnitude
                : nearest;
        }
    }
}
