// ============================================================================
// ImpactInfo.cs —— 记录墙最后一次被砸的信息（静态类）
// 由 WallImpactHandler 在触发碎裂前写入，供碎裂回调（特效/声音）在对应位置触发。
// ============================================================================
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>记录墙最后一次被砸的位置与冲量，供碎裂回调在正确位置播放特效/声音。</summary>
	public static class ImpactInfo
	{
		/// <summary>最后一次被砸的位置（世界坐标）。</summary>
		public static Vector3 LastHitPoint { get; private set; }

		/// <summary>最后一次被砸的冲量大小。</summary>
		public static float LastImpulse { get; private set; }

		/// <summary>记录一次砸击信息（碎裂前由处理器调用）。</summary>
		public static void Record(Vector3 hitPoint, float impulse)
		{
			LastHitPoint = hitPoint;
			LastImpulse = impulse;
		}
	}
}
