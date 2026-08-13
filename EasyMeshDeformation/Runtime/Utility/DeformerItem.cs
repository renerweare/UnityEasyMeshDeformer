using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 一个晶格项：引用一个晶格（DeformerCube），并为其单独配置插值方式、Global（越界处理）与遮罩。一个Mesh组件可挂多个晶格项，依次施加变形。
	/// </summary>
	[Serializable]
	public struct DeformerItem : ISerializationCallbackReceiver
	{
		private const string DeformerCubeTooltip = "要应用的格子。";

		private const string InterpolationTooltip =
			"插值方式：\n" +
			" - 线性（清晰）：线性插值。\n" +
			" - 线性（平滑）：线性插值，带近似平滑。\n" +
			" - 三次方：三次插值，效果优于线性但开销更高。";

		private const string GlobalTooltip =
			"格子外部的变形处理方式：\n" +
			" - 禁用：格子外部的变形会逐渐衰减。\n" +
			" - 启用：变形会延续到格子外部，与外部控制点保持一致。";

		private const string MaskTooltip =
			"遮罩选项。可用于控制Mesh上的变形范围。";

		/// 要应用的晶格（DeformerCube 组件）。
		[Tooltip(DeformerCubeTooltip)]
		public DeformerCube Cube;

		/// 该晶格项使用的插值方式（线性清晰/线性平滑/三次方）。
		[Tooltip(InterpolationTooltip)]
		public InterpolationMethod Interpolation;

		/// Global 开关：true 时变形延续到晶格外部（越界偏移沿用，DEFORM_ZERO_OUTSIDE 关闭）；false 时晶格外衰减（关键字开启，越界偏移视为 0）。
		[Tooltip(GlobalTooltip)]
		public bool Global;

		/// 该晶格项的遮罩设置（控制哪些顶点变形、每个顶点变形的程度）。
		[Tooltip(MaskTooltip)]
		public DeformerMask Mask;

		#region Serialization

		/// 已废弃的「高质量」旧字段（早期版本用 bool 表示是否使用三次插值），仅用于序列化兼容，不参与运行逻辑。
		[SerializeField, HideInInspector, FormerlySerializedAs("HighQuality")]
		private int d_HighQuality; // 已废弃的高质量字段

		/// 序列化前把废弃字段重置为 -1（标记「无旧数据」，避免误触发升级逻辑）。
		void ISerializationCallbackReceiver.OnBeforeSerialize()
		{
			d_HighQuality = -1;
		}

		/// 数据升级：旧 HighQuality 字段（0/1）迁移到 Interpolation（1→Cubic，0→LinearSmooth）。
		void ISerializationCallbackReceiver.OnAfterDeserialize()
		{
			if ((d_HighQuality >= 0) && (d_HighQuality <= 1))
			{
				Interpolation = (d_HighQuality == 1) 
					? InterpolationMethod.Cubic 
					: InterpolationMethod.LinearSmooth;
			}
		}

		#endregion
	}
}
