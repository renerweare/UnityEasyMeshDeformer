// ============================================================================
// 文件：TestSceneTools.cs
// 作用：临时测试工具（菜单：EasyMeshDeformation/重建测试晶格并绑定）。
//       一键为场景中所有 MeshDeformer 重建干净的 DeformerCube，
//       自动 Fit 包裹网格并绑定到晶格列表，用于清理
//       "新旧系统混用"后留下的测试环境。
// ============================================================================
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>临时测试工具：为场景中所有 MeshDeformer 一键重建 DeformerCube 并绑定到晶格列表。</summary>
	public static class TestSceneTools
	{
		/// <summary>菜单入口：为每个启用的 MeshDeformer 重建 DeformerCube 并写入晶格列表（Cubic 插值、Global 关闭）。</summary>
		[MenuItem("EasyMeshDeformation/重建测试晶格并绑定")]
		public static void RebuildAll()
		{
			MeshDeformer[] deformers = Object.FindObjectsByType<MeshDeformer>(FindObjectsSortMode.None);

			foreach (MeshDeformer deformer in deformers)
			{
				if (!deformer.isActiveAndEnabled) continue;

				// 以变形器自身作为适配基准
				Transform target = deformer.transform;

				// 创建独立的晶格对象（根对象，不跟随网格变换）
				GameObject go = new("DeformerCube");
				go.transform.position = target.position;

				DeformerCube cube = go.AddComponent<DeformerCube>();
				cube.Setup(cube.Resolution);

				// 自动缩放/定位包裹所有渲染器
				cube.FitToTransform(target, true, false);

				// 通过 SerializedObject 直接写入私有字段 _items，
				// 保证 Inspector 中的列表与场景数据保持一致
				SerializedObject so = new(deformer);
				SerializedProperty items = so.FindProperty("_items");
				items.arraySize = 1;
				SerializedProperty element = items.GetArrayElementAtIndex(0);
				element.FindPropertyRelative("Cube").objectReferenceValue = cube;
				element.FindPropertyRelative("Interpolation").enumValueIndex = (int)InterpolationMethod.Cubic;
				element.FindPropertyRelative("Global").boolValue = false;
				so.ApplyModifiedProperties();

				// 注册 Undo，便于撤销本次重建操作
				Undo.RegisterCreatedObjectUndo(go, "重建测试晶格");
			}

			Debug.Log($"已重建并绑定 {deformers.Length} 个测试晶格。");
		}
	}
}
