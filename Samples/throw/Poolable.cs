// ============================================================================
// Poolable.cs —— 池化对象组件
// 挂在池化对象（或其根物体）上：记录归属池与来源 prefab，提供显式 Release，
// 也支持取出后延迟自动归还。配合 ObjectPool 使用。
// ============================================================================
using System.Collections;
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 池化对象标记组件：显式 Release 归还，或设置 _autoReleaseAfter 延迟自动归还。
	/// </summary>
	[DisallowMultipleComponent]
	public class Poolable : MonoBehaviour
	{
		/// <summary>归属的对象池。</summary>
		public ObjectPool Pool { get; private set; }
		/// <summary>来源 prefab（决定归还到哪个池）。</summary>
		public GameObject SourcePrefab { get; private set; }
		/// <summary>是否已归还（防重复归还）。</summary>
		public bool Released { get; private set; }

		/// <summary>延迟自动归还（秒）：> 0 时取出后经过该时长自动回池；0 = 手动调用 Release。</summary>
		[SerializeField, Min(0f)] private float _autoReleaseAfter = 0f;

		private Coroutine _releaseRoutine;

		/// <summary>由对象池在 Get 时调用：记录归属并（可选）启动自动归还计时。</summary>
		internal void Init(ObjectPool pool, GameObject sourcePrefab)
		{
			Pool = pool;
			SourcePrefab = sourcePrefab;
			Released = false;

			if (_autoReleaseAfter > 0f)
			{
				if (_releaseRoutine != null) StopCoroutine(_releaseRoutine);
				_releaseRoutine = StartCoroutine(AutoRelease());
			}
		}

		/// <summary>归还到池（对象禁用并等待复用）；没有池时退化为直接销毁。</summary>
		public void Release()
		{
			if (Released) return;
			Released = true;
			if (Pool != null) Pool.Release(this);
			else Destroy(gameObject);
		}

		private IEnumerator AutoRelease()
		{
			yield return new WaitForSeconds(_autoReleaseAfter);
			Release();
		}
	}
}
