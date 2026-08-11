// ============================================================================
// ShowIfDrawer.cs
// 概述：[ShowIf("字段名", 值...)] 特性的条件显示绘制器：按同属对象上的
// 枚举/布尔字段决定当前字段是否显示。未提供目标值时按"非零/非 false"判断；
// 提供了目标值按"取值在集合内"判断；目标字段找不到时默认显示。
// ============================================================================
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>[ShowIf] 特性的"条件显示"属性绘制器，仅支持枚举与布尔两种属性类型。</summary>
	[CustomPropertyDrawer(typeof(ShowIfAttribute))] 
	internal class ShowIfDrawer : PropertyDrawer
	{
		/// <summary>关联的 ShowIfAttribute（记录条件字段名与允许值列表）。</summary>
		private ShowIfAttribute Attribute => attribute as ShowIfAttribute;

		/// <summary>判定当前字段是否显示：目标值为空集合按"非零/非 false"判断，否则按"取值是否在集合内"判断。</summary>
		/// <param name="property">当前绘制的序列化属性。</param>
		/// <returns>true 显示该字段，false 隐藏。</returns>
		private bool ShouldDraw(SerializedProperty property)
		{
			object[] values = Attribute.Values;

			// 在兄弟路径中查找条件字段；不存在时保守显示（不隐藏字段）
			SerializedProperty target = FindSiblingProperty(property, $"{Attribute.Name}");

			if (target == null)
				return true;

			// 按条件字段类型分派判断
			switch (target.propertyType)
			{
				case SerializedPropertyType.Enum:
				{
					// 枚举用索引值比较；无目标值时索引 > 0（非第一个枚举）才显示
					int value = target.enumValueIndex;
					return (values.Length > 0)
						? values.Cast<int>().Contains(value)
						: value > 0;
				}
				case SerializedPropertyType.Boolean:
				{
					// 布尔直接取值比较；无目标值为 true 才显示
					bool value = target.boolValue;
					return (values.Length > 0)
						? values.Cast<bool>().Contains(value)
						: value;
				}
			}

			// 其他属性类型一律显示
			return true;
		}

		/// <summary>返回字段占用高度：隐藏时返回负的行间距，使字段在布局中"零占用"。</summary>
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (ShouldDraw(property)) return base.GetPropertyHeight(property, label);
			else return -1f * EditorGUIUtility.standardVerticalSpacing;
		}

		/// <summary>仅在 ShouldDraw 判定为显示时绘制字段。</summary>
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			if (ShouldDraw(property)) EditorGUI.PropertyField(position, property, label);
		}

		/// <summary>查找同属一个对象的兄弟属性：路径按 '.' 拆分后替换末段为目标字段名，兼容嵌套序列化对象。</summary>
		/// <param name="property">当前属性。</param>
		/// <param name="sibling">目标兄弟字段名。</param>
		/// <returns>找到的兄弟属性；不存在时返回 null。</returns>
		private static SerializedProperty FindSiblingProperty(SerializedProperty property, string sibling)
		{
			// 拆分路径，替换末段为兄弟字段名（如 "items.Array.data[0].global" → "items.Array.data[0].x"）
			string[] path = property.propertyPath.Split('.');
			path[^1] = sibling;

			// 重新拼接路径（保留数组下标等中间段）
			string parent = path[0];
			for (int i = 1; i < path.Length; i++)
			{
				parent += $".{path[i]}";
			}

			return property.serializedObject.FindProperty(parent);
		}
	}
}
