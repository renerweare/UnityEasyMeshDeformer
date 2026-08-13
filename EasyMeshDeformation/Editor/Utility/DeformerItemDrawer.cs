// ============================================================================
// 文件：DeformerItemDrawer.cs
// 作用：晶格项（DeformerItem）的自定义属性绘制器，简化展示为 4 行字段：
//       晶格引用、插值方式、Global 开关、遮罩乘数；
//       其余 Mask 细节在代码层保留，不在 Inspector 中暴露。
// ============================================================================
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>简化后的晶格项绘制：只显示晶格、插值方式、Global、乘数，Mask 细节不在 Inspector 暴露。</summary>
	[CustomPropertyDrawer(typeof(DeformerItem))]
	internal class DeformerItemDrawer : PropertyDrawer
	{
		/// <summary>每行字段的高度（像素）。</summary>
		private const float LineHeight = 18f;

		/// <summary>行与行之间的垂直间距（像素）。</summary>
		private const float Spacing = 2f;

		/// <summary>返回该属性占用的总高度（4 行字段 + 3 个行间距）。</summary>
		/// <param name="property">被绘制的序列化属性（DeformerItem）。</param>
		/// <param name="label">属性的显示名称。</param>
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return 4 * LineHeight + 3 * Spacing;
		}

		/// <summary>在 Inspector 中按顺序绘制晶格、插值方式、Global、乘数四个字段。</summary>
		/// <param name="position">分配给该属性的绘制区域。</param>
		/// <param name="property">被绘制的序列化属性（DeformerItem）。</param>
		/// <param name="label">属性的显示名称。</param>
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			// 保证 Undo、多选、右键重置等编辑器功能对该属性生效
			EditorGUI.BeginProperty(position, label, property);

			SerializedProperty cube = property.FindPropertyRelative("Cube");
			SerializedProperty interpolation = property.FindPropertyRelative("Interpolation");
			SerializedProperty global = property.FindPropertyRelative("Global");
			SerializedProperty multiplier = property.FindPropertyRelative("Mask.Vertex.Multiplier");

			Rect rect = position;
			rect.height = LineHeight;

			// 第 1 行：晶格引用
			EditorGUI.PropertyField(rect, cube, new GUIContent("DeformerCube", "要应用的晶格（DeformerCube 组件）。"));
			Next();
			// 第 2 行：插值方式
			EditorGUI.PropertyField(rect, interpolation, new GUIContent("Compute Method",
				"插值方式：\n" +
				" - 线性（清晰）：线性插值。\n" +
				" - 线性（平滑）：线性插值，带近似平滑。\n" +
				" - 三次方：三次插值，效果优于线性但开销更高。"));
			Next();
			// 第 3 行：Global 开关
			EditorGUI.PropertyField(rect, global, new GUIContent("Global",
				"晶格外部的变形处理方式：\n" +
				" - 禁用：晶格外部的变形会逐渐衰减。\n" +
				" - 启用：变形会延续到晶格外部，与外部控制点保持一致。"));
			Next();
			// 第 4 行：遮罩乘数（0~1 滑杆）
			EditorGUI.Slider(rect, multiplier, 0f, 1f, new GUIContent("Multiplier", "该晶格的变形强度（0~1，默认 1）。"));

			EditorGUI.EndProperty();

			// 局部函数：把绘制位置下移到下一行（行高 + 间距）
			void Next() => rect.y += LineHeight + Spacing;
		}
	}
}
