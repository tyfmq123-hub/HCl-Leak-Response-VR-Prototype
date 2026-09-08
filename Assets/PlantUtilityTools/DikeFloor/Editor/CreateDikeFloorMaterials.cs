using UnityEditor;
using UnityEngine;

public static class CreateDikeFloorMaterials
{
    private const string Root = "Assets/PlantUtilityTools/DikeFloor";
    private const string MaterialFolder = Root + "/Materials";

    public static void CreateMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder(Root, "Materials");
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            Debug.LogError("URP/Lit 또는 Standard Shader를 찾지 못했습니다.");
            return;
        }

        CreateMaterial("DikeInterior", new Color32(119, 119, 115, 255),
            Root + "/Textures/DikeInterior.png", 0.18f, shader);

        CreateMaterial("YardFloor", new Color32(196, 193, 186, 255),
            Root + "/Textures/YardFloor.png", 0.18f, shader);

        CreateMaterial("DikeCurb", new Color32(146, 145, 140, 255),
            Root + "/Textures/DikeCurb.png", 0.20f, shader);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Material>(
            MaterialFolder + "/DikeInterior.mat"
        );

        Debug.Log("다이크 바닥 재질 생성 완료: DikeInterior, YardFloor, DikeCurb");
    }

    private static void CreateMaterial(
        string name,
        Color color,
        string texturePath,
        float smoothness,
        Shader shader
    )
    {
        string materialPath = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            material.shader = shader;
        }

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);

        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", texture);

        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);

        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", smoothness);

        EditorUtility.SetDirty(material);
    }
}
