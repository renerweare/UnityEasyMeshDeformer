using System;
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// CPU 端 FFD（自由变形）求解器，与 DeformCompute.compute 的 GPU 实现等价（用于编辑器预览、烘焙、回读等）。
	/// 算法：点变换到晶格局部空间 → 计算所在单元与单元内坐标 → 按插值方式对相邻控制点偏移加权求和 → 非 Global 时越界偏移按 0。
	/// </summary>
	public static class DeformSolver
	{
		#region Public Methods

		/// 变形单个点（世界坐标输入/输出，内部变换到晶格局部空间做 FFD 再变换回世界坐标）。
		public static Vector3 DeformPoint(DeformerItem item, Vector3 point)
		{
			Transform transform = item.Cube.transform;
			// 使用栈上小数组避免堆分配（性能优化）
			Span<Vector3> input = stackalloc Vector3[1];
			Span<Vector3> output = stackalloc Vector3[1];

			input[0] = transform.InverseTransformPoint(point);
			TransformPositions(item, input, output);
			return transform.TransformPoint(output[0]);
		}

		/// 变形一个变换矩阵（位置/朝向/缩放）：对原点与三个轴微元点分别做 FFD，再用变形后的四个点重建矩阵。
		public static Matrix4x4 DeformTransform(DeformerItem item, Matrix4x4 localToWorld)
		{
			Transform transform = item.Cube.transform;
			// 需要同时变形 4 个点：原点 + 三个轴微元点（偏移 0.001，取矩阵三列）
			Span<Vector3> input = stackalloc Vector3[4];
			Span<Vector3> output = stackalloc Vector3[4];

			input[0] = transform.InverseTransformPoint(localToWorld.GetPosition());
			input[1] = transform.InverseTransformPoint(localToWorld.GetPosition() + 0.001f * (Vector3)localToWorld.GetColumn(0));
			input[2] = transform.InverseTransformPoint(localToWorld.GetPosition() + 0.001f * (Vector3)localToWorld.GetColumn(1));
			input[3] = transform.InverseTransformPoint(localToWorld.GetPosition() + 0.001f * (Vector3)localToWorld.GetColumn(2));

			TransformPositions(item, input, output);

			Vector3 position = transform.TransformPoint(output[0]);

			// 用变形后的原点与三个轴微元点重建矩阵：轴向量 = 微元点 - 原点，再乘 1000 还原 0.001 的缩放
			localToWorld = new Matrix4x4(
				1000f * (transform.TransformPoint(output[1]) - position),
				1000f * (transform.TransformPoint(output[2]) - position),
				1000f * (transform.TransformPoint(output[3]) - position),
				position
			);

			// 确保齐次坐标分量为 1（单位缩放矩阵的 w 行）
			localToWorld[3, 3] = 1;

			return localToWorld;
		}

		#endregion

		#region Coefficients

		/// 插值系数接口：提供每轴采样点数量、按单元内坐标生成权重、按控制点单元偏移取权重。
		private interface ICoefficients
		{
			/// 每轴采样点数量（线性 2，三次 4）。
			int Length { get; }
			/// 根据单元内坐标（0~1）计算各方向权重系数。
			void GetCoefficients(Vector3 cellPosition);
			/// 按控制点单元偏移（0 或 1 / 0~3）取三轴权重乘积。
			float GetWeight(Vector3Int cell);
		}

		/// 三次插值系数：每轴 4 个采样点（4×4×4=64 个控制点），Catmull-Rom 风格基函数（aa0/aa1），对应 GPU 的 DEFORM_INTRP_CUBIC。
		private struct CubicCoefficients : ICoefficients
		{
			/// X 轴上 4 个采样点的权重。
			private Vector4 _x;
			/// Y 轴上 4 个采样点的权重。
			private Vector4 _y;
			/// Z 轴上 4 个采样点的权重。
			private Vector4 _z;

			public readonly int Length => 4;

			/// 按单元内坐标计算三次插值的 4 个基函数权重（逐轴独立）。
			public void GetCoefficients(Vector3 cellPosition)
			{
				// 三次基函数：h_00/h_10 对应 cellPosition 方向，h_01/h_11 对应其镜像（1 - cellPosition）方向
				Vector3 h_00 = Aa1(cellPosition + Vector3.one);
				Vector3 h_10 = Aa0(cellPosition);
				Vector3 h_01 = Aa0(Vector3.one - cellPosition);
				Vector3 h_11 = Aa1(2 * Vector3.one - cellPosition);

				_x = new Vector4(h_00.x, h_10.x, h_01.x, h_11.x);
				_y = new Vector4(h_00.y, h_10.y, h_01.y, h_11.y);
				_z = new Vector4(h_00.z, h_10.z, h_01.z, h_11.z);
			}

			/// 三次基函数 a₀(t) = 1.5t³ - 2.5t² + 1（过 (0,1) 与 (1,0)）。
			private static Vector3 Aa0(Vector3 t)
			{
				Vector3 t2 = Vector3.Scale(t, t);
				return 1.5f * Vector3.Scale(t2, t) - 2.5f * t2 + Vector3.one;
			}

			/// 三次基函数 a₁(t) = -0.5t³ + 2.5t² - 4t + 2（过 (0,2) 与 (1,0)）。
			private static Vector3 Aa1(Vector3 t)
			{
				Vector3 t2 = Vector3.Scale(t, t);
				return -0.5f * Vector3.Scale(t2, t) + 2.5f * t2 - 4.0f * t + 2.0f * Vector3.one;
			}

			/// 综合权重 = 三轴权重的乘积。
			public float GetWeight(Vector3Int cell)
			{
				return _x[cell.x] * _y[cell.y] * _z[cell.z];
			}
		}

		/// 线性插值系数：每轴 2 个采样点（2×2×2=8 个控制点），三线性插值，对应 GPU 的线性分支。
		private struct LinearCoefficients : ICoefficients
		{
			/// X 轴上 2 个采样点的权重（h_10 与 h_01）。
			private Vector2 _x;
			/// Y 轴上 2 个采样点的权重。
			private Vector2 _y;
			/// Z 轴上 2 个采样点的权重。
			private Vector2 _z;

			public readonly int Length => 2;

			/// 按单元内坐标计算线性权重：h_10 = 1 - 坐标，h_01 = 坐标。
			public void GetCoefficients(Vector3 cellPosition)
			{
				Vector3 h_10 = Vector3.one - cellPosition;
				Vector3 h_01 = cellPosition;

				_x = new Vector2(h_10.x, h_01.x);
				_y = new Vector2(h_10.y, h_01.y);
				_z = new Vector2(h_10.z, h_01.z);
			}

			/// 综合权重 = 三轴权重的乘积。
			public float GetWeight(Vector3Int cell)
			{
				return _x[cell.x] * _y[cell.y] * _z[cell.z];
			}
		}

		/// 平滑线性插值系数：先对单元内坐标逐轴 SmoothStep 再线性插值，对应 GPU 的 DEFORM_INTRP_SMOOTH。
		private struct SmoothLinearCoefficients : ICoefficients
		{
			/// 内部复用的线性系数结构。
			private LinearCoefficients _coeff;

			public readonly int Length => _coeff.Length;

			/// 对单元内坐标逐轴执行 SmoothStep(0,1,x) 后按线性方式生成权重。
			public void GetCoefficients(Vector3 cellPosition)
			{
				cellPosition = new(
					Mathf.SmoothStep(0, 1, cellPosition.x),
					Mathf.SmoothStep(0, 1, cellPosition.y),
					Mathf.SmoothStep(0, 1, cellPosition.z)
				);
				_coeff.GetCoefficients(cellPosition);
			}

			/// 综合权重（委托给内部线性系数）。
			public float GetWeight(Vector3Int cell)
			{
				return _coeff.GetWeight(cell);
			}
		}

		#endregion

		#region Private Methods

		/// 获取指定控制点的偏移量：非 Global（越界归零）时越界返回零（对应 DEFORM_ZERO_OUTSIDE）；随后钳制坐标并从偏移表读取。
		private static Vector3 GetOffset(DeformerCube cube, Vector3Int cell, bool global)
		{
			// 非 Global 模式：控制点越界时偏移为 0（变形在晶格外衰减为 0）
			if (!global)
			{
				if ((cell.x < 0) || (cell.x >= cube.Resolution.x) ||
					(cell.y < 0) || (cell.y >= cube.Resolution.y) ||
					(cell.z < 0) || (cell.z >= cube.Resolution.z))
					return Vector3.zero;
			}

			// 钳制到合法范围：Global 模式下越界控制点沿用最边缘控制点的偏移（变形延续到晶格外部）
			cell = Vector3Int.Max(cell, Vector3Int.zero);
			cell = Vector3Int.Min(cell, cube.Resolution - Vector3Int.one);
			return cube.GetHandleOffset(cell);
		}

		/// 计算顶点在单元内的归一化坐标（0~1）：局部坐标（-0.5~0.5）平移 0.5、缩放至 (Resolution-1) 后减去单元起点。
		private static Vector3 GetCellPosition(DeformerCube cube, Vector3 cubePosition, Vector3Int cell) 
		{
			cubePosition += 0.5f * Vector3.one;
			cubePosition.Scale(cube.Resolution - Vector3Int.one);
			return cubePosition - cell;
		}

		/// 计算顶点所在晶格单元坐标（局部坐标平移 0.5、缩放至 (Resolution-1) 后向下取整，可能越界由调用方处理）。
		private static Vector3Int GetCell(DeformerCube cube, Vector3 cubePosition)
		{
			cubePosition += 0.5f * Vector3.one;
			cubePosition.Scale(cube.Resolution - Vector3Int.one);
			return Vector3Int.FloorToInt(cubePosition);
		}

		/// 按晶格项的插值方式选择对应系数结构，对一组点执行 FFD 变形。
		private static void TransformPositions(DeformerItem item, ReadOnlySpan<Vector3> inputs, Span<Vector3> outputs)
		{
			switch (item.Interpolation)
			{
				case InterpolationMethod.LinearSharp:
					TransformPositions<LinearCoefficients>(item, inputs, outputs);
					break;
				case InterpolationMethod.LinearSmooth:
					TransformPositions<SmoothLinearCoefficients>(item, inputs, outputs);
					break;
				case InterpolationMethod.Cubic:
					TransformPositions<CubicCoefficients>(item, inputs, outputs);
					break;
			}
		}

		/// 泛型 FFD 核心：以第一个点确定单元，缓存各点插值系数，遍历单元周围 numSamples³ 个控制点，把「偏移 × 权重」累加到输出（与 GPU 内核一致）。
		private static void TransformPositions<Coefficients>(DeformerItem item, ReadOnlySpan<Vector3> input, Span<Vector3> output) where Coefficients : unmanaged, ICoefficients
		{
			int numPositions = input.Length;
			// 每轴采样点数：线性为 2，三次为 4
			int numSamples = default(Coefficients).Length;

			DeformerCube cube = item.Cube;
			bool global = item.Global;

			// 遮罩乘数：非「无」遮罩时使用 Multiplier，否则为 1（完全变形）
			float multiplier = (item.Mask.Vertex.Type != DeformerMask.VertexSettings.MaskType.None) 
				? item.Mask.Vertex.Multiplier : 1.0f;

			// 以第一个点确定所在单元（所有点共用同一单元，保证法线/切线等辅助点与主点使用一致的采样范围）
			Vector3Int cell = GetCell(cube, input[0]);

			// 为每个输入点缓存插值系数（栈上分配避免堆开销）
			Span<Coefficients> h = stackalloc Coefficients[numPositions];
			for (int r = 0; r < numPositions; r++)
			{
				h[r].GetCoefficients(GetCellPosition(cube, input[r], cell));
				output[r] = input[r];
			}

			// 三重循环遍历单元周围的 numSamples³ 个控制点
			for (int i = 0; i < numSamples; i++)
			{
				for (int j = 0; j < numSamples; j++)
				{
					for (int k = 0; k < numSamples; k++)
					{
						// 控制点相对单元的偏移（线性：0/1；三次：-1/0/1/2）
						Vector3Int index = new(i, j, k);
						// 控制点在晶格中的绝对坐标（以 cell 为基准居中偏移）
						Vector3Int handle = cell + index - (numSamples / 2 - 1) * Vector3Int.one;
						// 控制点偏移量（越界时按 Global 规则处理），并乘上遮罩乘数
						Vector3 handleOffset = multiplier * GetOffset(cube, handle, global);

						// 把该控制点的影响按权重累加到每个输出点上
						for (int m = 0; m < numPositions; m++)
						{
							output[m] += handleOffset * h[m].GetWeight(index);
						}
					}
				}
			}
		}

		#endregion
	}
}
