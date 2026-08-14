// ============================================================================
// DeformSettings.cs
// 概述：晶格在 Scene 视图中的显示偏好设置（线条/辉光/控制点/衰减）。
// 通过 EditorJsonUtility 序列化后用 EditorPrefs 保存（键名 "DeformerCube/Settings"），
// 编辑器重启后自动恢复；Reset() 提供全部默认值。
// ============================================================================
using System;
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>存储晶格显示偏好的单例设置类（通过 EditorPrefs 持久化，由偏好设置窗口编辑）。</summary>
	[Serializable]
	internal class DeformSettings : ScriptableObject
	{
		/// <summary>EditorPrefs 中的存储键名（不可改动，用于 Save/Load 读写）。</summary>
		internal const string SettingsKey = "DeformerCube/Settings";

		/// <summary>单例实例（懒加载，首次访问时从 EditorPrefs 反序列化）。</summary>
		private static DeformSettings _instance;

		// ---------- 线条设置 ----------
		/// <summary>线框基础颜色。</summary>
		[SerializeField] internal Color _lineColor;
		/// <summary>线框基础粗细（像素）。</summary>
		[SerializeField] internal float _lineThickness;
		/// <summary>可见（前方）线框的不透明度。</summary>
		[SerializeField] internal float _lineInFrontOpacity;
		/// <summary>被遮挡（后方）线框的不透明度。</summary>
		[SerializeField] internal float _lineBehindOpacity;

		// ---------- 控制点设置 ----------
		/// <summary>控制点默认颜色。</summary>
		[SerializeField] internal Color _handleColor;
		/// <summary>选中控制点的高亮颜色。</summary>
		[SerializeField] internal Color _handleColorSelected;
		/// <summary>控制点的显示大小（屏幕像素比例）。</summary>
		[SerializeField] internal float _handleSize;

		// ---------- 辉光设置 ----------
		/// <summary>辉光颜色渐变（按挤压/拉伸程度采样：-1 蓝 → 0 黑 → +1 红）。</summary>
		[SerializeField] internal Gradient _glowColorGradient;
		/// <summary>辉光整体透明度。</summary>
		[SerializeField] internal float _glowOpacity;
		/// <summary>挤压（压缩）时的辉光厚度。</summary>
		[SerializeField] internal float _glowThicknessSquish;
		/// <summary>拉伸时的辉光厚度。</summary>
		[SerializeField] internal float _glowThicknessStretch;

		// ---------- 选择衰减设置 ----------
		/// <summary>是否启用选择衰减（拖动选中点时带动周围控制点）。</summary>
		[SerializeField] internal bool _selectionFalloffEnabled;
		/// <summary>衰减半径（影响周围多少个格子距离的控制点）。</summary>
		[SerializeField] internal float _selectionFalloffRadius;
		/// <summary>衰减曲线（权重随距离变化的曲线）。</summary>
		[SerializeField] internal AnimationCurve _selectionFalloffCurve;
		/// <summary>衰减区域的可视化颜色。</summary>
		[SerializeField] internal Color _selectionFalloffColor;

		/// <summary>是否使用"选中相对 Gizmo"（手柄贴合变形表面朝向）。</summary>
		[SerializeField] internal bool _handleRelativeGizmos;

		/// <summary>单例实例：首次访问时从 EditorPrefs 加载，之后复用。</summary>
		public static DeformSettings Instance
		{
			get
			{
				if (_instance == null) Load();
				return _instance;
			}
		}

		// ---------- 线条设置访问器 ----------
		/// <summary>线框基础颜色。</summary>
		public static Color LineColor => Instance._lineColor;
		/// <summary>线框基础粗细。</summary>
		public static float LineThickness => Instance._lineThickness;
		/// <summary>可见（前方）线框不透明度。</summary>
		public static float LineInFrontOpacity => Instance._lineInFrontOpacity;
		/// <summary>被遮挡（后方）线框不透明度。</summary>
		public static float LineBehindOpacity => Instance._lineBehindOpacity;

		// ---------- 辉光设置访问器 ----------
		/// <summary>辉光颜色渐变。</summary>
		public static Gradient GlowColorGradient => Instance._glowColorGradient;
		/// <summary>辉光整体透明度。</summary>
		public static float GlowOpacity => Instance._glowOpacity;
		/// <summary>挤压时的辉光厚度。</summary>
		public static float GlowThicknessSquish => Instance._glowThicknessSquish;
		/// <summary>拉伸时的辉光厚度。</summary>
		public static float GlowThicknessStretch => Instance._glowThicknessStretch;

		// ---------- 控制点设置访问器 ----------
		/// <summary>控制点默认颜色。</summary>
		public static Color HandleColor => Instance._handleColor;
		/// <summary>选中控制点高亮颜色。</summary>
		public static Color HandleColorSelected => Instance._handleColorSelected;
		/// <summary>控制点显示大小。</summary>
		public static float HandleSize => Instance._handleSize;

		// ---------- 选择衰减设置访问器 ----------
		/// <summary>是否启用选择衰减（可读写）。</summary>
		public static bool SelectionFalloffEnabled { get => Instance._selectionFalloffEnabled; set => Instance._selectionFalloffEnabled = value; }
		/// <summary>衰减半径（可读写，写入时钳制在 [0.33, 10] 范围内）。</summary>
		public static float SelectionFalloffRadius { get => Instance._selectionFalloffRadius; set => Instance._selectionFalloffRadius = Mathf.Clamp(value, 0.33f, 10f); }
		/// <summary>衰减曲线。</summary>
		public static AnimationCurve SelectionFalloffCurve => Instance._selectionFalloffCurve;
		/// <summary>衰减区域可视化颜色。</summary>
		public static Color SelectionFalloffColor => Instance._selectionFalloffColor;

		/// <summary>是否使用"选中相对 Gizmo"（可读写）。</summary>
		public static bool SelectionRelativeGizmos { get => Instance._handleRelativeGizmos; set => Instance._handleRelativeGizmos = value; }

		/// <summary>把当前设置序列化为 JSON 后写入 EditorPrefs（偏好窗口关闭或退出编辑器时调用）。</summary>
		public static void Save()
		{
			string serializedSettings = EditorJsonUtility.ToJson(Instance, false);
			EditorPrefs.SetString(SettingsKey, serializedSettings);
		}

		/// <summary>从 EditorPrefs 加载设置：先应用默认值，再用保存的 JSON 覆盖。</summary>
		private static void Load()
		{
			if (_instance == null)
			{
				_instance = CreateInstance<DeformSettings>();
			}

			_instance.name = "晶格设置";
			// 先恢复默认值，保证字段始终完整
			_instance.Reset();

			string serializedSettings = EditorPrefs.GetString(SettingsKey, string.Empty);
			if (!string.IsNullOrEmpty(serializedSettings))
			{
				// 用保存的 JSON 覆盖默认值
				EditorJsonUtility.FromJsonOverwrite(serializedSettings, _instance);
			}

			_instance.hideFlags = HideFlags.DontSave;
		}

		/// <summary>打开"偏好设置"窗口并定位到晶格设置页（Preferences/晶格）。</summary>
		public static void OpenPreferences()
		{
			SettingsService.OpenUserPreferences(DeformSettingsProvider.PreferencesPath);
		}

		/// <summary>将所有设置恢复为默认值（默认颜色与数值为固定常量，如需持久化请再调用 Save()）。</summary>
		public void Reset()
		{
			// 线条默认值：黑色细线，前方更不透明
			_lineColor = Color.black;
			_lineThickness = 2f;
			_lineInFrontOpacity = 2f;
			_lineBehindOpacity = 0.5f;

			// 辉光默认渐变：挤压（0）→ 蓝色系，中性（0.5）→ 黑色，拉伸（1）→ 红色系
			_glowColorGradient = new Gradient()
			{
				colorKeys = new GradientColorKey[]
				{
					new(new Color32(  0,  69, 255, 255), 0.00f),
					new(new Color32( 69, 174, 212, 255), 0.25f),
					new(new Color32(  0,   0,   0, 255), 0.50f),
					new(new Color32(233, 157,  76, 255), 0.75f),
					new(new Color32(255,   0,   0, 255), 1.00f),
				},
				alphaKeys = new GradientAlphaKey[]
				{
					new(1.00f, 0.00f),
					new(1.00f, 0.25f),
					new(0.25f, 0.50f),
					new(1.00f, 0.75f),
					new(1.00f, 1.00f)
				}
			};
			_glowOpacity = 0.5f;
			_glowThicknessSquish = 6f;
			_glowThicknessStretch = 3f;

			// 控制点默认值：黑色普通点、橙色高亮点、尺寸 3.5
			_handleColor = new Color(0f, 0f, 0f, 1f);
			_handleColorSelected = new Color(1f, 0.6f, 0f, 1f);
			_handleSize = 3.5f;

			// 选择衰减默认值：默认关闭、半径 1、缓入缓出曲线、半透明白色
			_selectionFalloffEnabled = false;
			_selectionFalloffRadius = 1.0f;
			_selectionFalloffCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
			_selectionFalloffColor = new Color(1f, 1f, 1f, 0.5f);

			_handleRelativeGizmos = false;
		}
	}
}
