using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 修改器（modifier）可用的更新模式：决定变形在何时被重新计算。
	/// </summary>
	public enum UpdateMode
	{
		/// 手动模式：不会自动更新，需要手动调用 MeshDeformerBase.RequestUpdate() 触发变形。
		[InspectorName("手动")]
		Manual,

		/// 渲染器可见时才更新（利用 OnBecameVisible/OnBecameInvisible 等回调优化性能）。
		[InspectorName("可见时")]
		WhenVisible,

		/// 每帧都更新。
		[InspectorName("始终")]
		Always
	}
}
