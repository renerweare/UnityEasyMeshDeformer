// ============================================================================
// DeformSettingsProvider.cs
// 概述：在"Preferences"窗口中注册"Preferences/晶格"选项卡，编辑 DeformSettings
// 的全部显示偏好（线条、辉光、控制点、衰减），并提供"恢复默认设置"按钮；
// 窗口失活（OnDeactivate）时调用 Save() 持久化。
// ============================================================================
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>在偏好设置窗口中创建"晶格"选项卡的 SettingsProvider（用 SerializedObject 逐项绘制，支持 Undo）。</summary>
	internal class DeformSettingsProvider : SettingsProvider
	{
		/// <summary>偏好设置页面的路径（显示为"Preferences/晶格"）。</summary>
		internal const string PreferencesPath = "Preferences/晶格";

		/// <summary>偏好设置窗口的搜索关键词（用于用户搜索定位该页面）。</summary>
		private static readonly string[] Keywords = new string[] { "晶格", "DeformerCube" };

		/// <summary>包装 DeformSettings 单例的 SerializedObject 缓存。</summary>
		private static SerializedObject _serializedSettings;

		/// <summary>懒加载的 SerializedObject：首次访问时绑定 DeformSettings 单例。</summary>
		private static SerializedObject SerializedSettings => _serializedSettings ??= new(DeformSettings.Instance);

		/// <summary>构造设置提供者：传入页面路径（用户级范围）与搜索关键词。</summary>
		public DeformSettingsProvider() : base(PreferencesPath, SettingsScope.User, Keywords) { }

		/// <summary>供 Unity 自动发现本设置提供者（[SettingsProvider] 静态工厂）。</summary>
		[SettingsProvider]
		internal static SettingsProvider CreateProvider()
		{
			return new DeformSettingsProvider();
		}

		/// <summary>绘制偏好设置界面：按分组逐项绘制各设置字段，底部提供"恢复默认设置"按钮。</summary>
		/// <param name="searchContext">偏好设置窗口的搜索上下文（本实现未使用）。</param>
		public override void OnGUI(string searchContext)
		{
			SerializedSettings.Update();

			// 左侧留白，贴近 Unity 偏好窗口的排版习惯
			using EditorGUILayout.HorizontalScope horizontal = new();
			GUILayout.Space(10f);
			using EditorGUILayout.VerticalScope vertical = new();

			// —— 线条设置分组 ——
			EditorGUILayout.Space(10f);
			EditorGUILayout.LabelField("线条", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._lineColor)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._lineThickness)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._lineInFrontOpacity)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._lineBehindOpacity)));

			// —— 辉光设置分组 ——
			EditorGUILayout.Space(10f);
			EditorGUILayout.LabelField("辉光", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._glowColorGradient)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._glowOpacity)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._glowThicknessSquish)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._glowThicknessStretch)));

			// —— 控制点设置分组 ——
			EditorGUILayout.Space(10f);
			EditorGUILayout.LabelField("控制点", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._handleColor)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._handleColorSelected)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._handleSize)));

			// —— 选择衰减设置分组 ——
			EditorGUILayout.Space(10f);
			EditorGUILayout.LabelField("衰减", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._selectionFalloffEnabled)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._selectionFalloffRadius)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._selectionFalloffCurve)));
			EditorGUILayout.PropertyField(SerializedSettings.FindProperty(nameof(DeformSettings._selectionFalloffColor)));

			SerializedSettings.ApplyModifiedProperties();

			// —— 底部"恢复默认设置"按钮：记录 Undo 后重置全部设置 ——
			EditorGUILayout.Space(10f);
			if (GUILayout.Button("恢复默认设置"))
			{
				Undo.RecordObject(DeformSettings.Instance, "重置晶格设置");
				DeformSettings.Instance.Reset();
			}
		}

		/// <summary>页面失活回调：保存设置到 EditorPrefs，保证修改持久化。</summary>
		public override void OnDeactivate()
		{
			DeformSettings.Save();
		}
	}
}
