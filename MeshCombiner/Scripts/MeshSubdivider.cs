using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 网格分割器：把 Cube 等网格的每个面细分加密（中点细分，外形不变），
/// 供网格变形和切割使用。挂上组件后立即在编辑器里细分一次。
/// 实现：每个三角形独立插入三条边的中点，拆成 4 个小三角形；
/// 不合并顶点、不插值法线——写回后由 RecalculateNormals 统一重算法线，
/// 保证每个小三角形都有正确的面法线，光照正常。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
public class MeshSubdivider : MonoBehaviour
{
    [Tooltip("细分次数，每细分一次三角形数量变为原来的 4 倍")]
    [Range(1, 5)]
    public int SubdivisionLevel = 2;

    /// <summary>细分后网格名字的后缀，用于防止重复细分</summary>
    private const string SubdividedSuffix = "_Subdivided";

    /// <summary>首次细分前的原始网格，级别变化时从它重新细分</summary>
    [SerializeField, HideInInspector] private Mesh _originalMesh;

    /// <summary>已应用的细分级别，用于检测用户是否改动了数值</summary>
    [SerializeField, HideInInspector] private int _appliedLevel = -1;

    private void Awake()
    {
        // 编辑器下细分（挂上组件即执行）；运行时不再细分，避免进入 Play 时重复细分。
        // Awake/OnValidate 阶段禁止修改 sharedMesh（set_sharedMesh 会触发
        // OnMeshFilterChanged 的 SendMessage），故延迟到安全时机执行。
        if (!Application.isPlaying)
        {
            ScheduleSubdivide();
        }
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            ScheduleSubdivide();
        }
    }

    /// <summary>
    /// 把细分调度到编辑器安全阶段（OnValidate/Awake 之后）执行，避免 SendMessage 限制警告。
    /// 重复调用会去重，最终只执行一次。
    /// </summary>
    private void ScheduleSubdivide()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= Subdivide;
        UnityEditor.EditorApplication.delayCall += Subdivide;
#endif
    }

    /// <summary>
    /// 细分当前物体的网格。始终从原始网格重新细分（改级别是"从头细分"，不叠加）。
    /// </summary>
    [ContextMenu("细分网格")]
    public void Subdivide()
    {
        // delayCall 触发时组件可能已被销毁
        if (this == null) return;

        var meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) return;

        Mesh mesh = Application.isPlaying ? meshFilter.mesh : meshFilter.sharedMesh;
        if (mesh == null) return;

        // 用 Contains 判断（Unity 实例网格名字可能带 " Instance" 后缀，EndsWith 会漏判导致重复细分）
        bool alreadySubdivided = mesh.name.Contains(SubdividedSuffix);

        if (!alreadySubdivided)
        {
            _originalMesh = mesh;
            _appliedLevel = -1;
        }

        // 已按当前级别细分过 → 跳过
        if (alreadySubdivided && _appliedLevel == SubdivisionLevel)
        {
            return;
        }

        // 已细分但找不到原始网格（旧版本遗留状态）→ 跳过，绝不叠加
        if (_originalMesh == null)
        {
            return;
        }

        // 保护：预计三角形数过大会卡死编辑器
        long estimated = _originalMesh.triangles.Length / 3;
        for (int i = 0; i < SubdivisionLevel; i++)
        {
            estimated *= 4;
        }
        if (estimated > 1000000)
        {
            return;
        }

        Mesh target = Object.Instantiate(_originalMesh);
        if (Application.isPlaying)
        {
            meshFilter.mesh = target;
        }
        else
        {
            meshFilter.sharedMesh = target;
        }

        SubdivideMesh(target, SubdivisionLevel);
        target.name += SubdividedSuffix;
        _appliedLevel = SubdivisionLevel;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(meshFilter);
        }
#endif
    }

    /// <summary>对网格细分 level 次</summary>
    public static void SubdivideMesh(Mesh mesh, int level)
    {
        for (int i = 0; i < level; i++)
        {
            SubdivideOnce(mesh);
        }
    }

    /// <summary>
    /// 细分一轮：每个三角形插入三条边的中点，拆成 4 个小三角形。
    /// 中点不做合并（每个三角形独立生成），所以顶点法线由 RecalculateNormals
    /// 按各自三角形计算，光照必然正确。
    /// </summary>
    private static void SubdivideOnce(Mesh mesh)
    {
        // 读入原始数据
        List<Vector3> vertices = new List<Vector3>(mesh.vertices);
        List<Vector2> uvs = new List<Vector2>(mesh.uv);
        if (uvs.Count != vertices.Count)
        {
            uvs = new List<Vector2>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++) uvs.Add(Vector2.zero);
        }
        int[] triangles = mesh.triangles;

        // 细分结果
        List<int> newTriangles = new List<int>(triangles.Length * 4);

        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t];
            int b = triangles[t + 1];
            int c = triangles[t + 2];

            // 为这个三角形插入三个中点（每个三角形独立，不合并）
            int ab = AddMidpoint(vertices, uvs, a, b);
            int bc = AddMidpoint(vertices, uvs, b, c);
            int ca = AddMidpoint(vertices, uvs, c, a);

            // 一个三角形拆成四个小三角形
            newTriangles.Add(a); newTriangles.Add(ab); newTriangles.Add(ca);
            newTriangles.Add(ab); newTriangles.Add(b); newTriangles.Add(bc);
            newTriangles.Add(ca); newTriangles.Add(bc); newTriangles.Add(c);
            newTriangles.Add(ab); newTriangles.Add(bc); newTriangles.Add(ca);
        }

        // 写回，统一重算法线
        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = newTriangles.ToArray();
        mesh.RecalculateNormals();
        // 必须补算切线：法线贴图需要 TBN 矩阵，缺切线会导致渲染过曝/黑块
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
    }

    /// <summary>把两端点的中点追加为新的顶点，返回其索引</summary>
    private static int AddMidpoint(List<Vector3> vertices, List<Vector2> uvs, int i1, int i2)
    {
        vertices.Add((vertices[i1] + vertices[i2]) * 0.5f);
        uvs.Add((uvs[i1] + uvs[i2]) * 0.5f);
        return vertices.Count - 1;
    }
}
