using UnityEditor;
using UnityEngine;

public static class CreateChainLinkFence
{
    public static void CreateFence()
    {
        GameObject root = new GameObject("Fence_Panel_Root");

        float width = 6f;
        float height = 2.2f;
        float postRadius = 0.06f;
        float railRadius = 0.04f;
        float meshRadius = 0.012f;

        Material metalMat = CreateMaterial("Mat_Galvanized_Metal", new Color(0.55f, 0.57f, 0.56f), 0.15f);
        Material darkWireMat = CreateMaterial("Mat_Fence_Wire_Dark", new Color(0.28f, 0.30f, 0.30f), 0.2f);
        if (metalMat == null || darkWireMat == null)
        {
            Object.DestroyImmediate(root);
            return;
        }

        // 기둥
        CreateCylinder(root.transform, "Post_Left", new Vector3(-width / 2f, height / 2f, 0), height, postRadius, Quaternion.identity, metalMat);
        CreateCylinder(root.transform, "Post_Right", new Vector3(width / 2f, height / 2f, 0), height, postRadius, Quaternion.identity, metalMat);
        CreateCylinder(root.transform, "Post_Middle", new Vector3(0, height / 2f, 0), height, postRadius, Quaternion.identity, metalMat);

        // 상단/하단 가로봉
        CreateCylinder(root.transform, "Top_Rail", new Vector3(0, height, 0), width, railRadius, Quaternion.Euler(0, 0, 90), metalMat);
        CreateCylinder(root.transform, "Bottom_Rail", new Vector3(0, 0.15f, 0), width, railRadius, Quaternion.Euler(0, 0, 90), metalMat);

        // 바닥 고정판
        CreateBasePlate(root.transform, "Base_Left", new Vector3(-width / 2f, 0, 0), metalMat);
        CreateBasePlate(root.transform, "Base_Right", new Vector3(width / 2f, 0, 0), metalMat);
        CreateBasePlate(root.transform, "Base_Middle", new Vector3(0, 0, 0), metalMat);

        // 철망 영역
        float meshBottom = 0.25f;
        float meshTop = height - 0.15f;
        float meshHeight = meshTop - meshBottom;
        float spacing = 0.35f;

        int index = 0;

        // 대각선 A 방향
        for (float x = -width; x <= width; x += spacing)
        {
            Vector3 start = new Vector3(x, meshBottom, -0.01f);
            Vector3 end = new Vector3(x + meshHeight, meshTop, -0.01f);

            ClipAndCreateWire(root.transform, $"Wire_A_{index}", start, end, -width / 2f, width / 2f, meshBottom, meshTop, meshRadius, darkWireMat);
            index++;
        }

        index = 0;

        // 대각선 B 방향
        for (float x = -width; x <= width; x += spacing)
        {
            Vector3 start = new Vector3(x, meshTop, 0.01f);
            Vector3 end = new Vector3(x + meshHeight, meshBottom, 0.01f);

            ClipAndCreateWire(root.transform, $"Wire_B_{index}", start, end, -width / 2f, width / 2f, meshBottom, meshTop, meshRadius, darkWireMat);
            index++;
        }

        Selection.activeGameObject = root;
        Debug.Log("철조망 패널 생성 완료");
    }

    private static Material CreateMaterial(string name, Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("URP Lit / Standard 셰이더를 찾지 못해 Material을 생성할 수 없습니다.");
            return null;
        }

        Material mat = new Material(shader);
        mat.name = name;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);

        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);

        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0.0f);

        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", smoothness);

        return mat;
    }

    private static void CreateCylinder(
        Transform parent,
        string name,
        Vector3 position,
        float length,
        float radius,
        Quaternion rotation,
        Material material)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.position = position;
        obj.transform.rotation = rotation;

        // Unity Cylinder 기본 높이는 Y축 2
        obj.transform.localScale = new Vector3(radius * 2f, length / 2f, radius * 2f);

        Renderer renderer = obj.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
    }

    private static void CreateBasePlate(Transform parent, string name, Vector3 position, Material material)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = name;
        plate.transform.SetParent(parent);
        plate.transform.position = new Vector3(position.x, 0.02f, position.z);
        plate.transform.localScale = new Vector3(0.35f, 0.04f, 0.25f);

        Renderer renderer = plate.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
    }

    private static void ClipAndCreateWire(
        Transform parent,
        string name,
        Vector3 start,
        Vector3 end,
        float minX,
        float maxX,
        float minY,
        float maxY,
        float radius,
        Material material)
    {
        if (!ClipLineToRect(ref start, ref end, minX, maxX, minY, maxY))
            return;

        Vector3 mid = (start + end) * 0.5f;
        Vector3 dir = end - start;
        float length = dir.magnitude;

        if (length < 0.05f)
            return;

        GameObject wire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wire.name = name;
        wire.transform.SetParent(parent);
        wire.transform.position = mid;

        // Cylinder의 Y축을 선 방향에 맞춘다.
        wire.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        wire.transform.localScale = new Vector3(radius * 2f, length / 2f, radius * 2f);

        Renderer renderer = wire.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
    }

    private static bool ClipLineToRect(
        ref Vector3 start,
        ref Vector3 end,
        float minX,
        float maxX,
        float minY,
        float maxY)
    {
        Vector3 d = end - start;
        float t0 = 0f;
        float t1 = 1f;

        if (!ClipTest(-d.x, start.x - minX, ref t0, ref t1)) return false;
        if (!ClipTest(d.x, maxX - start.x, ref t0, ref t1)) return false;
        if (!ClipTest(-d.y, start.y - minY, ref t0, ref t1)) return false;
        if (!ClipTest(d.y, maxY - start.y, ref t0, ref t1)) return false;

        Vector3 originalStart = start;
        Vector3 originalEnd = end;

        start = originalStart + t0 * (originalEnd - originalStart);
        end = originalStart + t1 * (originalEnd - originalStart);

        return true;
    }

    private static bool ClipTest(float p, float q, ref float t0, ref float t1)
    {
        if (Mathf.Approximately(p, 0f))
            return q >= 0f;

        float r = q / p;

        if (p < 0f)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }

        return true;
    }
}
