// ============================================================================
// 文件：DeformerCubeEditor.cs
// 作用：DeformerCube 在编辑器中的自定义 Editor：绘制晶格线框与控制点，
//       左键拖拽框选（Shift 加减选）、右键上下文菜单（扩展/收缩/反向/
//       全选/重置/生效控制点/复制索引/适配晶格/偏好设置），全程支持 Undo。
// ============================================================================
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>DeformerCube 的自定义 Editor：在场景中显示晶格并提供编辑工具。</summary>
	[CustomEditor(typeof(DeformerCube))]
	public class DeformerCubeEditor : UnityEditor.Editor
	{
		/// <summary>聚焦开关：在「聚焦包围盒」与「聚焦枢轴点」两种模式间切换（每次聚焦时取反）。</summary>
		private bool _frameBounds = false;

		/// <summary>当前选中的控制点集合（带 Undo 记录的编辑器数据，保存在晶格对象上）。</summary>
		private SelectedHandles _selectedHandles;
		/// <summary>晶格盒 Gizmo 绘制器（未选中控制点时绘制晶格盒子与中心点）。</summary>
		private DeformerGizmos _cubeGizmos;
		/// <summary>晶格线框绘制器（绘制晶格控制点连线Mesh）。</summary>
		private DeformerDrawer _cubeDrawer;
		/// <summary>控制点 Gizmo 绘制器（选中控制点时绘制平移/旋转/缩放手柄）。</summary>
		private HandleGizmos _handleGizmos;
		/// <summary>控制点小球绘制器（绘制所有控制点的小球）。</summary>
		private HandleDrawer _handleDrawer;

		/// <summary>把 Editor 的目标对象转换为 DeformerCube。</summary>
		private DeformerCube Cube => target as DeformerCube;

		/// <summary>DrawGizmo 回调：Hierarchy 中选中晶格时绘制线框（唯一激活选中的晶格由 OnSceneGUI 绘制）。</summary>
		[DrawGizmo(GizmoType.InSelectionHierarchy, typeof(DeformerCube))]
		internal static void OnDrawGizmo(DeformerCube cube, GizmoType gizmoType)
		{
			// 当前激活选中（且为唯一选中）的晶格由 OnSceneGUI 绘制，避免重复
			if (gizmoType.HasFlag(GizmoType.Active) && Selection.count == 1) return;

			DeformerDrawer.Draw(cube);
		}

		#region Editor

		/// <summary>绘制 Inspector：显示选中控制点信息，并监听分辨率变化以重建晶格。</summary>
		public override void OnInspectorGUI()
		{
			Vector3Int initialResolution = Cube.Resolution;

			base.OnInspectorGUI();

			// 只读展示当前选中的控制点信息
			using (var disabled = new EditorGUI.DisabledGroupScope(true))
			{
				if (_selectedHandles.Count == 0) EditorGUILayout.LabelField("已选择：无");
				else if (_selectedHandles.Count == 1) EditorGUILayout.LabelField($"已选择：{_selectedHandles.Handles[0]}");
				else EditorGUILayout.LabelField($"已选择：共 {_selectedHandles.Count} 个");
			}

			// 分辨率被修改：重建控制点、清空选中并记录 Undo
			if (Cube.Resolution != initialResolution)
			{
				Cube.Setup(Cube.Resolution);
				Undo.RecordObject(_selectedHandles, "修改晶格分辨率");
				_selectedHandles.Clear();
				Undo.SetCurrentGroupName("修改晶格分辨率");
			}
		}

		/// <summary>Scene 视图绘制与交互入口：绘制线框、控制点，处理框选与右键菜单。</summary>
		private void OnSceneGUI()
		{
			// 始终绘制晶格线框
			_cubeDrawer.Draw();

			// 同时选中了其他对象时不显示编辑手柄，直接返回
			if (!ShouldShowEditor()) return;

			// 同步 DeformerHandleEditor 记录的手柄选择到本 Editor
			UpdateSelection();

			// 绘制控制点小球（框选进行中不绘制，避免遮挡框选矩形）
			_handleDrawer.Draw(!_selecting);

			// 有选中控制点时绘制手柄 Gizmo，否则绘制晶格盒 Gizmo
			if (_selectedHandles.Count > 0) _handleGizmos.Draw();
			else _cubeGizmos.Draw();

			// 处理框选矩形与鼠标事件（左键框选、右键菜单）
			HandleSelection();
			HandleMouseEvents();
		}

		/// <summary>Editor 激活时初始化绘制器并订阅事件。</summary>
		private void OnEnable()
		{
			// 选中集合数据保存在晶格对象上，可跨 Editor 生命周期保持
			_selectedHandles = SelectedHandles.Get(Cube);
			_cubeGizmos = new DeformerGizmos(Cube);
			_cubeDrawer = new DeformerDrawer(Cube);
			_handleGizmos = new HandleGizmos(Cube, _selectedHandles);
			_handleDrawer = new HandleDrawer(Cube, _selectedHandles);

			// 订阅事件：选中集合变化、枢轴/视图工具变化、Undo 执行、选中对象变化
			_selectedHandles.SelectionChanged += ResetFocus;
			Tools.pivotRotationChanged += ResetGizmos;
			Tools.viewToolChanged += ResetGizmos;
			Undo.undoRedoPerformed += ResetGizmos;
			Selection.selectionChanged += OnSelectionChanged;

			OnSelectionChanged();
		}

		/// <summary>Editor 停用时释放绘制器并反订阅事件。</summary>
		private void OnDisable()
		{
			_handleGizmos.Dispose();

			_selectedHandles.SelectionChanged -= ResetFocus;
			Tools.pivotRotationChanged -= ResetGizmos;
			Tools.viewToolChanged -= ResetGizmos;
			Undo.undoRedoPerformed -= ResetGizmos;
			Selection.selectionChanged -= OnSelectionChanged;

			// 恢复被隐藏的编辑器工具，避免影响其他对象
			Tools.hidden = false;
		}

		/// <summary>HasFrameBounds 协议：声明该对象支持聚焦（Frame）操作。</summary>
		private bool HasFrameBounds() => true;

		/// <summary>计算聚焦包围盒：有选中则围绕选中控制点，否则围绕全部控制点，按 _frameBounds 在包围盒/枢轴点间切换。</summary>
		private Bounds OnGetFrameBounds()
		{
			// 每次聚焦时翻转开关，实现两种聚焦方式交替
			_frameBounds = !_frameBounds;

			if (_selectedHandles.Count > 0)
			{
				if (_frameBounds) return _selectedHandles.GetBounds();
				else return new(_selectedHandles.GetPivot(Tools.pivotMode), Vector3.one);
			}
			else
			{
				// 计算全部控制点的包围盒
				Vector3 initial = Cube.GetHandleWorldPosition(0, 0, 0);
				Bounds bounds = new(initial, Vector3.zero);
				foreach (Vector3Int handle in Cube.GetHandles())
				{
					bounds.Encapsulate(Cube.GetHandleWorldPosition(handle));
				}

				Vector3 pivot = bounds.center;
				return _frameBounds ? bounds : new(pivot, Vector3.one);
			}
		}

		/// <summary>选中集合变化回调：重置聚焦开关并刷新 Inspector。</summary>
		private void ResetFocus()
		{
			_frameBounds = false;
			Repaint();
		}

		/// <summary>重置所有 Gizmo 绘制器（枢轴/视图工具变化或执行 Undo 时调用）。</summary>
		private void ResetGizmos()
		{
			_cubeGizmos.Reset();
			_handleGizmos.Reset();
		}

		/// <summary>选择变化回调：按是否显示编辑器，隐藏/显示 Unity 内置工具。</summary>
		private void OnSelectionChanged()
		{
			Tools.hidden = ShouldShowEditor();
		}

		/// <summary>仅当场景中恰好选中 1 个对象（即本晶格）时显示编辑手柄。</summary>
		private bool ShouldShowEditor()
		{
			return Selection.count == 1;
		}

		/// <summary>把 DeformerHandleEditor 记录的被点选手柄转为晶格坐标并入选中集合。</summary>
		private void UpdateSelection()
		{
			if (DeformerHandleEditor.Selected.Length > 0)
			{
				Undo.RecordObject(_selectedHandles, "选择晶格控制点");
				_selectedHandles.Clear();
				_selectedHandles.AddRange(DeformerHandleEditor.Selected.Select(h => Cube.GetHandleCoords(h)));
				// 清空缓存，防止重复处理
				DeformerHandleEditor.Selected = System.Array.Empty<DeformerHandle>();
			}
		}

		#endregion

		#region Mouse Events

		/// <summary>六个轴向单位向量：用于"扩展/收缩选择"时判断相邻控制点。</summary>
		private static readonly Vector3Int[] Directions = new[]
		{
			Vector3Int.right, Vector3Int.left,
			Vector3Int.up, Vector3Int.down,
			Vector3Int.forward, Vector3Int.back,
		};

		/// <summary>框选矩形填充色（半透明蓝色）。</summary>
		private static readonly Color SelectionFaceColor = new(0.1f, 0.4f, 1f, 0.05f);
		/// <summary>框选矩形描边色（蓝色）。</summary>
		private static readonly Color SelectionOutlineColor = new(0.1f, 0.4f, 1f, 0.2f);

		/// <summary>是否正在拖拽框选。</summary>
		private bool _selecting = false;
		/// <summary>框选起始的屏幕坐标（GUI 坐标）。</summary>
		private Vector2 _selectingStartPos = Vector2.zero;
		/// <summary>当前框选矩形内包含的控制点坐标集合（框选期间每帧重算）。</summary>
		private readonly HashSet<Vector3Int> _handlesWithinSelection = new();

		/// <summary>框选处理：构建选择矩形、收集矩形内控制点并绘制半透明矩形。</summary>
		private void HandleSelection()
		{
			if (_selecting)
			{
				Rect selectionRect = Rect.MinMaxRect(
					_selectingStartPos.x,
					_selectingStartPos.y,
					Event.current.mousePosition.x,
					Event.current.mousePosition.y
				);

				// 重新计算落在矩形内的控制点（世界坐标转 GUI 屏幕坐标后判断）
				_handlesWithinSelection.Clear();
				foreach (Vector3Int handle in Cube.GetHandles())
				{
					Vector3 handlePosition = Cube.GetHandleWorldPosition(handle);
					Vector2 guiPosition = HandleUtility.WorldToGUIPoint(handlePosition);

					if (selectionRect.Contains(guiPosition, true))
					{
						_handlesWithinSelection.Add(handle);
					}
				}

				// 绘制选择矩形的填充与描边
				Handles.BeginGUI();
				Handles.DrawSolidRectangleWithOutline(selectionRect, SelectionFaceColor, SelectionOutlineColor);
				Handles.EndGUI();

				// 请求玩家循环更新，保证框选过程中视图实时刷新
				EditorApplication.QueuePlayerLoopUpdate();
			}
		}

		/// <summary>鼠标事件处理：左键框选开始/结束、右键上下文菜单触发、鼠标移动刷新视图。</summary>
		private void HandleMouseEvents()
		{
			// 鼠标移动时刷新 Scene 视图（使控制点高亮等效果实时更新）
			if (Event.current.type == EventType.MouseMove) SceneView.RepaintAll();

			// 注册为默认控件，使未命中其他控件的鼠标事件仍可被处理
			Event current = Event.current;
			int controlId = GUIUtility.GetControlID(FocusType.Passive);
			HandleUtility.AddDefaultControl(controlId);

			// 左键：按下开始框选，抬起结束框选
			if (current.button == 0)
			{
				// 左键按下（未按住 Alt）时开始框选，记录框选起点
				if (current.GetTypeForControl(controlId) == EventType.MouseDown && !current.alt)
				{
					GUIUtility.hotControl = controlId;

					_selecting = true;
					_selectingStartPos = current.mousePosition;
					current.Use();
				}

				if (current.GetTypeForControl(controlId) == EventType.MouseUp && _selecting)
				{
					GUIUtility.hotControl = 0;

					// 框选为空、之前无选中且未按住 Shift/Alt：视为"空白点击"，取消对象选择
					if (_handlesWithinSelection.Count == 0 && _selectedHandles.Count == 0 && !current.shift && !current.alt)
					{
						Selection.activeGameObject = null;
						_selecting = false;
						current.Use();
						return;
					}

					// 按住 Shift：与当前选择做对称差集（新增未选中的、移除已选中的）
					if (current.shift)
					{
						_handlesWithinSelection.SymmetricExceptWith(_selectedHandles.Handles);
					}

					// 记录 Undo 后用框选结果替换当前选择
					Undo.RecordObject(_selectedHandles, "选择晶格控制点");
					_selectedHandles.Clear();
					_selectedHandles.AddRange(_handlesWithinSelection);
					_selecting = false;
					current.Use();
				}
			}

		}

		#endregion
	}
}
