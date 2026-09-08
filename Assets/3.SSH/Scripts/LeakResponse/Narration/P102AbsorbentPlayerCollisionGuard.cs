using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>Carried/presented supplies are not platforms. Keep their authored physics
    /// and grab layers, but exclude the active XR capsule, including after reactivation.</summary>
    [DisallowMultipleComponent]
    public sealed class P102AbsorbentPlayerCollisionGuard : MonoBehaviour
    {
        readonly List<CharacterController> players = new List<CharacterController>();
        Collider[] solids;
        float nextPlayerRefresh;

        public static void Ensure(GameObject source)
        {
            var guard = source.GetComponent<P102AbsorbentPlayerCollisionGuard>();
            if (guard == null) guard = source.AddComponent<P102AbsorbentPlayerCollisionGuard>();
            guard.Refresh();
        }

        void OnEnable() => Refresh();

        public void Refresh()
        {
            solids = GetComponentsInChildren<Collider>(true);
            FindPlayers();
            IgnorePlayerPairs();
        }

        void FindPlayers()
        {
            players.Clear();
            foreach (var origin in FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude))
            {
                if (!origin.isActiveAndEnabled || origin.Origin == null) continue;
                var player = origin.Origin.GetComponent<CharacterController>();
                if (player != null && !players.Contains(player)) players.Add(player);
            }
            nextPlayerRefresh = Time.unscaledTime + .5f;
        }

        void FixedUpdate()
        {
            if (Time.unscaledTime >= nextPlayerRefresh) FindPlayers();
            // Physics discards ignore pairs when colliders/capsules are disabled.
            // Reapply before simulation rather than remembering only an old bool.
            IgnorePlayerPairs();
        }

        void IgnorePlayerPairs()
        {
            if (solids == null) return;
            foreach (var player in players)
            {
                if (player == null || !player.enabled || !player.gameObject.activeInHierarchy) continue;
                foreach (var solid in solids)
                {
                    if (solid == null || solid == player || solid.isTrigger ||
                        !solid.enabled || !solid.gameObject.activeInHierarchy) continue;
                    if (!Physics.GetIgnoreCollision(solid, player))
                        Physics.IgnoreCollision(solid, player, true);
                }
            }
        }
    }
}
