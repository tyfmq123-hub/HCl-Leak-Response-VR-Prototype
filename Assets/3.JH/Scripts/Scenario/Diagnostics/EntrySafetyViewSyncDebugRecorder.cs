using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// Records the physical XR poses used during Editor Play Mode so a matching
    /// simulator viewpoint can be reproduced during later review.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntrySafetyViewSyncDebugRecorder : MonoBehaviour
    {
        private const float DefaultSampleIntervalSeconds = 0.25f;
        private const string TraceFileName = "LatestPoseTrace.json";

        [SerializeField] private Transform _xrOrigin;
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _leftController;
        [SerializeField] private Transform _rightController;
        [SerializeField, Min(0.05f)] private float _sampleIntervalSeconds = DefaultSampleIntervalSeconds;

        private readonly List<PoseSample> _samples = new List<PoseSample>();
        private string _tracePath;
        private float _nextSampleTime;
        private bool _hasReportedWriteFailure;

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            ResolveReferences();
            _samples.Clear();
            _tracePath = Path.Combine(Application.persistentDataPath, "JH", "ViewSyncLogs", TraceFileName);
            _nextSampleTime = Time.unscaledTime;
            WriteTrace();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || Time.unscaledTime < _nextSampleTime)
                return;

            _nextSampleTime = Time.unscaledTime + Mathf.Max(0.05f, _sampleIntervalSeconds);
            if (!HasRequiredReferences())
            {
                ResolveReferences();
                if (!HasRequiredReferences())
                    return;
            }

            _samples.Add(new PoseSample
            {
                elapsedSeconds = Time.unscaledTime,
                xrOriginWorldPosition = _xrOrigin.position,
                xrOriginWorldRotation = _xrOrigin.rotation,
                headLocalPosition = _head.localPosition,
                headLocalRotation = _head.localRotation,
                headWorldPosition = _head.position,
                headWorldRotation = _head.rotation,
                leftControllerLocalPosition = _leftController.localPosition,
                leftControllerLocalRotation = _leftController.localRotation,
                leftControllerWorldPosition = _leftController.position,
                leftControllerWorldRotation = _leftController.rotation,
                rightControllerLocalPosition = _rightController.localPosition,
                rightControllerLocalRotation = _rightController.localRotation,
                rightControllerWorldPosition = _rightController.position,
                rightControllerWorldRotation = _rightController.rotation,
                interactionSimulatorActive = IsInteractionSimulatorActive()
            });

            WriteTrace();
        }

        private void OnDisable()
        {
            if (Application.isPlaying && !string.IsNullOrEmpty(_tracePath))
                WriteTrace();
        }

        private void ResolveReferences()
        {
            if (_xrOrigin == null)
            {
                var origin = GameObject.Find("XR Origin (VR)");
                if (origin != null)
                    _xrOrigin = origin.transform;
            }

            if (_head == null)
            {
                var head = GameObject.Find("XR Origin (VR)/Camera Offset/Main Camera");
                if (head != null)
                    _head = head.transform;
            }

            if (_leftController == null)
            {
                var leftController = GameObject.Find("XR Origin (VR)/Camera Offset/Left Controller");
                if (leftController != null)
                    _leftController = leftController.transform;
            }

            if (_rightController == null)
            {
                var rightController = GameObject.Find("XR Origin (VR)/Camera Offset/Right Controller");
                if (rightController != null)
                    _rightController = rightController.transform;
            }
        }

        private bool HasRequiredReferences()
        {
            return _xrOrigin != null && _head != null && _leftController != null && _rightController != null;
        }

        private void WriteTrace()
        {
            if (string.IsNullOrEmpty(_tracePath))
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_tracePath));
                var trace = new PoseTrace
                {
                    formatVersion = 1,
                    sampleIntervalSeconds = _sampleIntervalSeconds,
                    sampleCount = _samples.Count,
                    samples = _samples.ToArray()
                };
                File.WriteAllText(_tracePath, JsonUtility.ToJson(trace, true));
            }
            catch (IOException exception)
            {
                ReportWriteFailureOnce(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                ReportWriteFailureOnce(exception);
            }
        }

        private void ReportWriteFailureOnce(Exception exception)
        {
            if (_hasReportedWriteFailure)
                return;

            _hasReportedWriteFailure = true;
            Debug.LogWarning($"EntrySafety pose trace could not be written: {exception.Message}", this);
        }

        private static bool IsInteractionSimulatorActive()
        {
            var simulator = GameObject.Find("JH_XRInteractionSimulator_EditorOnly");
            return simulator != null && simulator.activeInHierarchy;
        }

        [Serializable]
        private sealed class PoseTrace
        {
            public int formatVersion;
            public float sampleIntervalSeconds;
            public int sampleCount;
            public PoseSample[] samples;
        }

        [Serializable]
        private sealed class PoseSample
        {
            public float elapsedSeconds;
            public Vector3 xrOriginWorldPosition;
            public Quaternion xrOriginWorldRotation;
            public Vector3 headLocalPosition;
            public Quaternion headLocalRotation;
            public Vector3 headWorldPosition;
            public Quaternion headWorldRotation;
            public Vector3 leftControllerLocalPosition;
            public Quaternion leftControllerLocalRotation;
            public Vector3 leftControllerWorldPosition;
            public Quaternion leftControllerWorldRotation;
            public Vector3 rightControllerLocalPosition;
            public Quaternion rightControllerLocalRotation;
            public Vector3 rightControllerWorldPosition;
            public Quaternion rightControllerWorldRotation;
            public bool interactionSimulatorActive;
        }
    }
}
