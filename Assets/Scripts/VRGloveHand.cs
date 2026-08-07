using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 리깅된 장갑 모델을 VR 컨트롤러 입력(그립/트리거)에 따라
/// 손가락을 쥐었다 폈다 하게 만드는 스크립트.
/// 장갑의 손목(핸드 루트) 오브젝트에 붙이고, 손가락 본을 할당하면 됩니다.
/// 컴포넌트 우클릭 → "손가락 본 자동 찾기"로 자동 할당을 시도할 수 있습니다.
/// </summary>
public class VRGloveHand : MonoBehaviour
{
    public enum Hand { Left, Right }

    [Header("손 방향")]
    public Hand handedness = Hand.Right;

    [Header("손가락 본 (뿌리 마디 → 끝 마디 순서)")]
    public Transform[] thumbBones;
    public Transform[] indexBones;
    public Transform[] middleBones;
    public Transform[] ringBones;
    public Transform[] pinkyBones;

    [Header("굽힘 설정")]
    [Tooltip("손가락이 굽어지는 로컬 회전 축. 미리보기로 확인하면서 X/Y/Z 축을 바꿔보세요. 반대로 굽으면 부호를 뒤집으세요(예: 0,0,-1).")]
    public Vector3 fingerCurlAxis = new Vector3(0f, 0f, 1f);
    [Tooltip("주먹을 쥐었을 때 손가락 마디 하나당 굽는 각도")]
    public float fingerCurlAngle = 60f;

    [Tooltip("엄지가 굽어지는 로컬 회전 축")]
    public Vector3 thumbCurlAxis = new Vector3(0f, 0f, 1f);
    [Tooltip("주먹을 쥐었을 때 엄지 마디 하나당 굽는 각도")]
    public float thumbCurlAngle = 35f;

    [Tooltip("쥐고 펴는 속도")]
    public float curlSpeed = 12f;

    [Header("입력")]
    [Tooltip("검지는 그립 대신 트리거를 따라가게 할지 여부")]
    public bool indexFollowsTrigger = true;
    [Tooltip("에디터 테스트용: G 키 = 그립, T 키 = 트리거")]
    public bool keyboardDebug = true;

    InputAction gripAction;
    InputAction triggerAction;

    float currentGrip;
    float currentTrigger;

    // 시작 시점의 펴진 손 포즈 저장용
    readonly Dictionary<Transform, Quaternion> openPose = new Dictionary<Transform, Quaternion>();
    bool poseCaptured;

    void Awake()
    {
        CaptureOpenPose();
    }

    void OnEnable()
    {
        string hand = handedness == Hand.Left ? "LeftHand" : "RightHand";
        gripAction = new InputAction("Grip", InputActionType.Value,
            $"<XRController>{{{hand}}}/grip", expectedControlType: "Axis");
        triggerAction = new InputAction("Trigger", InputActionType.Value,
            $"<XRController>{{{hand}}}/trigger", expectedControlType: "Axis");
        gripAction.Enable();
        triggerAction.Enable();
    }

    void OnDisable()
    {
        gripAction?.Disable();
        gripAction?.Dispose();
        triggerAction?.Disable();
        triggerAction?.Dispose();
        gripAction = null;
        triggerAction = null;
    }

    void Update()
    {
        float targetGrip = gripAction.ReadValue<float>();
        float targetTrigger = triggerAction.ReadValue<float>();

        if (keyboardDebug && Keyboard.current != null)
        {
            if (Keyboard.current.gKey.isPressed) targetGrip = 1f;
            if (Keyboard.current.tKey.isPressed) targetTrigger = 1f;
        }

        currentGrip = Mathf.MoveTowards(currentGrip, targetGrip, curlSpeed * Time.deltaTime);
        currentTrigger = Mathf.MoveTowards(currentTrigger, targetTrigger, curlSpeed * Time.deltaTime);

        float indexCurl = indexFollowsTrigger ? Mathf.Max(currentTrigger, currentGrip) : currentGrip;

        ApplyFinger(thumbBones, currentGrip, thumbCurlAxis, thumbCurlAngle);
        ApplyFinger(indexBones, indexCurl, fingerCurlAxis, fingerCurlAngle);
        ApplyFinger(middleBones, currentGrip, fingerCurlAxis, fingerCurlAngle);
        ApplyFinger(ringBones, currentGrip, fingerCurlAxis, fingerCurlAngle);
        ApplyFinger(pinkyBones, currentGrip, fingerCurlAxis, fingerCurlAngle);
    }

    void CaptureOpenPose()
    {
        if (poseCaptured) return;
        foreach (var bone in AllBones())
            openPose[bone] = bone.localRotation;
        poseCaptured = true;
    }

    IEnumerable<Transform> AllBones()
    {
        foreach (var arr in new[] { thumbBones, indexBones, middleBones, ringBones, pinkyBones })
        {
            if (arr == null) continue;
            foreach (var b in arr)
                if (b != null) yield return b;
        }
    }

    void ApplyFinger(Transform[] bones, float curl, Vector3 axis, float anglePerJoint)
    {
        if (bones == null) return;
        foreach (var bone in bones)
        {
            if (bone == null || !openPose.TryGetValue(bone, out var open)) continue;
            bone.localRotation = open * Quaternion.AngleAxis(anglePerJoint * curl, axis);
        }
    }

    // ---------- 에디터 편의 기능 ----------

    static readonly string[][] fingerKeywords =
    {
        new[] { "thumb", "엄지" },
        new[] { "index", "point", "검지" },
        new[] { "middle", "중지" },
        new[] { "ring", "약지" },
        new[] { "pinky", "little", "소지", "새끼" },
    };

    [ContextMenu("손가락 본 자동 찾기")]
    public void AutoAssignBones()
    {
        thumbBones = FindFingerChain(fingerKeywords[0]);
        indexBones = FindFingerChain(fingerKeywords[1]);
        middleBones = FindFingerChain(fingerKeywords[2]);
        ringBones = FindFingerChain(fingerKeywords[3]);
        pinkyBones = FindFingerChain(fingerKeywords[4]);

        Debug.Log($"[VRGloveHand] 자동 할당 결과 - 엄지:{thumbBones.Length} 검지:{indexBones.Length} " +
                  $"중지:{middleBones.Length} 약지:{ringBones.Length} 새끼:{pinkyBones.Length}\n" +
                  "0개인 손가락이 있으면 본 이름이 일반적인 규칙과 달라서 못 찾은 것이니, " +
                  "Hierarchy에서 해당 본을 직접 드래그해서 넣어주세요.");
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    Transform[] FindFingerChain(string[] keywords)
    {
        var matches = GetComponentsInChildren<Transform>(true)
            .Where(t => keywords.Any(k => t.name.ToLowerInvariant().Contains(k)))
            .Where(t =>
            {
                string n = t.name.ToLowerInvariant();
                // 스킨에 영향 없는 끝점 본은 제외
                return !n.Contains("tip") && !n.EndsWith("_end") && !n.EndsWith(" end");
            })
            .OrderBy(Depth)
            .ToArray();
        return matches;
    }

    static int Depth(Transform t)
    {
        int d = 0;
        while (t.parent != null) { d++; t = t.parent; }
        return d;
    }

    [ContextMenu("미리보기: 주먹 쥐기")]
    void PreviewFist()
    {
        CaptureOpenPose();
        ApplyFinger(thumbBones, 1f, thumbCurlAxis, thumbCurlAngle);
        ApplyFinger(indexBones, 1f, fingerCurlAxis, fingerCurlAngle);
        ApplyFinger(middleBones, 1f, fingerCurlAxis, fingerCurlAngle);
        ApplyFinger(ringBones, 1f, fingerCurlAxis, fingerCurlAngle);
        ApplyFinger(pinkyBones, 1f, fingerCurlAxis, fingerCurlAngle);
    }

    [ContextMenu("미리보기: 손 펴기 (원상복구)")]
    void PreviewOpen()
    {
        foreach (var bone in AllBones())
            if (openPose.TryGetValue(bone, out var open))
                bone.localRotation = open;
    }
}
