// ============================================================================
// DeformerHandle.cs —— 变形控制点（Handle）组件
// 作用：DeformerCube（FFD 晶格）中单个控制点的运行时载体，仅包装一个 Vector3
// offset（相对基准位置的位移），由 DeformerCube.Setup() 自动创建为晶格子物体。
// ============================================================================
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>变形控制点（Handle）：FFD 晶格中单个控制点的运行时载体，仅包装 Vector3 偏移量。</summary>
	[ExecuteAlways, AddComponentMenu("")]
	public class DeformerHandle : MonoBehaviour
	{
		/// <summary>控制点相对基准位置的偏移量（晶格本地空间）；由编辑器工具/公开接口写入，DeformerCube 每帧汇总。</summary>
		[SerializeField] internal Vector3 offset;

#if UNITY_EDITOR
		/// <summary>编辑器下把控制点子物体从 Hierarchy 面板隐藏（保留 Scene 视图可见可编辑）。</summary>
		private void OnEnable()
		{
			gameObject.hideFlags |= HideFlags.HideInHierarchy;
		}
#endif
	}
}
