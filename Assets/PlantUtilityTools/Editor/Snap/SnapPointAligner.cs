using UnityEngine;
using UnityEditor;

/// <summary>
/// 배관/밸브/엘보 같은 모델에 SnapPoint를 만들고,
/// SnapPoint끼리 위치/회전을 맞추는 에디터 전용 도구.
/// 
/// 사용 위치:
/// Unity 상단 메뉴 > Tools > Snap
/// 
/// 주의:
/// 이 스크립트는 Editor 폴더 안에 있어야 한다.
/// 예: Assets/Editor/SnapPointTools.cs
/// </summary>
public static class SnapPointTools
{
    // =========================================================
    // 1. 직선 파이프용 SnapPoint 자동 생성
    // =========================================================
    // X축 방향으로 긴 파이프에 사용.
    // Snap_Left, Snap_Right를 만든다.
    public static void CreateStraightPipeSnapPointsXAxis()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogWarning("먼저 SnapPoint를 만들 오브젝트를 선택하세요.");
            return;
        }

        if (!TryGetWorldBounds(selected, out Bounds bounds))
        {
            Debug.LogWarning("선택한 오브젝트 안에 Renderer가 없습니다.");
            return;
        }

        Vector3 center = bounds.center;

        Vector3 leftPoint = new Vector3(bounds.min.x, center.y, center.z);
        Vector3 rightPoint = new Vector3(bounds.max.x, center.y, center.z);

        Transform left = CreateOrMoveSnapPoint(selected.transform, "Snap_Left", leftPoint);
        Transform right = CreateOrMoveSnapPoint(selected.transform, "Snap_Right", rightPoint);

        // 방향 설정
        // SnapPoint의 파란색 Z축 방향을 연결 방향으로 사용한다.
        left.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        right.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

        Debug.Log($"직선 파이프 X축 SnapPoint 생성 완료: {selected.name}");
    }

    // Z축 방향으로 긴 파이프에 사용.
    // Snap_Back, Snap_Front를 만든다.
    public static void CreateStraightPipeSnapPointsZAxis()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogWarning("먼저 SnapPoint를 만들 오브젝트를 선택하세요.");
            return;
        }

        if (!TryGetWorldBounds(selected, out Bounds bounds))
        {
            Debug.LogWarning("선택한 오브젝트 안에 Renderer가 없습니다.");
            return;
        }

        Vector3 center = bounds.center;

        Vector3 backPoint = new Vector3(center.x, center.y, bounds.min.z);
        Vector3 frontPoint = new Vector3(center.x, center.y, bounds.max.z);

        Transform back = CreateOrMoveSnapPoint(selected.transform, "Snap_Back", backPoint);
        Transform front = CreateOrMoveSnapPoint(selected.transform, "Snap_Front", frontPoint);

        back.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        front.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

        Debug.Log($"직선 파이프 Z축 SnapPoint 생성 완료: {selected.name}");
    }

    // =========================================================
    // 2. 엘보용 SnapPoint 대략 생성
    // =========================================================
    // X축에서 들어와 Y축으로 나가는 엘보 기준.
    // 즉, 바닥 방향/수평 방향 모델에 따라 미세 조정 필요.
    public static void CreateElbowSnapPointsXToY()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogWarning("먼저 elbow 오브젝트를 선택하세요.");
            return;
        }

        if (!TryGetWorldBounds(selected, out Bounds bounds))
        {
            Debug.LogWarning("선택한 오브젝트 안에 Renderer가 없습니다.");
            return;
        }

        Vector3 center = bounds.center;

        // 대략적인 엘보 입구/출구 위치
        // Snap_In  : X축 음수 방향 끝
        // Snap_Out : Y축 양수 방향 끝
        Vector3 snapInPosition = new Vector3(bounds.min.x, center.y, center.z);
        Vector3 snapOutPosition = new Vector3(center.x, bounds.max.y, center.z);

        Transform snapIn = CreateOrMoveSnapPoint(selected.transform, "Snap_In", snapInPosition);
        Transform snapOut = CreateOrMoveSnapPoint(selected.transform, "Snap_Out", snapOutPosition);

        // 연결 방향 설정
        snapIn.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        snapOut.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

        Debug.Log($"엘보 X→Y SnapPoint 생성 완료: {selected.name}. 위치는 대략값이므로 Scene에서 한 번 확인하세요.");
    }

    // X축에서 Z축으로 나가는 엘보 기준.
    public static void CreateElbowSnapPointsXToZ()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogWarning("먼저 elbow 오브젝트를 선택하세요.");
            return;
        }

        if (!TryGetWorldBounds(selected, out Bounds bounds))
        {
            Debug.LogWarning("선택한 오브젝트 안에 Renderer가 없습니다.");
            return;
        }

        Vector3 center = bounds.center;

        Vector3 snapInPosition = new Vector3(bounds.min.x, center.y, center.z);
        Vector3 snapOutPosition = new Vector3(center.x, center.y, bounds.max.z);

        Transform snapIn = CreateOrMoveSnapPoint(selected.transform, "Snap_In", snapInPosition);
        Transform snapOut = CreateOrMoveSnapPoint(selected.transform, "Snap_Out", snapOutPosition);

        snapIn.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        snapOut.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

        Debug.Log($"엘보 X→Z SnapPoint 생성 완료: {selected.name}. 위치는 대략값이므로 Scene에서 한 번 확인하세요.");
    }

    // =========================================================
    // 3. 선택한 SnapPoint를 다른 SnapPoint에 위치만 맞추기
    // =========================================================
public static void AlignSnapPointPositionOnly()
{
    Transform[] selected = Selection.transforms;

    if (selected.Length != 2)
    {
        Debug.LogWarning("SnapPoint 2개를 선택하세요. 움직일 SnapPoint 하나, 고정할 SnapPoint 하나가 필요합니다.");
        return;
    }

    // Unity에서는 마지막에 클릭한 오브젝트가 activeTransform이 된다.
    // 그래서 마지막에 클릭한 SnapPoint를 '고정 대상'으로 사용한다.
    Transform targetSnap = Selection.activeTransform;

    Transform movingSnap = selected[0] == targetSnap ? selected[1] : selected[0];

    if (movingSnap == null || targetSnap == null)
    {
        Debug.LogWarning("선택된 SnapPoint를 확인할 수 없습니다.");
        return;
    }

    Transform movingRoot = movingSnap.parent;

    if (movingRoot == null)
    {
        Debug.LogWarning("움직일 SnapPoint의 부모 Root를 찾지 못했습니다.");
        return;
    }

    Undo.RecordObject(movingRoot, "Align SnapPoint Position Only");

    Vector3 offset = targetSnap.position - movingSnap.position;

    movingRoot.position += offset;

    Debug.Log($"위치 정렬 완료: 움직인 쪽 = {movingSnap.name}, 고정 대상 = {targetSnap.name}");
}
    // =========================================================
    // 4. 선택한 SnapPoint를 다른 SnapPoint에 위치 + 회전 맞추기
    // =========================================================
    public static void AlignSelectedSnapPointToTargetPositionAndRotation()
    {
        Transform[] selected = Selection.transforms;

        if (selected.Length != 2)
        {
            Debug.LogWarning("SnapPoint 2개를 선택하세요. 움직일 SnapPoint 하나, 고정할 SnapPoint 하나가 필요합니다. (마지막 클릭 = 고정 대상)");
            return;
        }

        // Match AlignSnapPointPositionOnly: last-clicked object is the fixed target.
        Transform targetSnap = Selection.activeTransform;
        Transform movingSnap = selected[0] == targetSnap ? selected[1] : selected[0];

        if (movingSnap == null || targetSnap == null)
        {
            Debug.LogWarning("선택된 SnapPoint를 확인할 수 없습니다.");
            return;
        }

        Transform movingRoot = movingSnap.parent;

        if (movingRoot == null)
        {
            Debug.LogWarning("움직일 SnapPoint의 부모 Root를 찾지 못했습니다.");
            return;
        }

        Undo.RecordObject(movingRoot, "Align Snap Point Position And Rotation");

        // 1단계: 회전 맞추기
        //
        // movingSnap의 방향이 targetSnap의 반대 방향을 보게 만든다.
        // 배관 연결은 보통 서로 마주 봐야 하므로 targetSnap.forward의 반대 방향을 사용한다.
        Quaternion targetRotation = Quaternion.LookRotation(-targetSnap.forward, targetSnap.up);
        Quaternion rotationOffset = targetRotation * Quaternion.Inverse(movingSnap.rotation);

        movingRoot.rotation = rotationOffset * movingRoot.rotation;

        // 2단계: 회전 후 위치 다시 계산해서 맞추기
        Vector3 positionOffset = targetSnap.position - movingSnap.position;
        movingRoot.position += positionOffset;

        Debug.Log($"위치 + 회전 정렬 완료: 움직인 쪽 = {movingSnap.name}, 고정 대상 = {targetSnap.name}");
    }

    // =========================================================
    // 공통 기능: Renderer Bounds 계산
    // =========================================================
    private static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return true;
    }

    // =========================================================
    // 공통 기능: SnapPoint 생성 또는 기존 SnapPoint 이동
    // =========================================================
    private static Transform CreateOrMoveSnapPoint(Transform parent, string name, Vector3 worldPosition)
    {
        Transform existing = parent.Find(name);

        GameObject snapObject;

        if (existing == null)
        {
            snapObject = new GameObject(name);
            snapObject.transform.SetParent(parent);
            Undo.RegisterCreatedObjectUndo(snapObject, $"Create {name}");
        }
        else
        {
            snapObject = existing.gameObject;
            Undo.RecordObject(snapObject.transform, $"Move {name}");
        }

        snapObject.transform.position = worldPosition;
        snapObject.transform.localScale = Vector3.one;

        // Scene에서 잘 보이게 아이콘 지정
        //EditorGUIUtility.SetIconForObject(snapObject, EditorGUIUtility.IconContent("sv_label_3").image as Texture2D);

        return snapObject.transform;
    }
}
