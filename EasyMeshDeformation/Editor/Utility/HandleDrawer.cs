// ============================================================================
// HandleDrawer.cs
// 概述：控制点绘制器：绘制全部控制点圆点（选中高亮、未生效置灰，
// 深度排序 + zTest 两段绘制实现遮挡感）；并注册隐形点击按钮，
// Shift 切换、普通点击单选，支持 Undo。
// ============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EasyMeshDeformation.Editor
{
	/// <summary>在 Scene 视图中绘制晶格控制点的工具类：视觉绘制（高亮/置灰）与点击交互（选择）。</summary>
	public class HandleDrawer
	{
		/// <summary>被绘制的晶格对象（提供控制点位置与分辨率等数据）。</summary>
		private readonly DeformerCube _cube;

		/// <summary>选中控制点集合（用于查询选中状态与修改选择）。</summary>
		private readonly SelectedHandles _selectedHandles;

		/// <summary>控制点缓存列表：SortHandles 时重建并按深度排序，复用列表避免每帧分配。</summary>
		private readonly List<Handle> _handles = new();

		/// <summary>实例化绘制器并绑定晶格与选中集合。</summary>
		/// <param name="cube">目标晶格。</param>
		/// <param name="handles">控制点选中集合。</param>
		public HandleDrawer(DeformerCube cube, SelectedHandles handles)
		{
			_cube = cube;
			_selectedHandles = handles;
		}

		/// <summary>每帧绘制入口。</summary>
		/// <param name="drawButtons">是否绘制点击交互按钮（一般仅对选中晶格为 true）。</param>
		public void Draw(bool drawButtons)
		{
			// Layout 阶段重建控制点列表并按深度排序
			if (Event.current.type == EventType.Layout)
			{
				SortHandles();
			}
			// Repaint 阶段：先画被遮挡的半透明圆点，再画可见的清晰圆点
			else if (Event.current.type == EventType.Repaint)
			{
				DrawHandles(0.5f, CompareFunction.Greater);
				DrawHandles(1f, CompareFunction.LessEqual);
			}

			// 需要交互时为每个控制点注册透明的点击按钮
			if (drawButtons) DrawButtons();
		}

		/// <summary>重建控制点列表并按深度排序（画家算法）：按位置在相机前方向上的投影深度降序排列。</summary>
		private void SortHandles()
		{
			_handles.Clear();

			// 记录全部控制点的索引与世界坐标
			for (int i = 0; i < _cube.Resolution.x; i++)
			{
				for (int j = 0; j < _cube.Resolution.y; j++)
				{
					for (int k = 0; k < _cube.Resolution.z; k++)
					{
						Vector3Int index = new(i, j, k);
						_handles.Add(new(index, _cube.GetHandleWorldPosition(index)));
					}
				}
			}

			// 深度比较器：按点在相机前方向上的投影深度排序（放大 100 倍取整，减小浮点噪声）
			static int CompareDepth(Handle a, Handle b)
			{
				Vector3 forward = Camera.current.transform.forward;
				float depthA = Vector3.Dot(forward, a.Position);
				float depthB = Vector3.Dot(forward, b.Position);
				float difference = 100 * (depthB - depthA);
				return (int)difference;
			}

			_handles.Sort(CompareDepth);
		}

		/// <summary>以指定透明度与深度测试函数绘制全部控制点圆点（屏幕尺寸由 HandleSize 与距离换算决定，近大远小但占比恒定）。</summary>
		/// <param name="alpha">圆点透明度。</param>
		/// <param name="compareFunction">深度测试函数（Greater=被遮挡层，LessEqual=可见层）。</param>
		private void DrawHandles(float alpha, CompareFunction compareFunction)
		{
			using Handles.DrawingScope drawingScope = new(Matrix4x4.identity);

			// 临时切换深度测试函数，绘制后恢复
			CompareFunction previousZTest = Handles.zTest;
			Handles.zTest = compareFunction;

			float handleSize = DeformSettings.HandleSize * 0.01f;

			for (int i = 0; i < _handles.Count; i++)
			{
				Handle handle = _handles[i];

				// 按到相机的距离换算屏幕大小（近大远小但屏幕占比恒定）
				float size = handleSize * HandleUtility.GetHandleSize(handle.Position);

				// 选中高亮色，未选中用普通颜色
				Color color = _selectedHandles.Contains(handle.Index)
					? DeformSettings.HandleColorSelected
					: DeformSettings.HandleColor;

				// 仅使用生效控制点时，未生效的控制点显示为灰色
				if (_cube.UseEnabledHandlesOnly && !_cube.EnabledHandles.Contains(handle.Index))
				{
					color = Color.gray;
				}
				// 叠加分层透明度（被遮挡层更淡）
				color.a *= alpha;

				using (new Handles.DrawingScope(color))
					Handles.DotHandleCap(0, handle.Position, Quaternion.identity, size, Event.current.type);
			}

			Handles.zTest = previousZTest;
		}

		/// <summary>为每个控制点注册透明点击按钮：普通点击单选、Shift 点击切换；Always 深度测试保证任意遮挡下可点击。</summary>
		private void DrawButtons()
		{
			// 按钮不可见，只提供点击命中
			using Handles.DrawingScope drawingScope = new(Color.clear, Matrix4x4.identity);

			// 深度测试设为 Always，保证被遮挡的控制点也能被点击到
			CompareFunction previousZTest = Handles.zTest;
			Handles.zTest = CompareFunction.Always;

			float handleSize = DeformSettings.HandleSize * 0.01f;

			for (int i = 0; i < _handles.Count; i++)
			{
				Handle handle = _handles[i];
				// 按钮命中半径略大于显示半径，方便点击
				float size = handleSize * HandleUtility.GetHandleSize(handle.Position);

				if (Handles.Button(handle.Position, Quaternion.identity, size, size * 1.5f, Handles.DotHandleCap))
				{
					// 记录 Undo，支持撤销选择操作
					Undo.RecordObject(_selectedHandles, "选择晶格控制点");
					// Shift：切换该点选中状态（已选取消，未选加入）
					if (Event.current.shift)
					{
						if (_selectedHandles.Contains(handle.Index)) _selectedHandles.Remove(handle.Index);
						else _selectedHandles.Add(handle.Index);
					}
					// 非 Shift：清空后单选当前点
					else
					{
						_selectedHandles.Clear();
						_selectedHandles.Add(handle.Index);
					}
				}
			}

			Handles.zTest = previousZTest;
		}

		/// <summary>控制点数据结构：晶格内的三维索引与其世界空间位置。</summary>
		struct Handle
		{
			/// <summary>控制点在晶格中的三维索引。</summary>
			public Vector3Int Index;

			/// <summary>控制点的世界空间位置。</summary>
			public Vector3 Position;

			/// <summary>构造一个控制点数据。</summary>
			public Handle(Vector3Int index, Vector3 position)
			{
				Index = index;
				Position = position;
			}
		}
	}
}
