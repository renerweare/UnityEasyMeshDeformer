using System;
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 晶格修改器（MeshDeformerBase）相关的实用扩展方法。
	/// </summary>
	public static class MeshDeformerExtensions
	{
		/// 从 GPU 回读（ReadBack）变形后的Mesh，存入原始Mesh的副本并返回（使用静态顶点缓冲）。
		public static Mesh GetDeformedMesh(this MeshDeformerBase modifier)
		{
			return GetDeformedMesh(modifier, GetStaticVertexBuffer);
		}

		/// 从 GPU 回读变形并蒙皮后的Mesh副本（使用蒙皮顶点缓冲，包含当前蒙皮姿势下的结果）。
		public static Mesh GetDeformedSkinnedMesh(this MeshDeformerBase modifier)
		{
			return GetDeformedMesh(modifier, GetSkinnedVertexBuffer);
		}

		/// 回读逻辑核心：实例化Mesh副本，从指定顶点缓冲（静态或蒙皮）读取数据写入副本，存在附加缓冲时一并回读。
		private static Mesh GetDeformedMesh<T>(T modifier, Func<T, GraphicsBuffer> getVertexBuffer) where T : MeshDeformerBase
		{
			Mesh mesh = Mesh.Instantiate(modifier.Mesh);
			MeshInfo info = modifier.MeshInfo;

			GraphicsBuffer vertexBuffer = getVertexBuffer(modifier);
			if (vertexBuffer == null)
			{
				Debug.LogError("无法获取顶点缓冲区。");
				return null;
			}

			int size = info.VertexCount * info.BufferStride;
			byte[] data = new byte[size];
			vertexBuffer.GetData(data);
			mesh.SetVertexBufferData(data, 0, 0, size, 0);

			if (info.HasAdditionalBuffer())
			{
				GraphicsBuffer additionalBuffer = modifier.AdditionalBuffer;
				if (additionalBuffer == null)
				{
					Debug.LogError("无法获取附加顶点缓冲区。");
					return null;
				}

				int additionalSize = info.VertexCount * info.AdditionalStride;
				byte[] additionalData = new byte[additionalSize];
				additionalBuffer.GetData(additionalData);
				mesh.SetVertexBufferData(additionalData, 0, 0, additionalSize, 1);
			}

			return mesh;
		}

		/// 获取修改器的静态顶点缓冲。
		private static GraphicsBuffer GetStaticVertexBuffer(MeshDeformerBase modifier)
		{
			return modifier.VertexBuffer;
		}

		/// 获取修改器的蒙皮顶点缓冲（可能不存在，失败时返回 null）。
		private static GraphicsBuffer GetSkinnedVertexBuffer(MeshDeformerBase modifier)
		{
			modifier.TryGetSkinnedBuffer(out var buffer);
			return buffer;
		}
	}
}
