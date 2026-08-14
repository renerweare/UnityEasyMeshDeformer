// ============================================================================
// DeformerDrawer.cs
// 概述：晶格线框绘制器：绘制相邻控制点连线，并按"挤压/拉伸"程度叠加辉光；
// 通过深度排序 + zTest 实现"遮挡线变暗、可见线明亮"的立体观感。
// 关键算法：SortLines 按线段中心在相机前方向上的投影深度排序（画家算法）；
// DrawLine 依据线段长度相对单位长度的偏差映射辉光渐变与粗细。
// ============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EasyMeshDeformation.Editor
{
	/// <summary>在 Scene 视图中绘制晶格线框的工具类：辉光线框、深度排序、"前/后"两段式渲染。</summary>
	public class DeformerDrawer
	{
		/// <summary>要绘制线框的晶格对象。</summary>
		private DeformerCube _cube;

		/// <summary>线段缓存列表：每次 SortLines 时重建，避免重复分配。</summary>
		private readonly List<Line> _lines = new();

		/// <summary>实例化绘制器并绑定要绘制的晶格。</summary>
		/// <param name="cube">目标晶格（DeformerCube）。</param>
		public DeformerDrawer(DeformerCube cube)
		{
			_cube = cube;
		}

		/// <summary>绘制晶格线框：Layout 阶段重建并排序线段，Repaint 阶段分两层绘制（先远后近）。</summary>
		public void Draw()
		{
			// Layout 阶段只做几何重建与排序，不产生实际绘制
			if (Event.current.type == EventType.Layout)
			{
				SortLines();
			}
			// Repaint 阶段：先用 Greater 深度测试画被遮挡的暗线，再用 LessEqual 画可见的亮线
			else if (Event.current.type == EventType.Repaint)
			{
				DrawLines(DeformSettings.LineBehindOpacity, CompareFunction.Greater);
				DrawLines(DeformSettings.LineInFrontOpacity, CompareFunction.LessEqual);
			}
		}

		/// <summary>静态单例实例：供静态 Draw 方法复用，避免每次绘制都创建对象。</summary>
		private static readonly DeformerDrawer _instance = new(null);

		/// <summary>静态方法：为指定晶格绘制线框（只画"可见层"，适合无遮挡需求的场景）。</summary>
		/// <param name="cube">要绘制线框的晶格对象。</param>
		public static void Draw(DeformerCube cube)
		{
			if (Event.current.type == EventType.Repaint)
			{
				_instance._cube = cube;
				_instance.SortLines();
				_instance.DrawLines(DeformSettings.LineBehindOpacity, CompareFunction.LessEqual);
			}
		}

		/// <summary>重建线段列表并按深度排序（画家算法）：相邻控制点连线，按线段中心在相机前方向的投影深度降序排列。</summary>
		private void SortLines()
		{
			_lines.Clear();

			// 三个轴向分别连接相邻控制点（越界处不连线）
			for (int i = 0; i < _cube.Resolution.x; i++)
			{
				for (int j = 0; j < _cube.Resolution.y; j++)
				{
					for (int k = 0; k < _cube.Resolution.z; k++)
					{
						Vector3 pos = _cube.GetHandleWorldPosition(i, j, k);

						if (i != _cube.Resolution.x - 1) _lines.Add(new Line(pos, _cube.GetHandleWorldPosition(i + 1, j, k)));
						if (j != _cube.Resolution.y - 1) _lines.Add(new Line(pos, _cube.GetHandleWorldPosition(i, j + 1, k)));
						if (k != _cube.Resolution.z - 1) _lines.Add(new Line(pos, _cube.GetHandleWorldPosition(i, j, k + 1)));
					}
				}
			}

			// 深度比较器：按线段中心在相机前方向上的投影深度排序（放大 100 倍取整，避免浮点噪声）
			static int CompareDepth(Line a, Line b)
			{
				Vector3 forward = Camera.current.transform.forward;
				float depthA = Vector3.Dot(forward, a.Centre());
				float depthB = Vector3.Dot(forward, b.Centre());
				float difference = 100 * (depthB - depthA);
				return (int)difference;
			}

			// 深度排序：近的排后面（后绘制）、远的排前面（先绘制）
			_lines.Sort(CompareDepth);
		}

		/// <summary>以指定透明度与深度测试函数绘制全部线段（临时切换 Handles.zTest，绘制后恢复）。</summary>
		/// <param name="alpha">线段透明度（0~1，会叠加在颜色上）。</param>
		/// <param name="compareFunction">深度测试函数：Greater 画被遮挡暗线，LessEqual 画可见亮线。</param>
		private void DrawLines(float alpha, CompareFunction compareFunction)
		{
			CompareFunction previousZTest = Handles.zTest;
			Handles.zTest = compareFunction;

			for (int i = 0; i < _lines.Count; i++)
			{
				DrawLine(_lines[i], alpha);
			}

			// 恢复原来的深度测试设置
			Handles.zTest = previousZTest;
		}

		/// <summary>复用的双顶点数组：避免为 DrawAAPolyLine 反复分配数组。</summary>
		private static readonly Vector3[] _line = new Vector3[2];

		/// <summary>绘制单条线段：把线段换算到"单位晶格"长度与 1 比较得 squishStretchFactor，驱动辉光厚度/颜色/透明度。</summary>
		/// <param name="line">线段的两个世界空间端点。</param>
		/// <param name="alpha">叠加的透明度。</param>
		private void DrawLine(Line line, float alpha)
		{
			// 把线段向量转到晶格局部空间并按分辨率缩放为"单位格子"长度
			Vector3 relativeOffset = _cube.transform.InverseTransformVector(line.B - line.A);
			relativeOffset = Vector3.Scale(relativeOffset, _cube.Resolution - Vector3Int.one);

			// 长度与 1 的偏差钳制在 [-1, 1]：负数=挤压，正数=拉伸
			float squishStretchFactor = Mathf.Clamp(relativeOffset.magnitude - 1f, -1f, 1f);

			// 辉光厚度：基准为普通线宽，再按偏差插值到对应的挤压/拉伸厚度
			float minThickness = DeformSettings.LineThickness;
			float maxThickness = (squishStretchFactor < 0)
				? DeformSettings.GlowThicknessSquish
				: DeformSettings.GlowThicknessStretch;
			float glowThickness = 2 * Mathf.Lerp(minThickness, maxThickness, Mathf.Abs(squishStretchFactor));

			// 把端点写入复用数组，避免每帧分配新数组
			_line[0] = line.A;
			_line[1] = line.B;

			// 按挤压/拉伸程度采样辉光渐变颜色，绘制底层辉光线
			Color glowColour = DeformSettings.GlowColorGradient.Evaluate(0.5f * squishStretchFactor + 0.5f);
			glowColour.a *= alpha * DeformSettings.GlowOpacity;

			using (new Handles.DrawingScope(glowColour, Matrix4x4.identity))
			{
				Handles.DrawAAPolyLine(glowThickness, _line);
			}

			// 再绘制固定宽度的实线覆盖在辉光之上，形成"亮芯 + 光晕"效果
			Color lineColour = DeformSettings.LineColor;
			lineColour.a *= alpha;

			using (new Handles.DrawingScope(lineColour, Matrix4x4.identity))
			{
				Handles.DrawAAPolyLine(DeformSettings.LineThickness, _line);
			}
		}

		/// <summary>线段数据结构：世界空间两个端点与中心点计算（用于深度排序）。</summary>
		struct Line
		{
			/// <summary>线段起点（世界空间）。</summary>
			public Vector3 A;

			/// <summary>线段终点（世界空间）。</summary>
			public Vector3 B;

			/// <summary>构造一条线段。</summary>
			public Line(Vector3 a, Vector3 b)
			{
				A = a;
				B = b;
			}

			/// <summary>返回线段中心点（用于深度排序比较）。</summary>
			public readonly Vector3 Centre()
			{
				return (A + B) / 2;
			}
		}
	}
}
