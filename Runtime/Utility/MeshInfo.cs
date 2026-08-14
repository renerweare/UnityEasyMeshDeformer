using UnityEngine;
using UnityEngine.Rendering;

namespace EasyMeshDeformation
{
	/// <summary>
	/// Mesh顶点缓冲的布局信息：顶点数量、主/附加缓冲步长（stride）及各顶点属性（位置/法线/切线/颜色/多组 UV）的字节偏移。
	/// CPU（DeformSolver/回读）与 GPU（DeformFeature）共用，确保按正确偏移读写 GPU 顶点缓冲中的每个属性。
	/// </summary>
	public readonly struct MeshInfo
	{
		/// 顶点数量。
		public readonly int VertexCount;
		/// 主顶点缓冲（stream 0）中单个顶点的字节步长。
		public readonly int BufferStride;
		/// 附加顶点缓冲（stream 1）中单个顶点的字节步长；为 0 表示不存在附加缓冲。
		public readonly int AdditionalStride;

		// —— 主缓冲（stream 0）中各属性的字节偏移 ——
		/// 位置（Position）属性的字节偏移。
		public readonly int PositionOffset;
		/// 法线（Normal）属性的字节偏移。
		public readonly int NormalOffset;
		/// 切线（Tangent）属性的字节偏移。
		public readonly int TangentOffset;

		// —— 附加缓冲（stream 1）中各属性的字节偏移 ——
		/// 顶点色（Color）属性的字节偏移。
		public readonly int ColorOffset;
		/// UV0 的字节偏移。
		public readonly int TexCoord0Offset;
		/// UV1 的字节偏移。
		public readonly int TexCoord1Offset;
		/// UV2 的字节偏移。
		public readonly int TexCoord2Offset;
		/// UV3 的字节偏移。
		public readonly int TexCoord3Offset;
		/// UV4 的字节偏移。
		public readonly int TexCoord4Offset;
		/// UV5 的字节偏移。
		public readonly int TexCoord5Offset;
		/// UV6 的字节偏移。
		public readonly int TexCoord6Offset;
		/// UV7 的字节偏移。
		public readonly int TexCoord7Offset;

		/// 从Mesh读取并缓存各缓冲的步长与属性偏移。
		public MeshInfo(Mesh mesh)
		{
			VertexCount = mesh.vertexCount;
			BufferStride = mesh.GetVertexBufferStride(0);
			AdditionalStride = mesh.GetVertexBufferStride(1);

			PositionOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
			NormalOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Normal);
			TangentOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Tangent);

			ColorOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Color);
			TexCoord0Offset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0);
			TexCoord1Offset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord1);
			TexCoord2Offset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord2);
			TexCoord3Offset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord3);
			TexCoord4Offset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord4);
			TexCoord5Offset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord5);
			TexCoord6Offset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord6);
			TexCoord7Offset = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord7);
		}

		/// 按 UV 组序号（0~7）返回对应 UV 属性的字节偏移；序号越界时返回 -1。
		public readonly int GetTexCoordOffset(int index)
		{
			return index switch
			{
				0 => TexCoord0Offset,
				1 => TexCoord1Offset,
				2 => TexCoord2Offset,
				3 => TexCoord3Offset,
				4 => TexCoord4Offset,
				5 => TexCoord5Offset,
				6 => TexCoord6Offset,
				7 => TexCoord7Offset,
				_ => -1,
			};
		}

		/// Mesh是否存在附加顶点缓冲（即附加缓冲步长不为 0）。
		public readonly bool HasAdditionalBuffer() => AdditionalStride != 0;
	}
}
