// ============================================================================
// SelectedHandles.cs
// 概述：控制点选中集合：以 ScriptableObject（DontSave，不落盘）保存选中索引，
// 支持 Undo/Redo，并通过静态字典在多个 DeformerCubeEditor 实例间共享。
// 提供枢轴计算（中心/最后选中点）、包围盒计算与索引合法性校验。
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>存储选中控制点的工具类：支持 Undo/Redo，并通过静态字典在多个晶格编辑器实例间共享。</summary>
	[Serializable]
	public class SelectedHandles : ScriptableObject
	{
		/// <summary>静态实例字典：每个晶格对应一个独立的选中集合实例，同晶格的所有编辑器共享。</summary>
		private static readonly Dictionary<DeformerCube, SelectedHandles> _instances = new();

		/// <summary>获取（或按需创建）指定晶格对应的选中集合实例（DontSave，仅作编辑器运行时数据）。</summary>
		/// <param name="cube">目标晶格。</param>
		/// <returns>与该晶格关联的选中集合实例。</returns>
		public static SelectedHandles Get(DeformerCube cube)
		{
			// 字典中不存在时创建新实例并缓存
			if (!_instances.TryGetValue(cube, out var instance))
			{
				instance = CreateInstance<SelectedHandles>();
				instance.name = "已选择的控制点";
				instance.hideFlags = HideFlags.DontSave;
				_instances[cube] = instance;
			}
			// 每次获取都刷新绑定的晶格引用（防止替换/重新加载后引用失效）
			instance._cube = cube;
			return instance;
		}

		/// <summary>选择变化事件：选中集合增删/清空时触发，供 Gizmo 等订阅方刷新状态。</summary>
		public event Action SelectionChanged;

		/// <summary>选中的控制点三维索引列表（序列化保存，支持 Undo）。</summary>
		[SerializeField] private List<Vector3Int> _handles = new();

		/// <summary>绑定的晶格对象（提供分辨率与位置计算，不序列化）。</summary>
		private DeformerCube _cube;

		/// <summary>只读访问选中索引列表：访问前先做合法性校验，防止分辨率变化后遗留越界索引。</summary>
		public IReadOnlyList<Vector3Int> Handles
		{
			get
			{
				Validate();
				return _handles;
			}
		}

		/// <summary>当前选中的控制点数量（访问前同样先校验）。</summary>
		public int Count
		{
			get
			{
				Validate();
				return _handles.Count;
			}
		}

		/// <summary>加入一个控制点索引（已存在则忽略），并触发选择变化事件。</summary>
		/// <param name="handle">要加入的三维索引。</param>
		public void Add(Vector3Int handle)
		{
			if (!_handles.Contains(handle))
			{
				_handles.Add(handle);
				SelectionChanged?.Invoke();
			}
		}

		/// <summary>批量加入多个控制点索引（逐个调用 Add）。</summary>
		/// <param name="handles">要加入的索引集合。</param>
		public void AddRange(IEnumerable<Vector3Int> handles)
		{
			foreach (Vector3Int handle in handles)
			{
				Add(handle);
			}
		}

		/// <summary>移除一个控制点索引，并触发选择变化事件。</summary>
		/// <param name="handle">要移除的三维索引。</param>
		public void Remove(Vector3Int handle)
		{
			_handles.Remove(handle);
			SelectionChanged?.Invoke();
		}

		/// <summary>清空全部选中索引，并触发选择变化事件。</summary>
		public void Clear()
		{
			_handles.Clear();
			SelectionChanged?.Invoke();
		}

		/// <summary>查询指定索引是否处于选中状态。</summary>
		/// <param name="handle">要查询的三维索引。</param>
		public bool Contains(Vector3Int handle)
		{
			return _handles.Contains(handle);
		}

		/// <summary>计算"当前枢轴"（基于变形后的世界位置）：Center 取平均值，否则取最后选中点；无选中返回零向量。</summary>
		/// <param name="mode">枢轴模式（Center 取中心，否则取最后选中点）。</param>
		public Vector3 GetPivot(PivotMode mode)
		{
			Validate();

			if (_handles.Count == 0) return default;

			// Center：求所有选中点世界坐标的平均值
			if (mode == PivotMode.Center)
			{
				Vector3 centre = Vector3.zero;
				for (int i = 0; i < _handles.Count; i++)
				{
					centre += _cube.GetHandleWorldPosition(_handles[i]);
				}
				centre /= Count;
				return centre;
			}
			// Pivot：返回最后选中的控制点位置
			else
			{
				return _cube.GetHandleWorldPosition(_handles[^1]);
			}
		}

		/// <summary>计算"基准枢轴"（基于未变形时的基准位置），用于 SelectionRelativeGizmos 估算变形前表面朝向。</summary>
		/// <param name="mode">枢轴模式。</param>
		public Vector3 GetBasePivot(PivotMode mode)
		{
			Validate();

			if (_handles.Count == 0) return default;

			// Center：基准位置的平均值
			if (mode == PivotMode.Center)
			{
				Vector3 centre = Vector3.zero;
				for (int i = 0; i < _handles.Count; i++)
				{
					centre += _cube.GetHandleBaseWorldPosition(_handles[i]);
				}
				centre /= Count;
				return centre;
			}
			// Pivot：最后选中点的基准位置
			else
			{
				return _cube.GetHandleBaseWorldPosition(_handles[^1]);
			}
		}

		/// <summary>计算选中集合的包围盒：单选返回以该点为中心、尺寸为单个晶格格子大小的包围盒；多选则逐步 Encapsulate。</summary>
		public Bounds GetBounds()
		{
			Validate();

			if (_handles.Count == 0) return default;

			// 以第一个选中点为初始值
			Vector3 initial = _cube.GetHandleWorldPosition(_handles[0]);

			// 单选：构造"单个晶格格子大小"的包围盒（格距 = 1/(分辨率-1)，变换到世界空间）
			if (_handles.Count == 1)
			{
				Vector3 scale = new(
					1f / (_cube.Resolution.x - 1), 
					1f / (_cube.Resolution.y - 1), 
					1f / (_cube.Resolution.z - 1)
				);
				scale = _cube.transform.TransformVector(scale);
				return new Bounds(initial, scale);
			}

			// 多选：从第一个点开始逐步扩展包围盒包含其余选中点
			Bounds bounds = new(initial, Vector3.zero);
			for (int i = 1; i < _handles.Count; i++)
			{
				bounds.Encapsulate(_cube.GetHandleWorldPosition(_handles[i]));
			}
			return bounds;
		}

		/// <summary>合法性校验：发现越界索引（如分辨率被调小）立即清空选择，避免下游使用无效索引。</summary>
		private void Validate()
		{
			foreach (Vector3Int handle in _handles)
			{
				// 任一轴索引不小于对应分辨率即视为越界
				if ((handle.x >= _cube.Resolution.x) ||
					(handle.y >= _cube.Resolution.y) ||
					(handle.z >= _cube.Resolution.z))
				{
					Clear();
					return;
				}
			}
		}
	}
}
