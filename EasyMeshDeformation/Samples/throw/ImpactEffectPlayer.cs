// ============================================================================
// ImpactEffectPlayer.cs —— 砸击特效/声音播放器
// 读取 ImpactInfo 记录的最后一次被砸位置，在该处实例化特效并播放音效。
// 挂到场景中的任意物体上，方法由下游组件（如 SliceAndFracture）的完成回调触发，
// 保证特效/声音出现在墙最后被砸的位置。
// ============================================================================
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 砸击特效播放器：读取 ImpactInfo 记录的位置，在最后一次被砸处播放特效与声音。
	/// </summary>
	public class ImpactEffectPlayer : MonoBehaviour
	{
		[SerializeField, Tooltip("特效预制体（在最后一次被砸的位置实例化）。")]
		private GameObject effectPrefab;
		[SerializeField, Tooltip("音效（在最后一次被砸的位置播放）。")]
		private AudioClip soundClip;
		[SerializeField, Tooltip("音效音量。")]
		private float soundVolume = 1f;

		/// <summary>回调入口：在最后被砸位置实例化特效并播放音效（挂到下游组件的完成回调）。</summary>
		public void PlayImpactEffect()
		{
			Vector3 point = ImpactInfo.LastHitPoint;

			if (effectPrefab != null) Instantiate(effectPrefab, point, Quaternion.identity);
			if (soundClip != null) AudioSource.PlayClipAtPoint(soundClip, point, soundVolume);
		}
	}
}
