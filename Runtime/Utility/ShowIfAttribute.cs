using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 编辑器（Inspector）条件显示特性：当某字段的值满足条件时显示/隐藏另一字段。仅包内使用，无对应 PropertyDrawer 时不生效。
	/// </summary>
	public class ShowIfAttribute : PropertyAttribute
	{
		/// 需要检查的字段名（通常为同一序列化对象上的另一个字段）。
		public string Name;

		/// 期望的值列表。若被检查字段的值不在该列表中，则隐藏目标字段。
		public object[] Values = new object[0];

		/// 构造函数。
		public ShowIfAttribute(string name, params object[] values)
		{
			Name = name;
			Values = values;
		}
	}
}
