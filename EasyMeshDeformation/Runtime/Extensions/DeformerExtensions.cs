using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 晶格（DeformerCube）相关的实用扩展方法。
	/// </summary>
	public static class DeformerExtensions
	{
		/// 获取可枚举结构，用于遍历晶格中的所有控制点（handle）坐标，避免手写三重嵌套循环。
		public static Handles GetHandles(this DeformerCube cube)
		{
			return new Handles(cube);
		}

		/// 调整晶格的大小与位置，使其包住目标 Transform 上的所有渲染器（含子物体），常用于把晶格自动适配到模型包围范围。
		public static void FitToTransform(this DeformerCube cube, Transform target, bool includeChildren = true, bool rotateCube = false)
		{
			Renderer[] renderers = includeChildren
				? target.GetComponentsInChildren<Renderer>()
				: target.GetComponents<Renderer>();

			if (renderers.Length == 0) return;

			// 局部函数：把单个渲染器的本地包围盒转换到晶格本地空间并合并
			static Bounds GetBounds(Matrix4x4 worldToLocal, Renderer renderer)
			{
				Matrix4x4 rendererToLocal = worldToLocal * renderer.localToWorldMatrix;

				// Skinned Mesh以 rootBone 为基准（骨骼根决定了Skinned Mesh的实际包围范围）
				if ((renderer is SkinnedMeshRenderer skinnedRenderer) && (skinnedRenderer.rootBone != null))
				{
					rendererToLocal = worldToLocal * skinnedRenderer.rootBone.localToWorldMatrix;
				}

				// 把渲染器本地包围盒的 8 个角点全部变换到晶格本地空间，合并出一个新包围盒
				Bounds rendererBounds = renderer.localBounds;
				Bounds bounds = new(rendererToLocal.MultiplyPoint(rendererBounds.min), Vector3.zero);

				for (int x = 0; x <= 1; x++)
				{
					for (int y = 0; y <= 1; y++)
					{
						for (int z = 0; z <= 1; z++)
						{
							if (x == 0 && y == 0 && z == 0) continue;

							Vector3 offset = new(
								x * rendererBounds.size.x,
								y * rendererBounds.size.y,
								z * rendererBounds.size.z
							);
							Vector3 point = rendererBounds.min + offset;
							bounds.Encapsulate(rendererToLocal.MultiplyPoint(point));
						}
					}
				}

				return bounds;
			}

			Transform transform = cube.transform;
			if (rotateCube) transform.rotation = target.rotation;
			transform.localScale = Vector3.one;

			Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
			Bounds bounds = GetBounds(worldToLocal, renderers[0]);
			for (int i = 1; i < renderers.Length; i++)
			{
				bounds.Encapsulate(GetBounds(worldToLocal, renderers[i]));
			}

			transform.position = transform.TransformPoint(bounds.center);
			transform.localScale = bounds.size;
		}

		#region Enumeration

		/// 用于遍历晶格内所有控制点坐标的实用结构：foreach (Vector3Int h in cube.GetHandles()) { ... }。
		public struct Handles
		{
			/// 枚举器：按「先 z、再 y、最后 x」的顺序遍历晶格分辨率范围内的所有单元坐标。
			public struct Enumerator
			{
				/// 目标晶格。
				private DeformerCube _cube;
				/// 当前遍历到的单元坐标。
				private Vector3Int _index;

				/// 当前单元坐标。
				public readonly Vector3Int Current => _index;

				/// 初始坐标设为 (0, 0, -1)，使第一次 MoveNext 后落到 (0, 0, 0)。
				public Enumerator(DeformerCube cube)
				{
					_cube = cube;
					_index = new(0, 0, -1);
				}

				/// 前进到下一个控制点坐标；z 到顶进位 y，y 到顶进位 x，x 也到顶时遍历结束。
				public bool MoveNext()
				{
					_index.z += 1;

					if (_index.z >= _cube.Resolution.z)
					{
						_index.z = 0;
						_index.y += 1;

						if (_index.y >= _cube.Resolution.y)
						{
							_index.y = 0;
							_index.x += 1;

							if (_index.x >= _cube.Resolution.x)
							{
								return false;
							}
						}
					}

					return true;
				}
			}

			/// 目标晶格。
			private readonly DeformerCube _cube;

			/// 返回一个枚举器（供 foreach 使用）。
			public readonly Enumerator GetEnumerator() => new(_cube);

			/// 绑定目标晶格。
			public Handles(DeformerCube cube)
			{
				_cube = cube;
			}
		}

		#endregion
	}
}
