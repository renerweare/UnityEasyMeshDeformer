// ============================================================================
// 文件：DeformerCubeEditor.cs
// 作用：DeformerCube 在编辑器中的自定义 Editor：绘制晶格线框与控制点，
//       左键拖拽框选（Shift 加减选）、右键上下文菜单（扩展/收缩/反向/
//       全选/重置/生效控制点/复制索引/适配晶格/偏好设置），全程支持 Undo。
// ============================================================================
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Search;
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

		/// <summary>菜单入口（GameObject/Effects/DeformerCube）：创建新晶格，并自动绑定到选中的变形器。</summary>
		[MenuItem("GameObject/Effects/DeformerCube", false, 4040)]
		private static void CreateDeformerCube(MenuCommand menuCommand)
		{
			// 右键菜单上下文与当前激活对象不一致（多选等）时中止，避免误操作
			if ((menuCommand.context != null) && (menuCommand.context != Selection.activeGameObject))
				return;

			// 新建晶格根对象并注册 Undo
			GameObject gameObject = new("DeformerCube");
			Undo.RegisterCreatedObjectUndo(gameObject, "创建晶格");

			// 仅选中一个对象时：作为其子物体并归零本地变换
			if (Selection.gameObjects.Length == 1)
			{
				Undo.SetTransformParent(gameObject.transform, Selection.activeTransform, "创建晶格");
				gameObject.transform.localPosition = Vector3.zero;
				gameObject.transform.localRotation = Quaternion.identity;
				gameObject.transform.localScale = Vector3.one;
			}
			// 多选或空选：放到最近活动 Scene 视图的观察中心
			else if (SceneView.lastActiveSceneView != null)
			{
				gameObject.transform.localPosition = SceneView.lastActiveSceneView.pivot;
			}

			// 添加 DeformerCube 组件并按默认分辨率初始化控制点
			Undo.RegisterCompleteObjectUndo(gameObject, "创建晶格");
			DeformerCube cube = Undo.AddComponent<DeformerCube>(gameObject);
			cube.Setup(cube.Resolution);

			// 局部函数：把新晶格追加到变形器晶格列表（沿用最后一项的插值方式；
			// 若最后一项无晶格引用则直接替换，避免留下空白条目）
			void AddDeformerCubeToList(List<DeformerItem> items)
			{
				DeformerItem item = new() { Cube = cube };

				if (items.Count > 0)
				{
					DeformerItem previous = items[^1];
					item.Interpolation = previous.Interpolation;

					if (previous.Cube == null)
					{
						items[^1] = item;
						return;
					}
				}

				items.Add(item);
			}

			// 把新晶格绑定到所有当前选中对象上的 MeshDeformerBase 变形器
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				if (Selection.gameObjects[i].TryGetComponent(out MeshDeformerBase modifier))
				{
					Undo.RegisterCompleteObjectUndo(modifier, "创建晶格");
					AddDeformerCubeToList(modifier.Items);
				}
			}

			// 合并 Undo 分组，让整次创建操作作为一步可撤销
			Undo.SetCurrentGroupName("创建晶格");

			// 创建完成后选中新晶格
			Selection.activeTransform = gameObject.transform;
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

		/// <summary>右键按下的屏幕坐标：用于判断右键是否在相同位置抬起（区分"点击"与"拖拽"）。</summary>
		private Vector2 _rightMouseDownPos = Vector2.zero;

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

			// 右键：按下记录位置，抬起位置相同时弹出上下文菜单（区分"点击"与"拖拽"）
			if (current.button == 1)
			{
				if (current.GetTypeForControl(controlId) == EventType.MouseDown)
				{
					_rightMouseDownPos = current.mousePosition;
				}

				if (current.GetTypeForControl(controlId) == EventType.MouseUp)
				{
					// 抬起位置与按下位置相同（"点击"而非"拖拽"）时弹出菜单
					if (current.mousePosition == _rightMouseDownPos)
					{
						ShowContextMenu();
						current.Use();
					}
				}
			}
		}

		/// <summary>把控制点坐标钳制到有效范围（0 ~ Resolution-1），用于扩展/收缩选择时访问边界外坐标。</summary>
		private Vector3Int ClampHandle(Vector3Int handle)
		{
			return Vector3Int.Min(Vector3Int.Max(handle, Vector3Int.zero), Cube.Resolution - Vector3Int.one);
		}

		/// <summary>弹出右键上下文菜单：选择操作（扩展/收缩/反向/全选/外部）、重置、生效控制点、复制索引、适配晶格、偏好设置。</summary>
		private void ShowContextMenu()
		{
			GenericMenu menu = new();

			// 「使用选择衰减」：开关型菜单项，勾选状态读自当前晶格
			menu.AddItem(new GUIContent("使用选择衰减"), Cube.SelectionFalloffEnabled, () =>
			{
				Cube.SelectionFalloffEnabled = !Cube.SelectionFalloffEnabled;
			});

			// 「使用选择相对 Gizmo」：开关型菜单项，勾选状态读自全局设置
			menu.AddItem(new GUIContent("使用选择相对 Gizmo"), DeformSettings.SelectionRelativeGizmos, () =>
			{
				DeformSettings.SelectionRelativeGizmos = !DeformSettings.SelectionRelativeGizmos;
			});

			menu.AddSeparator("");

			// 扩展/收缩选择：无选中控制点时置灰
			if (_selectedHandles.Count == 0)
			{
				menu.AddDisabledItem(new GUIContent("扩展选择"));
				menu.AddDisabledItem(new GUIContent("收缩选择"));
			}
			else
			{
				// 「扩展选择」：把与当前选中控制点相邻（六方向）的控制点加入选择
				menu.AddItem(new GUIContent("扩展选择"), false, () =>
				{
					Undo.RecordObject(_selectedHandles, "扩展选择");
					List<Vector3Int> handlesToAdd = new();
					foreach (Vector3Int handle in Cube.GetHandles())
					{
						// 若某个方向的相邻控制点已被选中，则把该控制点也加入待添加列表
						if (Directions.Any(direction => _selectedHandles.Contains(ClampHandle(handle + direction))))
						{
							handlesToAdd.Add(handle);
						}
					}

					// 统一添加（避免在遍历过程中修改集合）
					foreach (Vector3Int handle in handlesToAdd)
					{
						_selectedHandles.Add(handle);
					}
				});

				// 「收缩选择」：把不是"所有六方向相邻点都被选中"的控制点移出选择
				menu.AddItem(new GUIContent("收缩选择"), false, () =>
				{
					Undo.RecordObject(_selectedHandles, "收缩选择");
					List<Vector3Int> handlesToRemove = new();
					foreach (Vector3Int handle in Cube.GetHandles())
					{
						// 只要有一个方向的相邻控制点未被选中，就把它移出选择（收缩选择边界）
						if (!Directions.All(direction => _selectedHandles.Contains(ClampHandle(handle + direction))))
						{
							handlesToRemove.Add(handle);
						}
					}

					// 统一移除（避免在遍历过程中修改集合）
					foreach (Vector3Int handle in handlesToRemove)
					{
						_selectedHandles.Remove(handle);
					}
				});
			}

			// 「反向选择」：取消已选中的控制点，选中未选中的控制点
			menu.AddItem(new GUIContent("反向选择"), false, () =>
			{
				Undo.RecordObject(_selectedHandles, "反向选择");
				foreach (Vector3Int handle in Cube.GetHandles())
				{
					if (_selectedHandles.Contains(handle)) _selectedHandles.Remove(handle);
					else _selectedHandles.Add(handle);
				}
			});

			menu.AddSeparator("");

			// 「选择全部控制点」：把晶格所有控制点加入选择
			menu.AddItem(new GUIContent("选择全部控制点"), false, () =>
			{
				Undo.RecordObject(_selectedHandles, "选择全部控制点");
				foreach (Vector3Int handle in Cube.GetHandles())
				{
					_selectedHandles.Add(handle);
				}
			});

			// 「选择外部控制点」：只选择位于晶格表面（任一轴坐标为 0 或最大值）的控制点
			menu.AddItem(new GUIContent("选择外部控制点"), false, () =>
			{
				Undo.RecordObject(_selectedHandles, "选择外部控制点");
				foreach (Vector3Int handle in Cube.GetHandles())
				{
					if (handle.x == 0 || handle.y == 0 || handle.z == 0 ||
						(handle.x == (Cube.Resolution.x - 1)) ||
						(handle.y == (Cube.Resolution.y - 1)) ||
						(handle.z == (Cube.Resolution.z - 1)))
					{
						_selectedHandles.Add(handle);
					}
				}
			});

			menu.AddSeparator("");

			// 重置选中的控制点：无选中时置灰
			if (_selectedHandles.Count == 0)
			{
				menu.AddDisabledItem(new GUIContent("重置选中的控制点"));
			}
			else
			{
				// 「重置选中的控制点」：把所有选中控制点的偏移归零（恢复初始位置）
				menu.AddItem(new GUIContent("重置选中的控制点"), false, () =>
				{
					foreach (Vector3Int handle in _selectedHandles.Handles)
					{
						// 对每个控制点分别记录 Undo，再清零其偏移
						Undo.RecordObject(Cube.GetHandle(handle), "重置控制点");
						Cube.SetHandleOffset(handle, Vector3.zero);
					}
					_handleGizmos.Reset();
					EditorApplication.QueuePlayerLoopUpdate();
				});
			}

			// 「重置全部控制点」：把晶格所有控制点的偏移归零
			menu.AddItem(new GUIContent("重置全部控制点"), false, () =>
			{
				foreach (Vector3Int handle in Cube.GetHandles())
				{
					Undo.RecordObject(Cube.GetHandle(handle), "重置全部控制点");
					Cube.SetHandleOffset(handle, Vector3.zero);
				}
				_handleGizmos.Reset();
				EditorApplication.QueuePlayerLoopUpdate();
			});

			menu.AddSeparator("");

			// 「将选中的控制点设为生效」：把选中控制点写入 EnabledHandles，限制只对特定区域产生变形
			menu.AddItem(new GUIContent("将选中的控制点设为生效"), false, () =>
			{
				Undo.RecordObject(Cube, "设为生效控制点");
				Cube.EnabledHandles.Clear();
				Cube.EnabledHandles.AddRange(_selectedHandles.Handles);
				EditorUtility.SetDirty(Cube);
				EditorApplication.QueuePlayerLoopUpdate();
			});

			// 「清除全部生效控制点」：清空生效控制点列表
			menu.AddItem(new GUIContent("清除全部生效控制点"), false, () =>
			{
				Undo.RecordObject(Cube, "清除生效控制点");
				Cube.EnabledHandles.Clear();
				EditorUtility.SetDirty(Cube);
				EditorApplication.QueuePlayerLoopUpdate();
			});

			// 「仅使用生效控制点」：开关型菜单项，切换是否忽略未生效的控制点
			menu.AddItem(new GUIContent("仅使用生效控制点"), Cube.UseEnabledHandlesOnly, () =>
			{
				Undo.RecordObject(Cube, "切换仅使用生效控制点");
				Cube.UseEnabledHandlesOnly = !Cube.UseEnabledHandlesOnly;
				EditorUtility.SetDirty(Cube);
				EditorApplication.QueuePlayerLoopUpdate();
			});

			menu.AddSeparator("");

			// 复制单个选中控制点的索引：仅选中 1 个时可用
			if (_selectedHandles.Count != 1)
			{
				menu.AddDisabledItem(new GUIContent("复制选中的索引"));

			}
			else
			{
				Vector3Int handle = _selectedHandles.Handles[0];
				// 「复制选中的索引」：以 Vector3(x,y,z) 文本复制到系统剪贴板
				menu.AddItem(new GUIContent("复制选中的索引"), false, () =>
				{
					EditorGUIUtility.systemCopyBuffer = $"Vector3({handle.x},{handle.y},{handle.z})";
					Debug.Log($"已复制选中的控制点索引：{handle}\n" +
						"可粘贴到 Inspector 中的 Vector3 或 Vector3Int 字段。");
				});
			}

			// 复制多个选中控制点的索引：以 GenericPropertyJSON 格式复制，
			// 可直接粘贴到 Inspector 的 Vector3Int 数组/列表字段
			if (_selectedHandles.Count == 0)
			{
				menu.AddDisabledItem(new GUIContent("复制选中的索引（多个）"));
			}
			else
			{
				menu.AddItem(new GUIContent("复制选中的索引（多个）"), false, () =>
				{
					int count = _selectedHandles.Count;

					// 拼接 JSON：先写数组大小字段
					string values = $"{{" +
						$"\"name\":\"size\"," +
						$"\"type\":12," +
						$"\"val\":{count}" +
					$"}},";

					// 同时准备调试日志文本
					string debug = $"已复制 {count} 个选中的控制点索引。\n" +
						"可粘贴到 Inspector 中的 Vector3 或 Vector3Int 列表。\n" +
						"以下为选中的索引：\n";

					// 逐个控制点拼接 JSON 数据项（type 21 = 字符串形式的 Vector3）
					foreach (Vector3Int handle in _selectedHandles.Handles)
					{
						values += $"{{" +
							$"\"name\":\"data\"," +
							$"\"type\":21," +
							$"\"val\":\"Vector3({handle.x},{handle.y},{handle.z})\"" +
						$"}},";

						debug += $"{handle}\n";
					}

					// 去掉末尾多余的逗号
					values = values[..^1];
					debug = debug[..^1];

					// 组装成 Unity 可识别的属性粘贴格式（GenericPropertyJSON）
					string property = $"GenericPropertyJSON:{{" +
						$"\"name\":\"_indices\"," +
						$"\"type\":-1," +
						$"\"arraySize\":{count}," +
						$"\"arrayType\":\"Vector3Int\"," +
						$"\"children\":[" +
							$"{{" +
								$"\"name\":\"Array\"," +
								$"\"type\":-1," +
								$"\"arraySize\":{count}," +
								$"\"arrayType\":\"Vector3Int\"," +
								$"\"children\":[{values}]" +
							$"}}" +
						$"]" +
					$"}}";

					// 写入系统剪贴板并输出日志
					EditorGUIUtility.systemCopyBuffer = property;
					Debug.Log(debug);
				});
			}

			menu.AddSeparator("");

			// 适配晶格：根据渲染器或 Transform 调整晶格的位置/旋转/缩放
			FitToTransformMenuItem(menu);

			menu.AddSeparator("");

			// 「晶格偏好设置...」：打开 DeformSettings 的偏好设置窗口
			menu.AddItem(new GUIContent("晶格偏好设置..."), false, DeformSettings.OpenPreferences);

			// 以上下文菜单形式弹出
			menu.ShowAsContext();
		}

		/// <summary>添加「适配到渲染器... / 适配到 Transform...」菜单项（通过对象选择器选择目标）。</summary>
		private void FitToTransformMenuItem(GenericMenu menu)
		{
			// 记录适配前的晶格本地变换，用于取消适配时还原
			Transform transform = Cube.transform;

			Vector3 originalPosition = transform.localPosition;
			Quaternion originalRotation = transform.localRotation;
			Vector3 originalScale = transform.localScale;

			// 适配过程中是否确实发生过修改（用于 Undo 判断）
			bool changed = false;

			// 把晶格变换还原为适配前的状态
			void ResetTransform()
			{
				transform.localPosition = originalPosition;
				transform.localRotation = originalRotation;
				transform.localScale = originalScale;
			}

			// 对象选择器关闭（确认或取消）时的回调：适配生效则记录 Undo，否则还原
			void OnSelection(Object obj, bool canceled)
			{
				Vector3 newPosition = transform.localPosition;
				Quaternion newRotation = transform.localRotation;
				Vector3 newScale = transform.localScale;

				ResetTransform();

				if (changed)
				{
					Undo.RecordObject(transform, "适配晶格");
					transform.localPosition = newPosition;
					transform.localRotation = newRotation;
					transform.localScale = newScale;
				}
			}

			// 「适配到渲染器...」：选择任意 Renderer，让晶格包裹其 Transform
			menu.AddItem(new GUIContent("适配到渲染器..."), false, () =>
			{
				// 选择器预览回调：选到 Renderer 就实时调整，否则还原
				void OnChange(Object obj)
				{
					if (obj is Renderer target)
					{
						Cube.FitToTransform(target.transform, false);
						changed = true;
					}
					else
					{
						ResetTransform();
						changed = false;
					}
				}

				// 打开对象选择器（"h:" 过滤 Hierarchy 中的对象，类型限定为 Renderer）
				SearchService.ShowObjectPicker(OnSelection, OnChange, "h:", "", typeof(Renderer));
			});

			// 「适配到 Transform...」：选择任意 Transform，让晶格包裹其包围盒（含子物体）
			menu.AddItem(new GUIContent("适配到 Transform..."), false, () =>
			{
				// 选择器预览回调：实时预览适配效果
				void OnChange(Object obj)
				{
					if (obj is Transform target)
					{
						Cube.FitToTransform(target, true);
						changed = true;
					}
					else
					{
						ResetTransform();
						changed = false;
					}
				}

				// 打开对象选择器（"h: is:visible" 过滤 Hierarchy 中可见对象，类型限定为 Transform）
				SearchService.ShowObjectPicker(OnSelection, OnChange, "h: is:visible", "", typeof(Transform));
			});
		}

		#endregion
	}
}
