using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 对Mesh应用变形的方法（决定除了位置之外还要变形哪些顶点属性）。
	/// </summary>
	public enum ApplyMethod
	{
		/// 仅修改顶点位置（性能最好，但光照在变形后可能不准确）。
		[InspectorName("仅位置")]
		PositionOnly,

		/// 同时修改位置、法线和切线（光照结果更准确，但开销更高）。
		[InspectorName("位置、法线和切线")]
		PositionNormalTangent,
	};
}
