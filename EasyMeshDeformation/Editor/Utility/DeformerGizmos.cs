// ============================================================================
// DeformerGizmos.cs
// 概述：为整个晶格提供移动/旋转/缩放操作手柄（支持 Global/Local 与 Undo）。
// 旋转/缩放通过"上一帧工具状态"计算增量（change）再复合到 Transform，
// 保证操作稳定不漂移。注意：本类操作晶格本身，HandleGizmos 操作内部控制点。
// ============================================================================
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>为晶格整体绘制移动、旋转、缩放操作 Gizmo 的工具类（按当前工具分发，操作后记录 Undo）。</summary>
	public class DeformerGizmos
	{
		/// <summary>被操作的晶格对象。</summary>
		private readonly DeformerCube _cube;

		/// <summary>上一帧旋转工具的旋转值，用于计算本次旋转增量。</summary>
		private Quaternion _previousToolRotation = Quaternion.identity;

		/// <summary>上一帧缩放工具的缩放值，用于计算本次缩放增量。</summary>
		private Vector3 _previousToolScale = Vector3.one;

		/// <summary>实例化 Gizmo 工具并绑定目标晶格，同时初始化上一帧工具状态。</summary>
		/// <param name="cube">要操作的目标晶格。</param>
		public DeformerGizmos(DeformerCube cube)
		{
			_cube = cube;
			Reset();
		}

		/// <summary>重置工具状态：缩放恢复为 1，旋转按枢轴设置取单位四元数（Global）或晶格自身旋转（Local）。</summary>
		public void Reset()
		{
			_previousToolScale = Vector3.one;
			_previousToolRotation = (Tools.pivotRotation == PivotRotation.Global)
				? Quaternion.identity
				: _cube.transform.rotation;
		}

		/// <summary>每帧绘制入口：按当前工具分派 Gizmo；鼠标释放时重置状态，避免增量残留。</summary>
		public void Draw()
		{
			// 鼠标松开时重置，保证下次操作从干净状态开始
			if (Event.current.type == EventType.MouseUp) Reset();

			if (Tools.current == Tool.Move) DrawPositionGizmo();
			else if (Tools.current == Tool.Rotate) DrawRotationGizmo();
			else if (Tools.current == Tool.Scale) DrawScaleGizmo();
		}

		/// <summary>绘制移动 Gizmo：显示移动手柄，位置变化时记录 Undo 并写回晶格 Transform。</summary>
		private void DrawPositionGizmo()
		{
			// Global 用世界轴，Local 用晶格自身旋转
			Quaternion rotation = (Tools.pivotRotation == PivotRotation.Global)
				? Quaternion.identity
				: _cube.transform.rotation;

			EditorGUI.BeginChangeCheck();

			Vector3 finalPosition = Handles.PositionHandle(_cube.transform.position, rotation);

			// 位置变化时记录 Undo 并应用
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(_cube.transform, "移动晶格");
				_cube.transform.position = finalPosition;
			}
		}

		/// <summary>绘制旋转 Gizmo：用"上一帧到当前"的旋转增量施加到 Transform——Global 左乘、Local 右乘，避免漂移。</summary>
		private void DrawRotationGizmo()
		{
			EditorGUI.BeginChangeCheck();

			Quaternion finalRotation = Handles.RotationHandle(_previousToolRotation, _cube.transform.position);

			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(_cube.transform, "旋转晶格");

				// 计算本次拖拽产生的相对旋转增量（上一帧的逆 × 当前值）
				Quaternion change = Quaternion.Inverse(_previousToolRotation) * finalRotation;
				_previousToolRotation = finalRotation;

				// Global：增量左乘（世界空间）；Local：增量右乘（局部空间）
				if (Tools.pivotRotation == PivotRotation.Global)
				{
					_cube.transform.rotation = change * _cube.transform.rotation;
				}
				else
				{
					_cube.transform.rotation = _cube.transform.rotation * change;
				}
			}
		}

		/// <summary>绘制缩放 Gizmo：用"当前/上一帧"的缩放增量逐轴乘到 localScale 上。</summary>
		private void DrawScaleGizmo()
		{
			EditorGUI.BeginChangeCheck();

			Vector3 finalScale = Handles.ScaleHandle(_previousToolScale, _cube.transform.position, _cube.transform.rotation);

			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(_cube.transform, "缩放晶格");

				// 计算各轴缩放增量（当前/上一帧），逐轴乘到局部缩放上
				Vector3 change = new(
					finalScale.x / _previousToolScale.x,
					finalScale.y / _previousToolScale.y,
					finalScale.z / _previousToolScale.z
				);
				_previousToolScale = finalScale;

				Vector3 scale = _cube.transform.localScale;
				scale.Scale(change);
				_cube.transform.localScale = scale;
			}
		}
	}
}
