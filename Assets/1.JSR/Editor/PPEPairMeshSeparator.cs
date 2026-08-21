using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Loads persistent left/right PPE meshes extracted from connected geometry
/// islands. Does not duplicate a combined pair mesh.
/// </summary>
public static class PPEPairMeshSeparator
{
    public const string Folder = "Assets/1.JSR/Generated/PPE";
    public const string InnerGloveLeft = Folder + "/InnerGlove_Left.obj";
    public const string InnerGloveRight = Folder + "/InnerGlove_Right.obj";
    public const string ChemicalBootLeft = Folder + "/ChemicalBoot_Left.obj";
    public const string ChemicalBootRight = Folder + "/ChemicalBoot_Right.obj";

    public static Mesh LoadMesh(string assetPath)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (var i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Mesh mesh)
                return mesh;
        }

        return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
    }

    public static bool TryLoadPair(string leftPath, string rightPath, out Mesh left, out Mesh right, out string error)
    {
        left = LoadMesh(leftPath);
        right = LoadMesh(rightPath);
        if (left == null || right == null)
        {
            error =
                "Generated left/right mesh assets are missing or not imported yet: " +
                leftPath + " / " + rightPath;
            return false;
        }

        if (left == right)
        {
            error = "Left and right mesh assets resolved to the same Mesh.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryExtractConnectedPair(Mesh source, bool bootsShelfYawMinus90, out Mesh left, out Mesh right, out string error)
    {
        left = null;
        right = null;
        error = null;
        if (source == null)
        {
            error = "Source mesh is null.";
            return false;
        }

        Vector3[] vertices;
        int[] triangles;
        try
        {
            vertices = source.vertices;
            triangles = source.triangles;
        }
        catch (System.Exception ex)
        {
            error = "Source mesh is not readable: " + ex.Message;
            return false;
        }

        if (vertices == null || triangles == null || vertices.Length < 12 || triangles.Length < 9)
        {
            error = "Source mesh does not contain enough geometry to split.";
            return false;
        }

        var islands = BuildIslands(vertices.Length, triangles);
        if (islands.Count < 2)
        {
            error = "Source asset requires manual Blender/asset separation. Connected-component count=" + islands.Count;
            return false;
        }

        GroupIntoTwo(vertices, islands, out var pieceA, out var pieceB, out var centroidA, out var centroidB);
        var aIsLeft = bootsShelfYawMinus90
            ? -centroidA.z <= -centroidB.z
            : centroidA.x <= centroidB.x;
        var leftVerts = aIsLeft ? pieceA : pieceB;
        var rightVerts = aIsLeft ? pieceB : pieceA;
        left = BuildSubmesh(source, vertices, triangles, leftVerts, source.name + "_Left");
        right = BuildSubmesh(source, vertices, triangles, rightVerts, source.name + "_Right");
        if (left == null || right == null)
        {
            error = "Source asset requires manual Blender/asset separation. Submesh build failed.";
            return false;
        }

        return true;
    }

    static List<List<int>> BuildIslands(int vertCount, int[] triangles)
    {
        var parent = new int[vertCount];
        for (var i = 0; i < vertCount; i++)
            parent[i] = i;

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
                parent[rb] = ra;
        }

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            Union(triangles[i], triangles[i + 1]);
            Union(triangles[i], triangles[i + 2]);
        }

        var map = new Dictionary<int, List<int>>();
        for (var i = 0; i < vertCount; i++)
        {
            var root = Find(i);
            if (!map.TryGetValue(root, out var list))
            {
                list = new List<int>();
                map[root] = list;
            }

            list.Add(i);
        }

        var islands = new List<List<int>>(map.Values);
        islands.Sort((a, b) => b.Count.CompareTo(a.Count));
        return islands;
    }

    static void GroupIntoTwo(
        Vector3[] vertices,
        List<List<int>> islands,
        out HashSet<int> pieceA,
        out HashSet<int> pieceB,
        out Vector3 centroidA,
        out Vector3 centroidB)
    {
        centroidA = Centroid(vertices, islands[0]);
        centroidB = Centroid(vertices, islands[1]);
        var delta = centroidA - centroidB;
        var axis = 0;
        if (Mathf.Abs(delta.y) >= Mathf.Abs(delta.x) && Mathf.Abs(delta.y) >= Mathf.Abs(delta.z))
            axis = 1;
        else if (Mathf.Abs(delta.z) >= Mathf.Abs(delta.x))
            axis = 2;

        pieceA = new HashSet<int>(islands[0]);
        pieceB = new HashSet<int>(islands[1]);
        for (var i = 2; i < islands.Count; i++)
        {
            var c = Centroid(vertices, islands[i]);
            var da = Mathf.Abs(GetAxis(c, axis) - GetAxis(centroidA, axis));
            var db = Mathf.Abs(GetAxis(c, axis) - GetAxis(centroidB, axis));
            if (da <= db)
            {
                for (var v = 0; v < islands[i].Count; v++)
                    pieceA.Add(islands[i][v]);
            }
            else
            {
                for (var v = 0; v < islands[i].Count; v++)
                    pieceB.Add(islands[i][v]);
            }
        }

        centroidA = Centroid(vertices, pieceA);
        centroidB = Centroid(vertices, pieceB);
    }

    static Mesh BuildSubmesh(Mesh source, Vector3[] vertices, int[] triangles, HashSet<int> keep, string name)
    {
        var map = new int[vertices.Length];
        var newVerts = new List<Vector3>(keep.Count);
        for (var i = 0; i < vertices.Length; i++)
        {
            if (!keep.Contains(i))
            {
                map[i] = -1;
                continue;
            }

            map[i] = newVerts.Count;
            newVerts.Add(vertices[i]);
        }

        var newTris = new List<int>();
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = map[triangles[i]];
            var b = map[triangles[i + 1]];
            var c = map[triangles[i + 2]];
            if (a < 0 || b < 0 || c < 0)
                continue;
            newTris.Add(a);
            newTris.Add(b);
            newTris.Add(c);
        }

        if (newVerts.Count < 8 || newTris.Count < 9)
            return null;

        var mesh = new Mesh { name = name };
        mesh.SetVertices(newVerts);
        mesh.SetTriangles(newTris, 0);
        var normals = source.normals;
        if (normals != null && normals.Length == vertices.Length)
        {
            var newNormals = new List<Vector3>(newVerts.Count);
            for (var i = 0; i < vertices.Length; i++)
            {
                if (map[i] >= 0)
                    newNormals.Add(normals[i]);
            }

            mesh.SetNormals(newNormals);
        }
        else
        {
            mesh.RecalculateNormals();
        }

        var uv = source.uv;
        if (uv != null && uv.Length == vertices.Length)
        {
            var newUv = new List<Vector2>(newVerts.Count);
            for (var i = 0; i < vertices.Length; i++)
            {
                if (map[i] >= 0)
                    newUv.Add(uv[i]);
            }

            mesh.SetUVs(0, newUv);
        }

        var tangents = source.tangents;
        if (tangents != null && tangents.Length == vertices.Length)
        {
            var newTangents = new List<Vector4>(newVerts.Count);
            for (var i = 0; i < vertices.Length; i++)
            {
                if (map[i] >= 0)
                    newTangents.Add(tangents[i]);
            }

            mesh.SetTangents(newTangents);
        }

        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector3 Centroid(Vector3[] vertices, IReadOnlyList<int> ids)
    {
        var sum = Vector3.zero;
        for (var i = 0; i < ids.Count; i++)
            sum += vertices[ids[i]];
        return sum / ids.Count;
    }

    static Vector3 Centroid(Vector3[] vertices, HashSet<int> ids)
    {
        var sum = Vector3.zero;
        foreach (var id in ids)
            sum += vertices[id];
        return sum / ids.Count;
    }

    static float GetAxis(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 1: return value.y;
            case 2: return value.z;
            default: return value.x;
        }
    }
}
