using UnityEngine;
using UnityEngine.Rendering;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 集中管理 DeformCompute.compute 中使用的参数 ID（PropertyToID 缓存）与局部关键字（LocalKeyword）句柄。
	/// </summary>
	public class DeformShaderProperties
	{
		// —— 主顶点缓冲区相关参数 ID ——
		public static readonly int VertexBufferId = Shader.PropertyToID("VertexBuffer");       // 主顶点缓冲区（RWByteAddressBuffer）
		public static readonly int VertexCountId  = Shader.PropertyToID("VertexCount");        // 顶点数量
		public static readonly int BufferStrideId = Shader.PropertyToID("BufferStride");       // 单个顶点的字节步长（stride）

		public static readonly int PositionOffsetId = Shader.PropertyToID("PositionOffset");   // 位置属性在主缓冲区内的字节偏移
		public static readonly int NormalOffsetId   = Shader.PropertyToID("NormalOffset");     // 法线属性在主缓冲区内的字节偏移
		public static readonly int TangentOffsetId  = Shader.PropertyToID("TangentOffset");    // 切线属性在主缓冲区内的字节偏移

		public static readonly int AdditionalBufferId = Shader.PropertyToID("AdditionalBuffer"); // 附加缓冲区（颜色、多组 UV 等次要属性）
		public static readonly int AdditionalStrideId = Shader.PropertyToID("AdditionalStride"); // 附加缓冲区中单个顶点的字节步长

		// —— 晶格（deformer cube）相关参数 ID ——
		public static readonly int DeformBufferId     = Shader.PropertyToID("DeformBuffer");     // 控制点偏移缓冲（StructuredBuffer<float3>），相对原位的偏移量
		public static readonly int ObjectToDeformId   = Shader.PropertyToID("ObjectToDeform");   // 对象空间 → 晶格空间的 4×4 矩阵（顶点变换到晶格局部空间）
		public static readonly int DeformToObjectId   = Shader.PropertyToID("DeformToObject");   // 晶格空间 → 对象空间的 4×4 矩阵（ObjectToDeform 的逆）
		public static readonly int DeformResolutionId = Shader.PropertyToID("DeformResolution"); // 晶格三轴分辨率（uint3）

		// —— 遮罩（mask）相关参数 ID ——
		public static readonly int MaskOffsetId     = Shader.PropertyToID("MaskOffset");     // 遮罩数据（顶点色/UV）在附加缓冲区中的字节偏移
		public static readonly int MaskChannelId    = Shader.PropertyToID("MaskChannel");    // 遮罩采样使用的颜色通道（RGBA 之一）
		public static readonly int MaskTextureId    = Shader.PropertyToID("MaskTexture");    // 用作遮罩的贴图（Texture2D）
		public static readonly int MaskMultiplierId = Shader.PropertyToID("MaskMultiplier"); // 遮罩缩放倍数（multiplier）

		// —— 按材质索引（submesh）筛选顶点相关参数 ID ——
		public static readonly int IndexToVertexMapId = Shader.PropertyToID("IndexToVertexMap"); // 顶点索引 → 顶点 ID 映射缓冲（DEFORM_USE_INDICES 时只变形指定材质顶点）
		public static readonly int IndexCountId       = Shader.PropertyToID("IndexCount");       // 索引映射缓冲的有效条目数量

		// —— 局部关键字（LocalKeyword）句柄 ——
		public readonly LocalKeyword NormalsKeyword;         // 同时变形法线与切线（DEFORM_NORMALS）
		public readonly LocalKeyword MultipleBuffersKeyword; // 使用附加顶点缓冲区（DEFORM_MULTIPLE_BUFFERS）

		public readonly LocalKeyword InterpolationSmooth;    // 插值：线性平滑（DEFORM_INTRP_SMOOTH）
		public readonly LocalKeyword InterpolationCubic;     // 插值：三次方（DEFORM_INTRP_CUBIC）
		public readonly LocalKeyword ZeroOutsideKeyword;     // 越界控制点偏移归零（DEFORM_ZERO_OUTSIDE，晶格项 Global=false 时启用）
		public readonly LocalKeyword MaskColorKeyword;       // 遮罩：顶点色（DEFORM_MASK_COLOR）
		public readonly LocalKeyword MaskUVKeyword;          // 遮罩：UV（DEFORM_MASK_UV）
		public readonly LocalKeyword MaskTextureKeyword;     // 遮罩：贴图采样（DEFORM_MASK_TEXTURE）
		public readonly LocalKeyword MaskConstantKeyword;    // 遮罩：常数乘数（DEFORM_MASK_CONSTANT）
		public readonly LocalKeyword UseIndicesKeyword;      // 按材质索引筛选顶点（DEFORM_USE_INDICES）

		/// 关联的计算着色器实例（用于设置/禁用关键字与参数）。
		private readonly ComputeShader _shader;

		/// 根据计算着色器初始化所有局部关键字句柄。
		public DeformShaderProperties(ComputeShader shader)
		{
			NormalsKeyword         = new LocalKeyword(shader, "DEFORM_NORMALS");
			MultipleBuffersKeyword = new LocalKeyword(shader, "DEFORM_MULTIPLE_BUFFERS");
			InterpolationSmooth    = new LocalKeyword(shader, "DEFORM_INTRP_SMOOTH");
			InterpolationCubic	   = new LocalKeyword(shader, "DEFORM_INTRP_CUBIC");
			ZeroOutsideKeyword     = new LocalKeyword(shader, "DEFORM_ZERO_OUTSIDE");
			MaskColorKeyword       = new LocalKeyword(shader, "DEFORM_MASK_COLOR");
			MaskUVKeyword          = new LocalKeyword(shader, "DEFORM_MASK_UV");
			MaskTextureKeyword     = new LocalKeyword(shader, "DEFORM_MASK_TEXTURE");
			MaskConstantKeyword    = new LocalKeyword(shader, "DEFORM_MASK_CONSTANT");
			UseIndicesKeyword      = new LocalKeyword(shader, "DEFORM_USE_INDICES");

			_shader = shader;
		}

		/// 禁用所有本类管理的关键字（每帧设置参数前调用，确保关键字组合干净）。
		public void DisableAllKeywords()
		{
			_shader.DisableKeyword(NormalsKeyword);
			_shader.DisableKeyword(MultipleBuffersKeyword);
			_shader.DisableKeyword(InterpolationSmooth);
			_shader.DisableKeyword(InterpolationCubic);
			_shader.DisableKeyword(ZeroOutsideKeyword);
			_shader.DisableKeyword(MaskColorKeyword);
			_shader.DisableKeyword(MaskUVKeyword);
			_shader.DisableKeyword(MaskTextureKeyword);
			_shader.DisableKeyword(MaskConstantKeyword);
			_shader.DisableKeyword(UseIndicesKeyword);
		}
	}
}
