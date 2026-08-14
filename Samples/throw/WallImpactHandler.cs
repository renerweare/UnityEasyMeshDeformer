// ============================================================================
// WallImpactHandler.cs —— 墙砸击事件处理器（订阅 WallImpactSystem 的 onImpact 事件）
// 机制：
//  1. 记录最后一次被砸的位置/冲量到 ImpactInfo（供后续回调里的特效/声音使用）。
//  2. 等一帧（等本帧 GPU 变形写完，含最后放置的 defoCb），再固定网格：
//     用 MeshCombiner 把墙「变形后的」网格融合写回容器 sharedMesh——注意变形发生在
//     GPU 顶点缓冲（compute shader），MeshCombiner 会从 GPU 回读，不能直接用 sharedMesh。
//  3. 回收 defoCb：移除变形器 Items 引用并把实例归还对象池。
//  4. 触发转发事件（在 Inspector 中绑到下游处理，例如 SliceAndFracture.CauseFracture）；
//     后续的特效/声音由下游组件的回调触发，原物体由下游自动隐藏。
// 注意：MeshCombiner 需勾选 Include Self Mesh（墙网格在容器自身时才会被固定）。
// ============================================================================
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 墙砸击事件处理器：记录砸击信息、用 MeshCombiner 固定变形网格、回收 defoCb，并转发事件。
	/// </summary>
	public class WallImpactHandler : MonoBehaviour
	{
		[SerializeField, Tooltip("墙砸击系统（回收 defoCb；留空自动查找）。")]
		private WallImpactSystem impactSystem;
		[SerializeField, Tooltip("网格融合器（固定变形网格；需勾选 Include Self Mesh；留空自动查找）。")]
		private MeshCombiner meshCombiner;
		[SerializeField, Tooltip("转发事件：固定网格并回收 defoCb 后触发（在 Inspector 中绑到下游处理）。")]
		private UnityEvent onImpactHandled;

		private void Awake()
		{
			if (impactSystem == null) impactSystem = GetComponent<WallImpactSystem>();
			if (meshCombiner == null) meshCombiner = GetComponent<MeshCombiner>();
		}

		/// <summary>事件入口：记录砸击信息，然后等一帧再固定网格并转发（保证最后一击的凹陷写入 GPU）。</summary>
		public void OnImpact(Vector3 hitPoint, float impulse)
		{
			// 1. 记录最后一次被砸的位置/冲量（供后续回调里的特效/声音使用）
			ImpactInfo.Record(hitPoint, impulse);

			// 2. 等一帧：GPU 变形在每帧渲染后（PostLateUpdate）写入顶点缓冲，
			//    本帧刚放置的 defoCb 此时还未生效，直接回读会缺失最后一击的凹痕。
			//    等一帧后 MeshCombiner 回读到的就是包含全部凹痕的变形网格。
			StartCoroutine(FixThenForward());
		}

		/// <summary>等一帧后：MeshCombiner 固定变形网格 → 回收 defoCb → 转发事件。</summary>
		private IEnumerator FixThenForward()
		{
			yield return null;

			// 固定变形网格（MeshCombiner 内部会从 GPU 回读变形结果，非 CPU 的 sharedMesh）
			if (meshCombiner != null) meshCombiner.CombineMeshes(false);

			// 回收 defoCb（固定后再回收，不影响上面的融合结果）
			if (impactSystem != null) impactSystem.RecycleAll();

			// 转发事件（在 Inspector 中绑到下游处理，如 SliceAndFracture.CauseFracture）
			onImpactHandled?.Invoke();
		}
	}
}
