// ============================================================================
// DeformerBaker.cs
// 概述：烘焙工具：把 MeshDeformer 的实时变形结果固化到目标Mesh上，
// 并通过 BakeLookup 缓存复用相同配置的计算结果。
// 本类属于编辑器程序集，仅在编辑器环境下生效。
// ============================================================================
using UnityEngine;
using UnityEngine.Rendering;

namespace EasyMeshDeformation.Editor
{
	/// <summary>烘焙晶格变形组件的方法集合（通过 BakeLookup 缓存结果，避免重复计算）。</summary>
	internal static class DeformerBaker
	{
		/// <summary>复用的 CommandBuffer：向 GPU 提交变形计算命令（静态复用，避免反复分配）。</summary>
		private static readonly CommandBuffer _cmd = new();

		/// <summary>烘焙结果缓存表：以"目标Mesh + 修改器配置"为键复用变形结果（见 <see cref="BakeLookup"/>）。</summary>
		private static readonly BakeLookup _lookup = new();

		/// <summary>清空所有烘焙缓存（销毁临时Mesh并清空字典）。</summary>
		internal static void Clear()
		{
			_lookup.Clear();
		}

		/// <summary>将指定 MeshDeformer 的变形结果烘焙到其目标Mesh上。</summary>
		/// <param name="modifier">要烘焙的Mesh修改器组件。</param>
		/// <param name="lightmapping">是否为光照贴图烘焙：为 true 时保留修改器组件，以便光照阶段读取变形结果。</param>
		internal static void Bake(MeshDeformer modifier, bool lightmapping = false)
		{
			// 组件未激活或缺少 MeshFilter 时无法烘焙，直接跳过
			if (!modifier.isActiveAndEnabled) return;
			if (!modifier.TryGetComponent(out MeshFilter filter)) return;

			// 优先复用缓存结果，未命中时才执行实际计算
			if (!_lookup.TryGet(modifier, out Mesh deformedMesh))
			{
				_cmd.Clear();
				DeformFeature.ApplyModifier(_cmd, modifier);
				Graphics.ExecuteCommandBuffer(_cmd);

				deformedMesh = modifier.GetDeformedMesh();
				_lookup.Add(modifier, deformedMesh);
			}

			// 非光照贴图烘焙：禁用修改器组件，让静态Mesh接管显示，
			// 并把 MeshCollider 的共享Mesh替换为变形结果，保证物理与渲染一致
			if (!lightmapping)
			{
				modifier.enabled = false;

				if (modifier.TryGetComponent(out MeshCollider collider) &&
					(collider.sharedMesh == modifier.TargetMesh))
				{
					collider.sharedMesh = deformedMesh;
				}
			}

			// 将变形Mesh写入 MeshFilter，完成烘焙
			filter.sharedMesh = deformedMesh;
		}
	}
}
