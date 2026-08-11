// ============================================================================
// 文件：DeformerHandleEditor.cs
// 作用：点选控制点手柄时记录选中手柄（静态数组 Selected），并把对象选择
//       切换为手柄所属的 DeformerCube，让 DeformerCubeEditor 接管后续逻辑。
// ============================================================================
using System;
using System.Linq;
using UnityEditor;

namespace EasyMeshDeformation.Editor
{
	/// <summary>选中控制点手柄时，同时选中其所属晶格，并把被点选手柄记录到静态字段 Selected。</summary>
	[CustomEditor(typeof(DeformerHandle)), CanEditMultipleObjects]
	public class DeformerHandleEditor : UnityEditor.Editor
	{
		/// <summary>最近一次被点选的 DeformerHandle 数组（DeformerCubeEditor 读取后会清空）。</summary>
		internal static DeformerHandle[] Selected = Array.Empty<DeformerHandle>();

		/// <summary>手柄被选中时：记录到 Selected，并把对象选择切换为手柄所属晶格。</summary>
		private void OnEnable()
		{
			// 记录被选中的手柄，并把选择切换到其所属晶格对象
			Selected = targets.Cast<DeformerHandle>().ToArray();
			Selection.objects = Selected.Select(t => t.transform.parent.gameObject).ToArray();
		}
	}
}
