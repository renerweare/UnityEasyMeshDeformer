// ============================================================================
// DeformerCube.cs —— FFD（自由变形）晶格控制体
// 核心机制：晶格 = _resolution（每轴 ≥ 2）的 3D 控制点网格，每点是一个
// DeformerHandle 子物体；GetIndex/GetCoords 做 FFD 索引换算（x 最快、z 最慢），
// 每帧 LateUpdate 把各控制点 offset 汇总到 Offsets 缓冲供 compute shader 使用。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

namespace EasyMeshDeformation
{
	/// <summary>变形晶格组件：由控制点（DeformerHandle）构成的 3D 网格，是 FFD 的「笼子」。</summary>
	[ExecuteAlways]
	public class DeformerCube : MonoBehaviour
	{
		#region Constants

		/// <summary>「分辨率」字段在 Inspector 中的 Tooltip 文案。</summary>
		private const string ResolutionTooltip =
			"每个轴上的控制点数量。";

		#endregion

		/// <summary>选择衰减的范围模式（决定拖动控制点时的影响范围）。</summary>
		public enum SelectionFalloffMode
		{
			/// <summary>按分辨率比例：影响格子数 = 最大分辨率 × 比例（_selectionFalloffAmount）。</summary>
			[InspectorName("按分辨率比例")]
			Relative,
			/// <summary>手动指定：直接填写影响的控制点个数（_selectionFalloffCount）。</summary>
			[InspectorName("手动指定")]
			Manual,
		}

		#region Fields

		/// <summary>晶格分辨率：每轴控制点数量（≥2）；修改后必须调用 Setup() 重建。</summary>
		[SerializeField, NotKeyable, Tooltip(ResolutionTooltip)]
		private Vector3Int _resolution = new(2, 2, 2);

		/// <summary>全部控制点列表，按「x 最快、z 最慢」顺序排列（与 GetIndex/GetCoords 一致），由 Setup() 重建。</summary>
		[SerializeField, HideInInspector]
		private List<DeformerHandle> _handles = new();

		/// <summary>控制点偏移量的镜像缓冲，每帧从 _handles 同步，供 compute shader / 编辑器读取。</summary>
		private readonly List<Vector3> _offsets = new();

		/// <summary>是否启用选择衰减（拖动控制点时带动周围控制点）。</summary>
		[Tooltip("启用后，拖动控制点时按下面的设置带动周围控制点。")]
		[SerializeField] private bool _selectionFalloffEnabled = false;

		/// <summary>影响范围的计算方式：按分辨率比例 / 手动指定。</summary>
		[Tooltip("影响范围的计算方式：\n - 按分辨率比例：影响格子数 = 最大分辨率 × 比例\n - 手动指定：直接填写影响的控制点个数。")]
		[SerializeField] private SelectionFalloffMode _selectionFalloffMode = SelectionFalloffMode.Relative;

		/// <summary>影响范围（按分辨率比例，0~1）：0.1 = 仅影响紧邻控制点，1 = 整个晶格。</summary>
		[Tooltip("影响范围（按分辨率比例，0~1）：0.1 = 仅影响紧邻控制点，1 = 整个晶格。")]
		[SerializeField, Range(0f, 1f)] private float _selectionFalloffAmount = 0.25f;

		/// <summary>影响范围（手动）：影响的控制点个数，范围 0 ~ 最大分辨率。</summary>
		[Tooltip("影响范围（手动）：影响的控制点个数，范围 0 ~ 最大分辨率。")]
		[SerializeField, Min(0)] private int _selectionFalloffCount = 2;

		/// <summary>影响强度（0~1）：周围控制点的跟随程度，1 = 完全跟随选中点。</summary>
		[Tooltip("影响强度（0~1）：周围控制点的跟随程度，1 = 完全跟随选中点。")]
		[SerializeField, Range(0f, 1f)] private float _selectionFalloffStrength = 0.5f;

		/// <summary>仅用「生效控制点」列表中的控制点推动变形（其余视为未移动），由编辑器右键菜单设置。</summary>
		[Tooltip("启用后，只有「生效控制点」列表中的控制点会推动网格变形，其余控制点视为未移动。\n" +
			"在 Scene 中框选控制点后右键 → 「将选中的控制点设为生效」来设置。")]
		[SerializeField] private bool _useEnabledHandlesOnly = false;

		/// <summary>生效控制点列表（3D 坐标，编辑器右键菜单写入，序列化保存）。</summary>
		[SerializeField, HideInInspector]
		private List<Vector3Int> _enabledHandles = new();

		#endregion

		#region Properties

		/// <summary>晶格分辨率（每轴控制点数量）；修改请使用 Setup() 重建。</summary>
		public Vector3Int Resolution => _resolution;

		/// <summary>是否启用选择衰减。</summary>
		public bool SelectionFalloffEnabled { get => _selectionFalloffEnabled; set => _selectionFalloffEnabled = value; }

		/// <summary>选择衰减的影响强度（周围控制点的跟随程度，0~1）。</summary>
		public float SelectionFalloffStrength => _selectionFalloffStrength;

		/// <summary>是否仅使用「生效控制点」推动变形。</summary>
		public bool UseEnabledHandlesOnly
		{
			get => _useEnabledHandlesOnly;
			set => _useEnabledHandlesOnly = value;
		}

		/// <summary>生效控制点列表（3D 坐标，由编辑器操作设置）。</summary>
		public List<Vector3Int> EnabledHandles => _enabledHandles;

		/// <summary>供变形使用的控制点偏移量缓冲，由 LateUpdate 每帧自动更新。</summary>
		internal List<Vector3> Offsets
		{
			get
			{
				ValidateOffsets();
				return _offsets;
			}
		}

		#endregion

		#region Public Methods

		/// <summary>获取指定控制点相对其基准位置的当前偏移量（越界返回 Vector3.zero）。</summary>
		public Vector3 GetHandleOffset(int x, int y, int z)
		{
			// 防御：控制点数量不匹配（如 Reset 后）时返回安全值避免越界
			int index = GetIndex(x, y, z);
			if ((index < 0) || (index >= _handles.Count)) return Vector3.zero;
			return _handles[index].offset;
		}

		/// <inheritdoc cref="GetHandleOffset(int, int, int)"/>
		public Vector3 GetHandleOffset(Vector3Int coords) => GetHandleOffset(coords.x, coords.y, coords.z);

		/// <summary>设置指定控制点相对其基准位置的偏移量。</summary>
		public void SetHandleOffset(int x, int y, int z, Vector3 offset)
		{
			int index = GetIndex(x, y, z);
			if ((index < 0) || (index >= _handles.Count)) return;
			_handles[index].offset = offset;
		}

		/// <inheritdoc cref="SetHandleOffset(int, int, int, Vector3)"/>
		public void SetHandleOffset(Vector3Int coords, Vector3 offset) => SetHandleOffset(coords.x, coords.y, coords.z, offset);

		/// <summary>获取控制点的当前位置（基准位置 + 偏移量，晶格本地空间）。</summary>
		public Vector3 GetHandlePosition(int x, int y, int z)
		{
			return GetHandleOffset(x, y, z) + GetHandleBasePosition(x, y, z);
		}

		/// <inheritdoc cref="GetHandlePosition(int, int, int)"/>
		public Vector3 GetHandlePosition(Vector3Int coords) => GetHandlePosition(coords.x, coords.y, coords.z);

		/// <summary>设置控制点的位置（晶格本地空间），内部换算为偏移量保存。</summary>
		public void SetHandlePosition(int x, int y, int z, Vector3 position)
		{
			SetHandleOffset(x, y, z, position - GetHandleBasePosition(x, y, z));
		}

		/// <inheritdoc cref="SetHandlePosition(int, int, int, Vector3)"/>
		public void SetHandlePosition(Vector3Int coords, Vector3 position) => SetHandlePosition(coords.x, coords.y, coords.z, position);

		/// <summary>获取控制点的当前位置（含偏移量，世界空间）。</summary>
		public Vector3 GetHandleWorldPosition(int x, int y, int z)
		{
			return transform.TransformPoint(GetHandlePosition(x, y, z));
		}

		/// <inheritdoc cref="GetHandleBasePosition(int, int, int)"/>
		public Vector3 GetHandleWorldPosition(Vector3Int coords) => GetHandleWorldPosition(coords.x, coords.y, coords.z);

		/// <summary>设置控制点的位置（世界空间），内部换算到晶格本地空间保存。</summary>
		public void SetHandleWorldPosition(int x, int y, int z, Vector3 position)
		{
			SetHandlePosition(x, y, z, transform.InverseTransformPoint(position));
		}

		/// <inheritdoc cref="SetHandleWorldPosition(int, int, int, Vector3)"/>
		public void SetHandleWorldPosition(Vector3Int coords, Vector3 position) => SetHandleWorldPosition(coords.x, coords.y, coords.z, position);

		/// <summary>获取控制点的基准位置（本地空间）：坐标规格化到 [-0.5, 0.5]，与 compute shader 晶格采样约定一致。</summary>
		public Vector3 GetHandleBasePosition(int x, int y, int z)
		{
			return new Vector3(
				x / (_resolution.x - 1f) - 0.5f,
				y / (_resolution.y - 1f) - 0.5f,
				z / (_resolution.z - 1f) - 0.5f
			);
		}

		/// <inheritdoc cref="GetHandleBasePosition(int, int, int)"/>
		public Vector3 GetHandleBasePosition(Vector3Int coords) => GetHandleBasePosition(coords.x, coords.y, coords.z);

		/// <summary>获取控制点的基准位置（世界空间）。</summary>
		public Vector3 GetHandleBaseWorldPosition(int x, int y, int z)
		{
			return transform.TransformPoint(GetHandleBasePosition(x, y, z));
		}

		/// <inheritdoc cref="GetHandleBaseWorldPosition(int, int, int)"/>
		public Vector3 GetHandleBaseWorldPosition(Vector3Int coords) => GetHandleBaseWorldPosition(coords.x, coords.y, coords.z);

		/// <summary>按指定分辨率建立/重建晶格：删除旧的直接子级控制点，按分辨率创建新的（每轴 ≥ 2）。</summary>
		public void Setup(Vector3Int resolution)
		{
			// 钳制分辨率：每个轴不能小于 2（FFD 至少需要每个方向两个端点）
			resolution = Vector3Int.Max(2 * Vector3Int.one, resolution);

			DeformerHandle[] existingHandles = GetComponentsInChildren<DeformerHandle>();
			for (int i = 0; i < existingHandles.Length; i++)
			{
				GameObject child = existingHandles[i].gameObject;

				// 跳过属于子晶格（嵌套晶格）的控制点，只删除直接子物体
				if (child.transform.parent != transform)
					continue;

#if UNITY_EDITOR
				// 编辑器非运行态下走 Undo 删除，保证可撤销
				if (!Application.isPlaying)
				{
					UnityEditor.Undo.DestroyObjectImmediate(child);
				}
				else
#endif
				{
					Destroy(child);
				}
			}

#if UNITY_EDITOR
			// 记录撤销点，保证分辨率修改可撤销
			if (!Application.isPlaying) UnityEditor.Undo.RecordObject(this, "设置晶格");
#endif
			_resolution = resolution;

			// 创建新的控制点：三层循环按「x 最快、z 最慢」的顺序生成
			_handles.Clear();
			for (int k = 0; k < _resolution.z; k++)
			{
				for (int j = 0; j < _resolution.y; j++)
				{
					for (int i = 0; i < _resolution.x; i++)
					{
						// 子物体初始位置为零（真实位置由基准位置 + offset 计算）
						GameObject childObject = new($"Handle ({i}, {j}, {k})");
						childObject.transform.parent = transform;
						childObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

						// 挂载 DeformerHandle 并登记到 _handles（与 GetIndex 线性顺序一致）
						DeformerHandle handle = childObject.AddComponent<DeformerHandle>();
						_handles.Add(handle);
#if UNITY_EDITOR
						// 编辑器下注册创建撤销点
						if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(childObject, "设置晶格");
#endif
					}
				}
			}
#if UNITY_EDITOR
			// 编辑器下注册整体撤销点
			if (!Application.isPlaying) UnityEditor.Undo.RegisterCompleteObjectUndo(this, "设置晶格");
#endif
		}

		/// <inheritdoc cref="Setup(Vector3Int)"/>
		public void Setup(int x, int y, int z) => Setup(new Vector3Int(x, y, z));

		/// <summary>获取选择衰减的影响格子数（按当前模式计算，上限为最大分辨率）。</summary>
		public int GetSelectionFalloffRadius()
		{
			int maxResolution = Mathf.Max(_resolution.x, Mathf.Max(_resolution.y, _resolution.z));
			return _selectionFalloffMode switch
			{
				SelectionFalloffMode.Relative => Mathf.CeilToInt(maxResolution * _selectionFalloffAmount),
				_ => Mathf.Clamp(_selectionFalloffCount, 0, maxResolution),
			};
		}

		/// <summary>按 3D 坐标获取控制点组件。</summary>
		internal DeformerHandle GetHandle(int x, int y, int z)
		{
			return _handles[GetIndex(x, y, z)];
		}

		/// <inheritdoc cref="GetHandle(int, int, int)"/>
		internal DeformerHandle GetHandle(Vector3Int coords) => GetHandle(coords.x, coords.y, coords.z);

		/// <summary>反查控制点组件的 3D 坐标（未找到时返回 Vector3Int.zero）。</summary>
		internal Vector3Int GetHandleCoords(DeformerHandle handle)
		{
			int index = _handles.IndexOf(handle);
			if (index == -1) return Vector3Int.zero;

			return GetCoords(index);
		}

		#endregion

		#region Private Methods

		/// <summary>FFD 索引换算：index = x + res.x*y + res.x*res.y*z（x 最快、z 最慢），与 compute shader 布局一致。</summary>
		private int GetIndex(int x, int y, int z)
		{
			return x + (_resolution.x * y) + (_resolution.x * _resolution.y * z);
		}

		/// <summary>FFD 索引换算（GetIndex 逆运算）：把下标还原为 3D 坐标。</summary>
		private Vector3Int GetCoords(int index)
		{
			return new Vector3Int(
				index % _resolution.x,
				(index / _resolution.x) % _resolution.y,
				(index / (_resolution.x * _resolution.y)) % _resolution.z
			);
		}

		/// <summary>确保 Offsets 缓冲与控制点数量一致，不匹配时按 _handles 重建。</summary>
		private void ValidateOffsets()
		{
			if (_offsets.Count != _handles.Count)
			{
				_offsets.Clear();
				for (int i = 0; i < _handles.Count; i++)
				{
					_offsets.Add(_handles[i].offset);
				}
			}
		}

		#endregion

		#region Unity Methods

		/// <summary>Reset 回调：序列化字段恢复默认导致 _handles 清空但子物体还在，重建控制点防越界。</summary>
		private void Reset()
		{
			// Reset 会把序列化字段恢复默认值，导致 _handles 清空但子物体还在，需重建防越界
			if (Application.isPlaying) return;
			Setup(_resolution);
		}

		/// <summary>启动时若控制点数量与分辨率不符（反序列化不完整）则重建晶格。</summary>
		private void Start()
		{
			if (_handles.Count != _resolution.x * _resolution.y * _resolution.z)
			{
				Setup(_resolution);
			}
		}

		/// <summary>每帧把控制点 offset 同步到 Offsets 缓冲；启用「仅生效控制点」时未生效的置零。</summary>
		private void LateUpdate()
		{
			ValidateOffsets();
			for (int i = 0; i < _handles.Count; i++)
			{
				_offsets[i] = _handles[i].offset;
			}

			// 仅生效控制点：未生效的 offset 置 0（生效列表为空时视为全部生效）
			if (_useEnabledHandlesOnly && (_enabledHandles.Count > 0))
			{
				HashSet<Vector3Int> enabled = new(_enabledHandles);
				for (int i = 0; i < _handles.Count; i++)
				{
					if (!enabled.Contains(GetCoords(i)))
					{
						_offsets[i] = Vector3.zero;
					}
				}
			}
		}

		/// <summary>编辑器值变化回调：把分辨率各轴钳制到 ≥ 2。</summary>
		private void OnValidate()
		{
			_resolution = Vector3Int.Max(2 * Vector3Int.one, _resolution);
		}

		#endregion
	}
}
