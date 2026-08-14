using System.Collections.Generic;
using UnityEngine;
using EasyMeshDeformation;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshCombiner : MonoBehaviour
{
	private const int Mesh16BitBufferVertexLimit = 65535;

	[SerializeField, Tooltip("Include the container's own sharedMesh when combining (used to bake the deformed mesh before fracturing).")]
	private bool includeSelfMesh = true;

	/// <summary>合并时是否包含容器自身当前的 sharedMesh（用于固定变形网格后再破碎）。</summary>
	public bool IncludeSelfMesh { get { return includeSelfMesh; } set { includeSelfMesh = value; } }

	/// <summary>最近一次合并生成的网格（固定结果）；未合并过为 null。</summary>
	public Mesh LastCombinedMesh { get; private set; }

	/// <summary>待合并的一个网格信息：Mesh + 网格本地到世界的矩阵 + 渲染器引用。</summary>
	private class ChildMesh
	{
		public Mesh Mesh;
		public Matrix4x4 Transform;
		public Renderer Renderer;
		public bool IsSelf;     // 容器自身（includeSelfMesh 时加入）
		public bool IsReadback; // 从 GPU 回读的变形网格（临时对象，合并后需销毁）
	}

	/// <summary>
	/// 把容器自身（可选）+ 子物体的网格合并为一个网格写回容器 sharedMesh。
	/// 用于破碎前固定变形网格：合并结果与容器本地空间对齐，之后可交给碎裂器使用。
	/// </summary>
	public void CombineMeshes(bool showCreatedMeshInfo)
	{
		#region 临时解除父级并复位容器 Transform（保证合并结果对齐容器本地空间）
		Vector3 oldScaleAsChild = transform.localScale;
		int positionInParentHierarchy = transform.GetSiblingIndex();
		Transform parent = transform.parent;
		transform.parent = null;

		Quaternion oldRotation = transform.rotation;
		Vector3 oldPosition = transform.position;
		Vector3 oldScale = transform.localScale;
		transform.rotation = Quaternion.identity;
		transform.position = Vector3.zero;
		transform.localScale = Vector3.one;
		#endregion

		CombineMeshesWithSingleMaterial(showCreatedMeshInfo);

		#region 恢复容器 Transform
		transform.rotation = oldRotation;
		transform.position = oldPosition;
		transform.localScale = oldScale;
		transform.parent = parent;
		transform.SetSiblingIndex(positionInParentHierarchy);
		transform.localScale = oldScaleAsChild;
		#endregion
	}

	/// <summary>
	/// 收集待合并网格：includeSelfMesh 时先加入容器自身（单位矩阵，结果写回容器本地空间）；
	/// 再收集子物体的 MeshFilter 与 SkinnedMeshRenderer（后者 BakeMesh 烘焙当前骨骼姿势，
	/// 合并矩阵取根骨骼的 localToWorldMatrix）。
	/// </summary>
	private List<ChildMesh> GetChildMeshes()
	{
		List<ChildMesh> children = new List<ChildMesh>();
		MeshFilter selfFilter = GetComponent<MeshFilter>();

		// 容器自身：固定变形网格用
		if(includeSelfMesh && (selfFilter != null) && (selfFilter.sharedMesh != null))
		{
			Mesh sourceMesh = selfFilter.sharedMesh;
			bool isReadback = false;

			// 若容器上有 GPU 变形器：sharedMesh 的 CPU 数据是未变形的原始网格，
			// 变形结果在 GPU 顶点缓冲里（compute shader 每帧写入），必须先从 GPU 回读，
			// 否则合并出来会丢失凹痕
			MeshDeformerBase deformer = GetComponent<MeshDeformerBase>();
			if(deformer != null)
			{
				Mesh deformed = deformer.GetDeformedMesh();
				if(deformed != null)
				{
					sourceMesh = deformed;
					isReadback = true;
				}
			}

			children.Add(new ChildMesh
			{
				Mesh = sourceMesh,
				Transform = Matrix4x4.identity,
				Renderer = GetComponent<MeshRenderer>(),
				IsSelf = true,
				IsReadback = isReadback,
			});
		}

		// 子物体普通网格（跳过容器自身）
		MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(false);
		for(int i = 0; i < filters.Length; i++)
		{
			MeshFilter filter = filters[i];
			if(filter == selfFilter) continue;

			children.Add(new ChildMesh
			{
				Mesh = filter.sharedMesh,
				Transform = filter.transform.localToWorldMatrix,
				Renderer = filter.GetComponent<MeshRenderer>(),
			});
		}

		// 子物体蒙皮网格：烘焙当前骨骼姿势
		SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>(false);
		for(int i = 0; i < skinned.Length; i++)
		{
			SkinnedMeshRenderer renderer = skinned[i];

			Mesh baked = new Mesh();
			renderer.BakeMesh(baked);
			baked.name = (renderer.sharedMesh != null) ? renderer.sharedMesh.name : "SkinnedMesh";

			Transform root = (renderer.rootBone != null) ? renderer.rootBone : renderer.transform;

			children.Add(new ChildMesh
			{
				Mesh = baked,
				Transform = root.localToWorldMatrix,
				Renderer = renderer,
			});
		}

		return children;
	}

	private void CombineMeshesWithSingleMaterial(bool showCreatedMeshInfo)
	{
		List<ChildMesh> children = GetChildMeshes();
		if(children.Count == 0) return;

		MeshFilter selfFilter = GetComponent<MeshFilter>();

		CombineInstance[] combineInstances = new CombineInstance[children.Count];
		long verticesLength = 0;
		for(int i = 0; i < children.Count; i++)
		{
			combineInstances[i].subMeshIndex = 0;
			combineInstances[i].mesh = children[i].Mesh;
			combineInstances[i].transform = children[i].Transform;
			verticesLength += children[i].Mesh.vertices.Length;
		}

		// 取第一个来源的材质作为合并结果材质
		MeshRenderer selfRenderer = GetComponent<MeshRenderer>();
		if((children[0].Renderer != null) && (children[0].Renderer.sharedMaterials.Length > 0))
		{
			selfRenderer.sharedMaterials = new Material[1] { children[0].Renderer.sharedMaterials[0] };
		}
		else
		{
			selfRenderer.sharedMaterials = new Material[0];
		}

		Mesh combinedMesh = new Mesh();
		combinedMesh.name = name;

		if(verticesLength > Mesh16BitBufferVertexLimit)
		{
			combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
		}

		combinedMesh.CombineMeshes(combineInstances);
		selfFilter.sharedMesh = combinedMesh;
		LastCombinedMesh = combinedMesh;

		ReleaseBakedMeshes(children);

		if(showCreatedMeshInfo)
		{
			Debug.Log($"<color=#00cc00><b>Mesh \"{name}\" was created from {combineInstances.Length} meshes and has {verticesLength} vertices.</b></color>");
		}
	}

	/// <summary>释放合并产生的临时网格（GPU 回读的变形网格 / Skinned 烘焙网格；合并时 CombineMeshes 已拷贝顶点数据）。</summary>
	private void ReleaseBakedMeshes(List<ChildMesh> children)
	{
		Mesh selfMesh = GetComponent<MeshFilter>()?.sharedMesh;
		for(int i = 0; i < children.Count; i++)
		{
			Mesh mesh = children[i].Mesh;
			if((mesh == null) || (mesh == selfMesh)) continue; // 容器自身网格保留
			if(children[i].IsSelf || (children[i].Renderer is SkinnedMeshRenderer))
			{
				DestroyImmediate(mesh);
			}
		}
	}
}
