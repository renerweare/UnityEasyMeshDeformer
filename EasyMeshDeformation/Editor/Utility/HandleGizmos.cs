// ============================================================================
// HandleGizmos.cs
// 概述：为选中的控制点集合提供移动/旋转/缩放手柄，支持"选择衰减"（选中点
// 带动周围控制点，权重由 CalculateWeights 计算：切比雪夫距离 + 线性衰减）。
// MouseDown 记录原始位置并算权重；拖拽按权重混合；MouseUp 为位移超阈值的
// 控制点逐点记录 Undo（一次拖拽、一次撤销）。
// ============================================================================
using System;
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>为选中的控制点集合绘制移动、旋转、缩放 Gizmo 的工具类（销毁时解除事件订阅）。</summary>
	public class HandleGizmos : IDisposable
	{
		/// <summary>当前正在进行的交互动作类型。</summary>
		public enum Action
		{
			/// <summary>无操作（空闲状态）。</summary>
			None,
			/// <summary>正在移动控制点。</summary>
			Moving,
			/// <summary>正在旋转控制点。</summary>
			Rotating,
			/// <summary>正在缩放控制点。</summary>
			Scaling
		};

		/// <summary>被操作的晶格对象。</summary>
		private readonly DeformerCube _cube;

		/// <summary>选中的控制点集合（提供选中索引与枢轴位置）。</summary>
		private readonly SelectedHandles _handles;

		/// <summary>当前交互动作（用于状态机判断与事件分发）。</summary>
		private Action _action = Action.None;

		/// <summary>刷新标记：选中集合变化且处于拖拽中时置为 true，强制下一帧用新权重/新枢轴重新应用变换。</summary>
		private bool _refresh = false;

		/// <summary>每个控制点的衰减权重（Array3D 三维数组，按索引访问）。</summary>
		private Array3D<float> _weights = new();

		/// <summary>拖拽开始时每个控制点的原始世界位置快照。</summary>
		private Array3D<Vector3> _originalPositions = new();

		/// <summary>拖拽开始时选中集合的枢轴世界位置（操作中心）。</summary>
		private Vector3 _originalPosition = Vector3.zero;

		/// <summary>拖拽开始时选中集合的基准旋转（用于旋转/缩放手柄对齐）。</summary>
		private Quaternion _originalRotation = Quaternion.identity;

		/// <summary>拖拽开始时的手柄缩放基准（恒为 1，作为增量计算起点）。</summary>
		private Vector3 _originalScale = Vector3.one;

		/// <summary>由"原始位置 + 原始旋转"组成的变换矩阵（用于把相对位置变换回世界空间）。</summary>
		private Matrix4x4 _originalMatrix = Matrix4x4.identity;

		/// <summary>_originalMatrix 的逆矩阵（用于把相对位置变换到基准局部空间）。</summary>
		private Matrix4x4 _originalInverse = Matrix4x4.identity;

		/// <summary>上一帧移动工具的位置值，用于计算拖拽增量。</summary>
		private Vector3 _previousToolPosition = Vector3.zero;

		/// <summary>上一帧旋转工具的旋转值。</summary>
		private Quaternion _previousToolRotation = Quaternion.identity;

		/// <summary>上一帧缩放工具的缩放值。</summary>
		private Vector3 _previousToolScale = Vector3.one;

		/// <summary>旋转交互中已累计的旋转角度（度），供按权重插值。</summary>
		private float _currentAngle = 0;

		/// <summary>旋转交互中固定的旋转轴（世界空间）。</summary>
		private Vector3 _currentAxis = Vector3.zero;

		/// <summary>实例化 Gizmo 工具：绑定晶格与选中集合，订阅变更事件（选择变化时自动 Reset）。</summary>
		/// <param name="cube">目标晶格。</param>
		/// <param name="handles">控制点选中集合。</param>
		public HandleGizmos(DeformerCube cube, SelectedHandles handles)
		{
			_cube = cube;
			_handles = handles;
			_handles.SelectionChanged += Reset;

			Reset();
		}

		/// <summary>释放资源：取消对选中集合变更事件的订阅，避免悬挂引用。</summary>
		public void Dispose()
		{
			_handles.SelectionChanged -= Reset;
		}

		/// <summary>每帧绘制入口：MouseDown 开始操作（记录原始位置、算权重），MouseUp 结束（写 Undo 并重置）。</summary>
		public void Draw()
		{
			Event current = Event.current;

			// 鼠标按下开始、松开结束、空闲时维持重置
			if (current.type == EventType.MouseDown) OnMouseDown();
			else if (current.rawType == EventType.MouseUp) OnMouseUp();
			else if (_action == Action.None) Reset();

			// 按当前工具类型分发绘制对应手柄
			if (Tools.current == Tool.Move) DrawPositionGizmo();
			else if (Tools.current == Tool.Rotate) DrawRotationGizmo();
			else if (Tools.current == Tool.Scale) DrawScaleGizmo();

			// 每帧结束清空刷新标记
			_refresh = false;
		}

		/// <summary>重置操作状态：重算枢轴与基准旋转；开启"选中相对 Gizmo"时用 DeformSolver 求变形后朝向作手柄基准。</summary>
		public void Reset()
		{
			// 枢轴 = 选中集合中心或最后选中点（取决于 pivotMode）
			_originalPosition = _handles.GetPivot(Tools.pivotMode);
			// 基准旋转：Global 用单位旋转，Local 用晶格自身旋转
			_originalRotation = (Tools.pivotRotation == PivotRotation.Global)
				? Quaternion.identity
				: _cube.transform.rotation;
			_originalScale = Vector3.one;

			// 开启"选中相对 Gizmo"：用晶格变形估算选中中心处的表面朝向，作为手柄基准旋转
			if (DeformSettings.SelectionRelativeGizmos)
			{
				// 以选中集合"未变形位置的中心"作为求解原点
				Vector3 pivot = _handles.GetBasePivot(PivotMode.Center);

				// 临时晶格条目：单选 Cubic 平滑插值，多选 LinearSharp 快速插值
				DeformerItem item = new()
				{
					Cube = _cube,
					Interpolation = (_handles.Count > 1) 
						? InterpolationMethod.LinearSharp 
						: InterpolationMethod.Cubic,
					Global = true,
				};

				// 求解该原点经晶格变形后的变换，取其旋转作为基准旋转
				Matrix4x4 deformed = DeformSolver.DeformTransform(item, 
					Matrix4x4.TRS(pivot, _cube.transform.rotation, Vector3.one));

				_originalRotation = deformed.rotation;
			}

			// 构建基准矩阵及其逆，供旋转/缩放的局部空间变换使用
			_originalMatrix = Matrix4x4.TRS(_originalPosition, _originalRotation, Vector3.one);
			_originalInverse = _originalMatrix.inverse;

			// 上一帧工具状态与原始状态对齐，作为增量计算的起点
			_previousToolPosition = _originalPosition;
			_previousToolRotation = _originalRotation;
			_previousToolScale = _originalScale;

			// 清空动作状态与旋转累积量
			_action = Action.None;
			_currentAngle = 0;
			_currentAxis = Vector3.zero;
		}

		/// <summary>鼠标按下：重置状态、记录所有控制点原始位置快照、计算衰减权重。</summary>
		private void OnMouseDown()
		{
			Reset();

			// 保存所有控制点的原始位置快照
			_originalPositions.Resize(_cube.Resolution);
			foreach (Vector3Int coords in _cube.GetHandles())
			{
				_originalPositions[coords] = _cube.GetHandleWorldPosition(coords);
			}

			// 计算衰减权重（选中点权重为 1，周围点按距离衰减）
			CalculateWeights();
		}

		/// <summary>鼠标松开：为位移超阈值的控制点逐点记录 Undo（先还原再写新位置，实现一次拖拽一次撤销）。</summary>
		private void OnMouseUp()
		{
			if (_action != Action.None)
			{
				foreach (Vector3Int coords in _cube.GetHandles())
				{
					Vector3 originalPosition = _originalPositions[coords];
					Vector3 newPosition = _cube.GetHandleWorldPosition(coords);

					// 位移超过阈值的控制点才记录 Undo
					if (Vector3.Distance(newPosition, originalPosition) > 0.00001f)
					{
						DeformerHandle handle = _cube.GetHandle(coords);

						// 先写回原始位置并记录 Undo（保存"移动前"状态），再写新位置
						_cube.SetHandleWorldPosition(coords, originalPosition);
						Undo.RecordObject(handle, "移动晶格控制点");
						_cube.SetHandleWorldPosition(coords, newPosition);
					}
				}
			}

			Reset();
		}

		/// <summary>计算衰减权重（核心算法）：选中点权重为 1；开启衰减时按切比雪夫距离 + 线性衰减计算周围点权重，多选中点取最大。</summary>
		private void CalculateWeights()
		{
			_weights.Resize(_originalPositions.Size);

			// 选中点本身权重固定为 1（完全跟随）
			foreach (Vector3Int handle in _handles.Handles)
			{
				_weights[handle] = 1;
			}

			// 未启用衰减时，只有选中的控制点受影响
			if (!_cube.SelectionFalloffEnabled) return;

			// 读取衰减半径与影响强度（由晶格组件参数化，受曲线/滑块约束）
			int radius = _cube.GetSelectionFalloffRadius();
			float strength = _cube.SelectionFalloffStrength;

			// 对每个选中点，影响其周围全部控制点
			foreach (Vector3Int handle in _handles.Handles)
			{
				foreach (Vector3Int coords in _cube.GetHandles())
				{
					if (_weights[coords] >= 1f) continue; // 跳过选中点自身

					// 格子距离用切比雪夫距离（三轴坐标差绝对值的最大值）
					int distance = Mathf.Max(
						Mathf.Abs(coords.x - handle.x),
						Mathf.Abs(coords.y - handle.y),
						Mathf.Abs(coords.z - handle.z)
					);

					// 超出衰减半径的控制点不受影响
					if (distance > radius) continue;

					// 线性衰减：距离 0 → 1，距离 radius → 0，再乘影响强度
					float falloff = 1f - (float)distance / (radius + 1f);
					float weight = strength * falloff;

					// 多个选中点的衰减区域重叠时取最大权重
					if (weight > _weights[coords])
					{
						_weights[coords] = weight;
					}
				}
			}
		}

		/// <summary>绘制移动 Gizmo 并应用变形：手柄位移与原始位置相加得目标位置，再按权重插值（1 完全跟随、0 不动）。</summary>
		private void DrawPositionGizmo()
		{
			EditorGUI.BeginChangeCheck();

			_previousToolPosition = Handles.PositionHandle(_previousToolPosition, _originalRotation);

			// 手柄变化或移动中需要刷新时重新应用位移
			if (EditorGUI.EndChangeCheck() || ((_action == Action.Moving) && _refresh))
			{
				_action = Action.Moving;

				// 位移量（相对原始枢轴），按权重在原始位置与目标位置间插值
				Vector3 offset = _previousToolPosition - _originalPosition;

				foreach (Vector3Int coords in _cube.GetHandles())
				{
					Vector3 originalPosition = _originalPositions[coords];
					Vector3 newPosition = Vector3.Lerp(originalPosition, originalPosition + offset, _weights[coords]);
					_cube.SetHandleWorldPosition(coords, newPosition);
				}

				// 强制刷新场景视图，实时显示拖拽结果
				EditorApplication.QueuePlayerLoopUpdate();
			}
		}

		/// <summary>绘制旋转 Gizmo 并应用旋转：相对旋转转"角度+轴"，轴翻转时取补角保持连续，按权重围绕原始枢轴旋转。</summary>
		private void DrawRotationGizmo()
		{
			EditorGUI.BeginChangeCheck();

			_previousToolRotation = Handles.RotationHandle(_previousToolRotation, _originalPosition);

			if (EditorGUI.EndChangeCheck() || ((_action == Action.Rotating) && _refresh))
			{
				_action = Action.Rotating;

				float rotationAngle = 0;
				Vector3 rotationAxis = Vector3.zero;

				Quaternion relativeRotation = Quaternion.Inverse(_originalRotation) * _previousToolRotation;

				// 把相对旋转转换为"角度 + 轴"形式；首次旋转时固定旋转轴，保证插值稳定
				if (relativeRotation != Quaternion.identity)
				{
					relativeRotation.ToAngleAxis(out rotationAngle, out rotationAxis);

					if (_currentAxis == Vector3.zero) _currentAxis = rotationAxis;
				}

				// 没有旋转轴（无旋转输入）时直接返回
				if (_currentAxis == Vector3.zero) return;

				// 当前轴与固定轴方向相反时角度取补角，保持旋转方向连续（绕过 180° 临界点）
				if (Vector3.Dot(rotationAxis, _currentAxis) < 0)
				{
					rotationAngle = 360 - rotationAngle;
				}

				// 计算自上一帧的旋转增量，归一化到 [-180, 180] 后累加得到总旋转角
				float delta = rotationAngle - _currentAngle;
				delta = (((delta + 180.0f) % 360.0f) + 360.0f) % 360.0f - 180.0f;

				_currentAngle += delta;

				// 按权重旋转每个控制点：围绕原始枢轴、沿固定轴旋转加权角度
				foreach (Vector3Int coords in _cube.GetHandles())
				{
					Vector3 originalPosition = _originalPositions[coords];
					Vector3 relativePosition = originalPosition - _originalPosition;

					// 按权重插值旋转量（0 不转，1 转满角），并以原始旋转为参考系转换
					float weightedAngle = Mathf.Lerp(0, _currentAngle, _weights[coords]);
					Quaternion rotation = Quaternion.AngleAxis(weightedAngle, _currentAxis);
					rotation = _originalRotation * rotation * Quaternion.Inverse(_originalRotation);

					relativePosition = rotation * relativePosition;

					Vector3 newPosition = relativePosition + _originalPosition;
					_cube.SetHandleWorldPosition(coords, newPosition);
				}

				// 强制编辑器刷新场景视图
				EditorApplication.QueuePlayerLoopUpdate();
			}
		}

		/// <summary>绘制缩放 Gizmo 并应用缩放：相对向量经 _originalInverse/逐轴缩放/_originalMatrix 变换回世界空间，再按权重插值。</summary>
		private void DrawScaleGizmo()
		{
			EditorGUI.BeginChangeCheck();

			_previousToolScale = Handles.ScaleHandle(_previousToolScale, _originalPosition, _originalRotation);

			if (EditorGUI.EndChangeCheck() || (_action == Action.Scaling && _refresh))
			{
				_action = Action.Scaling;

				// 各轴缩放增量（当前 / 原始）
				Vector3 change = new(
					_previousToolScale.x / _originalScale.x,
					_previousToolScale.y / _originalScale.y,
					_previousToolScale.z / _originalScale.z
				);

				// 按权重缩放每个控制点：相对向量 → 基准局部空间 → 逐轴缩放 → 世界空间 → 按权重插值
				foreach (Vector3Int coords in _cube.GetHandles())
				{
					Vector3 originalPosition = _originalPositions[coords];
					Vector3 relativePosition = originalPosition - _originalPosition;

					// 变换到基准局部空间（消除枢轴旋转影响）
					relativePosition = _originalInverse.MultiplyVector(relativePosition);
					relativePosition.Scale(change);
					// 变换回世界空间
					relativePosition = _originalMatrix.MultiplyVector(relativePosition);

					Vector3 newPosition = Vector3.Lerp(originalPosition, relativePosition + _originalPosition, _weights[coords]);
					_cube.SetHandleWorldPosition(coords, newPosition);
				}

				// 强制编辑器刷新场景视图
				EditorApplication.QueuePlayerLoopUpdate();
			}
		}
	}
}
