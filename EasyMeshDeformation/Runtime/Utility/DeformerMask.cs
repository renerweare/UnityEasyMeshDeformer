using System;
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 每个晶格项的遮罩（mask）数据结构：Selection（选择哪些顶点参与变形）与 Vertex（控制每个顶点被变形的程度）。
	/// 当前已简化为「应用于全部」+「常数」遮罩，其余类型（材质/顶点色/UV/贴图）的代码路径保留，便于将来扩展。
	/// </summary>
	[Serializable]
	public struct DeformerMask : ISerializationCallbackReceiver
	{
		private const string MaterialTooltip =
			"用于控制哪些顶点会被变形的设置。";

		private const string VertexTooltip =
			"用于控制每个顶点被变形程度的设置。";

		/// 顶点选择设置（选择哪些顶点参与变形）。
		[Tooltip(MaterialTooltip)]
		[HideInInspector] // 简化：默认应用于全部，代码层保留自定义能力
		public SelectionSettings Selection;

		/// 顶点遮罩设置（控制每个顶点被变形的程度）。
		[Tooltip(VertexTooltip)]
		public VertexSettings Vertex;

		/// 顶点选择设置：控制哪些顶点会被变形。
		[Serializable]
		public struct SelectionSettings
		{
			/// 当前支持的遮罩类型。
			public enum MaskType : int
			{
				/// 对所有顶点应用变形。
				[InspectorName("应用于全部")]
				ApplyToAll = 0,
				/// 仅对使用指定材质（submesh 索引）的顶点应用变形。
				[InspectorName("材质")]
				Material = 1,
			}

			private const string TypeTooltip = 
				"选择遮罩类型：\n" +
				" - 应用于全部：对所有顶点应用变形。\n" +
				" - 材质：仅对使用该材质的顶点应用变形。";

			private const string IndexTooltip = 
				"用作遮罩的材质索引。格子变形只会应用到Mesh中使用此材质的部分。";

			/// 遮罩类型（应用于全部 / 材质）。
			[Tooltip(TypeTooltip)]
			public MaskType Type;

			/// 材质索引，当 Type 为 Material 时生效。
			[Tooltip(IndexTooltip)]
			[ShowIf(nameof(Type), MaskType.Material)]
			public int Index;
		}

		/// 顶点遮罩设置：控制每个顶点被变形的程度。
		[Serializable]
		public struct VertexSettings
		{
			/// 当前支持的遮罩类型。
			public enum MaskType : int
			{
				/// 不使用遮罩（所有顶点完全变形）。
				[InspectorName("无")]
				None = 0,
				/// 使用常数乘数（所有顶点乘以同一个倍数）。
				[InspectorName("常数")]
				Constant = 1,
				/// 使用顶点颜色（取指定通道作为变形程度）。
				[InspectorName("顶点色")]
				Color = 2,
				/// 使用顶点的纹理坐标（取指定分量作为变形程度）。
				[InspectorName("UV")]
				UV = 3,
				/// 使用贴图采样（用 UV 采样贴图，取指定通道作为变形程度）。
				[InspectorName("贴图")]
				Texture = 4,
			}

			/// 可用的颜色通道（顶点色/UV/贴图遮罩中取哪个分量）。
			public enum ColorChannel : int
			{
				[InspectorName("红")]
				Red = 0,
				[InspectorName("绿")]
				Green = 1,
				[InspectorName("蓝")]
				Blue = 2,
				[InspectorName("Alpha")]
				Alpha = 3,
			}

			private const string TypeTooltip = 
				"顶点遮罩类型：\n" +
				" - 无：不使用遮罩。\n" +
				" - 常数：使用常数乘数。\n" +
				" - 顶点色：使用顶点颜色。\n" +
				" - UV：使用顶点的纹理坐标。\n" +
				" - 贴图：使用贴图采样。";

			private const string UVTooltip =
				"根据遮罩类型而变化：\n" +
				" - UV：用作遮罩的纹理坐标。\n" +
				" - 贴图：用于采样贴图的 UV。";

			private const string TextureTooltip = "用作遮罩的贴图。";
			private const string ChannelTooltip = "要使用的 RGBA 通道。";
			private const string MultiplierTooltip = "遮罩的缩放倍数。";

			/// 顶点遮罩类型（无/常数/顶点色/UV/贴图）。
			[Tooltip(TypeTooltip)]
			[HideInInspector] // 简化：固定为「常数」，代码层保留其余类型
			public MaskType Type;

			/// 使用的纹理坐标（UV 遮罩 / 贴图遮罩时生效）。
			[Tooltip(UVTooltip)]
			[HideInInspector]
			public TextureCoordinate UV;

			/// 用作遮罩的贴图（贴图遮罩时生效）。
			[Tooltip(TextureTooltip)]
			[HideInInspector]
			public Texture Texture;

			/// 要使用的 RGBA 通道（顶点色 / UV / 贴图遮罩时生效）。
			[Tooltip(ChannelTooltip)]
			[HideInInspector]
			public ColorChannel Channel;

			/// 遮罩的缩放倍数（常数遮罩时为最终值，其余类型时为乘在采样值上的系数）。
			[Tooltip(MultiplierTooltip)]
			public float Multiplier;
		}

		#region Serialization

		/// 序列化前清空非贴图遮罩的贴图引用（避免场景/预制体资产残留无用的贴图引用）。
		void ISerializationCallbackReceiver.OnBeforeSerialize()
		{
			if (Vertex.Type != VertexSettings.MaskType.Texture)
			{
				Vertex.Texture = null;
			}
		}

		/// 数据升级：旧数据中 Type 为 None（无遮罩）时迁移为 Constant，让 Multiplier 始终参与运算（与简化前行为一致）。
		void ISerializationCallbackReceiver.OnAfterDeserialize()
		{
			if (Vertex.Type == VertexSettings.MaskType.None)
			{
				Vertex.Type = VertexSettings.MaskType.Constant;
			}
		}

		#endregion
	}
}
