using System;
using System.Collections.Generic;
using UnityEngine;

namespace HClTraining.App
{
    [CreateAssetMenu(menuName = "HCl Training/Stage XR Profile")]
    public sealed class StageXRProfile : ScriptableObject
    {
        [Serializable]
        public sealed class ComponentState
        {
            public string relativePath;
            public string typeName;
            public bool enabled;
        }

        [SerializeField] TrainingStage stage;
        [SerializeField] string sourceScenePath;
        [SerializeField] Vector3 spawnPosition;
        [SerializeField] Vector3 spawnEulerAngles;
        [SerializeField] List<ComponentState> componentStates = new List<ComponentState>();

        public TrainingStage Stage => stage;
        public string SourceScenePath => sourceScenePath;
        public Vector3 SpawnPosition => spawnPosition;
        public Quaternion SpawnRotation => Quaternion.Euler(spawnEulerAngles);
        public IReadOnlyList<ComponentState> ComponentStates => componentStates;

#if UNITY_EDITOR
        public void EditorConfigure(
            TrainingStage value,
            string scenePath,
            Vector3 position,
            Quaternion rotation,
            List<ComponentState> states)
        {
            stage = value;
            sourceScenePath = scenePath;
            spawnPosition = position;
            spawnEulerAngles = rotation.eulerAngles;
            componentStates = states ?? new List<ComponentState>();
        }
#endif
    }
}
