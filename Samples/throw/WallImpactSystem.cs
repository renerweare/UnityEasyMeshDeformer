// ============================================================================
// WallImpactSystem.cs —— 墙砸击系统（主控）
// 机制：
//  1. 墙用 kinematic 刚体，被砸纹丝不动；冲量用「相对速度 × 对方质量」估算。
//  2. 每次砸击：在命中点按 mergeRadius 球范围查找已有砸点——
//     有则冲量叠加，没有则按本次冲量选对应深度的 defoCb（浅/中/深），
//     统一放到墙外（命中点沿法线偏移 placeOffset）。脚本只负责放置，
//     不修改任何格子 offset，凹陷形状由预制体自带。
//  3. 每次砸击后累加整体被砸值 _totalImpact。
//  4. 任一砸点累计冲量 ≥ partialThreshold（局部），或整体 ≥ totalThreshold（整体），
//     就在「最后一次被砸的位置」触发事件（特效/声音等由下游处理器响应）。
//  5. RecycleAll() 供下游处理器在结算变形后调用：移除 Items 引用并把 defoCb 归还池。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 墙砸击系统：管理砸点（位置/冲量/defoCb），支持局部叠加与整体累计双阈值，
	/// 达标后在最后一次被砸的位置触发事件（由下游处理器响应）。
	/// </summary>
	[RequireComponent(typeof(Rigidbody))]
	public class WallImpactSystem : MonoBehaviour
	{
		[SerializeField, Tooltip("defoCb 对象池（取池中配置的晶格放置压凹）。")]
		private ObjectPool defoCbPool;
		[SerializeField, Tooltip("墙上的变形器（留空自动查找）。")]
		private MeshDeformer deformer;
		[SerializeField, Tooltip("砸点合并半径：该范围内的砸击算作同一处，冲量叠加。")]
		private float mergeRadius = 1f;
		[SerializeField, Tooltip("放置偏移：defoCb 沿碰撞法线（墙外方向）偏离命中点的距离，各预制体共用。")]
		private float placeOffset = 0.5f;
		[SerializeField, Tooltip("局部触发阈值：单个砸点累计冲量达到后触发事件。")]
		private float partialThreshold = 30f;
		[SerializeField, Tooltip("整体触发阈值：整面墙累计被砸值达到后触发事件。")]
		private float totalThreshold = 100f;
		[SerializeField, Tooltip("触发事件（参数：最后一次被砸的位置, 冲量），由下游处理器订阅。")]
		private ImpactEvent onImpact;

		[System.Serializable]
		private class ImpactEvent : UnityEvent<Vector3, float> { }

		/// <summary>一个砸点：位置 + 法线 + 累计冲量 + 关联的 defoCb。</summary>
		private class ImpactPoint
		{
			public Vector3 Position;
			public Vector3 Normal;
			public float Impulse;
			public DeformerCube Cube;
		}

		private readonly List<ImpactPoint> _points = new();
		private float _totalImpact;
		private bool _triggered;

		private void Awake()
		{
			Rigidbody rb = GetComponent<Rigidbody>();
			rb.isKinematic = true;
			// 保持重力开关开启（kinematic 本身不受重力，墙不会动），
			// 这样碎裂后碎片才能继承 useGravity = true 正常下落
			rb.useGravity = true;

			if (deformer == null) deformer = GetComponent<MeshDeformer>();
		}

		private void OnCollisionEnter(Collision collision)
		{
			if (_triggered) return;

			float impulse = EstimateImpulse(collision);
			Vector3 hitPoint = collision.contacts[0].point;
			Vector3 normal = collision.contacts[0].normal;

			// 在合并半径内找已有砸点；没有则新建（按冲量选对应深度的 defoCb）并放置
			ImpactPoint point = FindOrCreatePoint(hitPoint, normal, impulse);

			// 冲量叠加（只累计数值，不修改格子 offset——凹陷形状由预制体自带）
			point.Impulse += impulse;

			// 更新整体被砸值
			_totalImpact += impulse;

			// 调试：输出本次砸击信息
			Debug.Log($"[WallImpact] 被砸：位置 {hitPoint}，冲量 {impulse:F2}，" +
				$"该点累计 {point.Impulse:F2}，整体累计 {_totalImpact:F2}" +
				(point.Cube != null ? "，叠加到已有砸点" : "，新建砸点"), this);

			// 局部或整体达到阈值 → 在最后一次被砸的位置触发事件（下游处理器响应）
			if ((point.Impulse >= partialThreshold) || (_totalImpact >= totalThreshold))
			{
				Debug.Log($"[WallImpact] 达到阈值（局部 {point.Impulse:F2}/{partialThreshold}，" +
					$"整体 {_totalImpact:F2}/{totalThreshold}）→ 在 {hitPoint} 触发事件", this);
				FireImpact(hitPoint, impulse);
			}
		}

		/// <summary>估算冲量：kinematic 刚体的 collision.impulse 不可靠，改用相对速度 × 对方质量。</summary>
		private float EstimateImpulse(Collision collision)
		{
			float mass = (collision.rigidbody != null) ? collision.rigidbody.mass : 1f;
			return mass * collision.relativeVelocity.magnitude;
		}

		/// <summary>在 mergeRadius 内找已有砸点；没有则新建（按冲量选对应深度的 defoCb 放置到命中点）。</summary>
		private ImpactPoint FindOrCreatePoint(Vector3 hitPoint, Vector3 normal, float impulse)
		{
			ImpactPoint nearest = null;
			float nearestDist = mergeRadius;
			foreach (ImpactPoint point in _points)
			{
				float dist = Vector3.Distance(point.Position, hitPoint);
				if (dist <= nearestDist)
				{
					nearest = point;
					nearestDist = dist;
				}
			}
			if (nearest != null) return nearest;

			// 新建砸点：按冲量选对应深度的 defoCb 预制体，root 放到命中点（格子中心即命中点），
			// 不修改任何格子 offset——凹陷形状完全由预制体自带
			DeformerCube cube = null;
			if ((defoCbPool != null) && (deformer != null))
			{
				GameObject prefab = PickPrefab(impulse);
				if (prefab != null)
				{
					cube = defoCbPool.Get(prefab).GetComponent<DeformerCube>();
					if (cube != null)
					{
						// 统一放到墙外：命中点沿法线方向偏移 placeOffset（各预制体共用同一距离）
						cube.transform.position = hitPoint + normal * placeOffset;
						cube.transform.rotation = Quaternion.LookRotation(normal);
						deformer.Items.Add(new DeformerItem { Cube = cube });
					}
				}
			}

			ImpactPoint createdPoint = new ImpactPoint
			{
				Position = hitPoint,
				Normal = normal,
				Cube = cube,
			};
			_points.Add(createdPoint);
			return createdPoint;
		}

		/// <summary>按冲量选 defoCb 档位：冲量 0 对应最浅，达到局部阈值附近对应最深。</summary>
		private GameObject PickPrefab(float impulse)
		{
			int count = (defoCbPool != null) ? defoCbPool.PrefabCount : 0;
			if (count <= 0) return null;
			int index = Mathf.RoundToInt(Mathf.Clamp01(impulse / partialThreshold) * (count - 1));
			return defoCbPool.GetPrefab(index);
		}

		/// <summary>超过阈值：标记已触发并派发事件（参数为最后一次被砸的位置与冲量）。</summary>
		private void FireImpact(Vector3 hitPoint, float impulse)
		{
			_triggered = true;
			onImpact?.Invoke(hitPoint, impulse);
		}

		/// <summary>回收全部 defoCb：移除变形器 Items 引用并把实例归还池（下游处理器结算后调用）。</summary>
		public void RecycleAll()
		{
			foreach (ImpactPoint point in _points)
			{
				if (point.Cube == null) continue;

				if (deformer != null)
				{
					for (int i = deformer.Items.Count - 1; i >= 0; i--)
					{
						if (deformer.Items[i].Cube == point.Cube) deformer.Items.RemoveAt(i);
					}
				}

				if (defoCbPool != null) defoCbPool.Release(point.Cube.gameObject);
			}
			_points.Clear();
		}
	}
}
