// ============================================================================
// TransformDeformer.cs —— Transform 变形器（独立脚本）
// 对目标 Transform 的位置 / 旋转 / 缩放应用晶格变形：
// 取目标矩阵 → 依次应用每个晶格项的变形（DeformSolver.DeformTransform）→
// 按 ApplyFlags 把变形后的分量写回自身。每帧 LateUpdate 执行。
// 相当于原版 TransformLatticeModifier，但独立于 MeshDeformer 的Mesh模式。
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// Transform 变形器：把目标 Transform 的位姿经晶格变形后应用到自身。
	/// </summary>
	[ExecuteAlways]
	public class TransformDeformer : MonoBehaviour
	{
		/// <summary>要应用的分量（位置 / 旋转 / 缩放）。</summary>
		[Flags]
		public enum ApplyFlags
		{
			Position = 1,
			Rotation = 2,
			Scale = 4,
		}

		[SerializeField, Tooltip("要参照并变形的 Transform（其原点应落在晶格内）。")]
		private Transform _targetTransform;

		[SerializeField, Tooltip("要应用的分量：位置 / 旋转 / 缩放。默认应用位置与旋转。")]
		private ApplyFlags _applyMethod = ApplyFlags.Position | ApplyFlags.Rotation;

		[SerializeField, Tooltip("应用于目标 Transform 的晶格项，按顺序依次施加变形。")]
		private List<DeformerItem> _items = new()
		{
			new DeformerItem()
			{
				Mask = { Vertex = { Type = DeformerMask.VertexSettings.MaskType.Constant, Multiplier = 1f } }
			}
		};

		/// <summary>要变形的目标 Transform。</summary>
		public Transform TargetTransform
		{
			get => _targetTransform;
			set => _targetTransform = value;
		}

		/// <summary>要应用的分量（位置 / 旋转 / 缩放）。</summary>
		public ApplyFlags ApplyMethod
		{
			get => _applyMethod;
			set => _applyMethod = value;
		}

		/// <summary>应用的晶格项列表。</summary>
		public List<DeformerItem> Items => _items;

		private void LateUpdate()
		{
			Apply();
		}

		/// <summary>
		/// 执行一次变形：取目标矩阵 → 逐晶格变形 → 按分量写回自身 Transform。
		/// </summary>
		public void Apply()
		{
			if (_targetTransform == null)
				return;

			Matrix4x4 matrix = _targetTransform.localToWorldMatrix;

			// 依次应用所有启用的晶格（按 Items 顺序累积变形）
			for (int i = 0; i < _items.Count; i++)
			{
				DeformerCube cube = _items[i].Cube;
				if ((cube == null) || !cube.isActiveAndEnabled)
					continue;

				matrix = DeformSolver.DeformTransform(_items[i], matrix);
			}

			// 位置：开启 Position 时用变形后的位置，否则沿用目标
			if (_applyMethod.HasFlag(ApplyFlags.Position)) transform.position = matrix.GetPosition();
			else transform.position = _targetTransform.position;

			// 旋转：开启 Rotation 时用变形后的旋转，否则沿用目标
			if (_applyMethod.HasFlag(ApplyFlags.Rotation)) transform.rotation = matrix.rotation;
			else transform.rotation = _targetTransform.rotation;

			// 缩放：开启 Scale 时从变形后的矩阵提取，否则用目标的矩阵；
			// 先复位 localScale 再换算，避免累积误差
			Matrix4x4 scaleMatrix = _applyMethod.HasFlag(ApplyFlags.Scale)
				? matrix
				: _targetTransform.localToWorldMatrix;
			transform.localScale = Vector3.one;
			transform.localScale = (transform.worldToLocalMatrix * scaleMatrix).lossyScale;
		}
	}
}
