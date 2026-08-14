// ============================================================================
// ObjectPool.cs —— 多 prefab 对象池
// 每种 prefab 各自维护一个实例栈：Get 取出激活，Release 归还禁用，避免反复
// Instantiate/Destroy。配合 Poolable 组件实现自动归还。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 对象池：支持多种 prefab（每种一个池 + 各自预热数量），
	/// 通过 Get(prefab) 取出、Release(obj) 归还。
	/// </summary>
	public class ObjectPool : MonoBehaviour
	{
		/// <summary>池配置项：一个 prefab 对应一个池。</summary>
		[System.Serializable]
		public struct PoolItem
		{
			/// <summary>要池化的预制体。</summary>
			public GameObject Prefab;
			/// <summary>启动时预热（预创建）的实例数量。</summary>
			public int Prewarm;
		}

		[SerializeField] private PoolItem[] pools = new PoolItem[0];

		/// <summary>配置的第一个 prefab（供不区分档位时取用）；未配置返回 null。</summary>
		public GameObject FirstPrefab => pools.Length > 0 ? pools[0].Prefab : null;

		/// <summary>配置的 prefab 数量（供按冲量选档位）。</summary>
		public int PrefabCount => pools.Length;

		/// <summary>按索引取配置的 prefab（调用方需保证 0 ≤ index &lt; PrefabCount）。</summary>
		public GameObject GetPrefab(int index) => pools[index].Prefab;

		/// <summary>每种 prefab 一个实例栈（key = prefab 引用）。</summary>
		private readonly Dictionary<GameObject, Stack<GameObject>> _pool = new();

		private void Awake()
		{
			// 预热：为每种 prefab 预创建实例并禁用入栈
			foreach (PoolItem item in pools)
			{
				if (item.Prefab == null) continue;
				Stack<GameObject> stack = GetOrCreateStack(item.Prefab);
				for (int i = 0; i < item.Prewarm; i++)
				{
					GameObject obj = Create(item.Prefab);
					obj.SetActive(false);
					stack.Push(obj);
				}
			}
		}

		/// <summary>从指定 prefab 的池取出一个实例（池空则新建），激活并返回。</summary>
		public GameObject Get(GameObject prefab)
		{
			Stack<GameObject> stack = GetOrCreateStack(prefab);
			GameObject obj = stack.Count > 0 ? stack.Pop() : Create(prefab);

			// 记录归属（Poolable），供自动归还使用；预制体上已挂则复用
			Poolable poolable = obj.GetComponent<Poolable>();
			if (poolable == null) poolable = obj.AddComponent<Poolable>();
			poolable.Init(this, prefab);

			obj.SetActive(true);
			return obj;
		}

		/// <summary>归还实例（自动找到其所属池）；非池化对象直接销毁。</summary>
		public void Release(GameObject obj)
		{
			Poolable poolable = obj != null ? obj.GetComponent<Poolable>() : null;
			if (poolable == null) { if (obj != null) Destroy(obj); return; }
			poolable.Release();
		}

		/// <summary>Poolable 内部归还入口：禁用并放回所属池。</summary>
		internal void Release(Poolable poolable)
		{
			if (poolable.Released) return; // 防重复归还
			Stack<GameObject> stack = GetOrCreateStack(poolable.SourcePrefab);
			poolable.gameObject.SetActive(false);
			stack.Push(poolable.gameObject);
		}

		private GameObject Create(GameObject prefab)
		{
			GameObject obj = Instantiate(prefab, transform);
			obj.name = prefab.name;
			return obj;
		}

		private Stack<GameObject> GetOrCreateStack(GameObject prefab)
		{
			if (!_pool.TryGetValue(prefab, out Stack<GameObject> stack))
			{
				stack = new Stack<GameObject>();
				_pool.Add(prefab, stack);
			}
			return stack;
		}
	}
}
