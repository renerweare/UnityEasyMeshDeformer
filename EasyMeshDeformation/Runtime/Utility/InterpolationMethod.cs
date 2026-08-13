using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 晶格变形使用的插值方式，对应 DeformCompute.compute 的插值分支与 DeformSolver 中的系数结构。
	/// </summary>
	public enum InterpolationMethod
	{
		/// 三线性插值，不做平滑处理（清晰锐利，计算量最小）。
		[InspectorName("Linear (Sharp)")]
		LinearSharp = 1,

		/// 三线性插值，但每个轴向上先经过 SmoothStep 平滑（产生更柔和的过渡）。
		[InspectorName("Linear (Smooth)")]
		LinearSmooth = 0,

		/// 三三次（Cubic）插值：每个顶点受 4×4×4 = 64 个控制点影响，最平滑但开销最高。
		[InspectorName("Cubic")]
		Cubic = 2,
	}
}
