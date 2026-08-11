// ============================================================================
// 文件：MeshDeformerEditor.cs
// 作用：MeshDeformer 在 Inspector 中的自定义 Editor：「模式」放最前，
//       并按模式显示对应字段（目标网格 + 更新模式 + 晶格列表）。
// ============================================================================
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>MeshDeformer 的自定义 Editor：「模式」放最前，按模式显示对应字段。</summary>
	[CustomEditor(typeof(MeshDeformer))]
	public class MeshDeformerEditor : UnityEditor.Editor
	{
		/// <summary>模式：网格 / 蒙皮网格，决定 Inspector 展示哪些字段。</summary>
		private SerializedProperty _mode;
		/// <summary>目标网格（网格 / 蒙皮网格模式使用）：要被变形的 Mesh / SkinnedMeshRenderer。</summary>
		private SerializedProperty _targetMesh;
		/// <summary>更新模式：控制变形何时重新计算。</summary>
		private SerializedProperty _updateMode;
		/// <summary>晶格项列表：一组 DeformerItem（晶格 + 插值 + 遮罩）。</summary>
		private SerializedProperty _items;

		/// <summary>Editor 激活时缓存序列化属性句柄，供 OnInspectorGUI 复用。</summary>
		private void OnEnable()
		{
			_mode = serializedObject.FindProperty("_mode");
			_targetMesh = serializedObject.FindProperty("_targetMesh");
			_updateMode = serializedObject.FindProperty("_updateMode");
			_items = serializedObject.FindProperty("_items");
		}

		/// <summary>绘制 Inspector：同步序列化对象、展示字段、写回修改。</summary>
		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			// 模式放最前：让用户优先选择变形模式
			EditorGUILayout.PropertyField(_mode);

			// 网格 / 蒙皮网格模式共用的网格相关字段
			EditorGUILayout.PropertyField(_targetMesh);
			EditorGUILayout.PropertyField(_updateMode);
			EditorGUILayout.PropertyField(_items);

			serializedObject.ApplyModifiedProperties();
		}
	}
}
