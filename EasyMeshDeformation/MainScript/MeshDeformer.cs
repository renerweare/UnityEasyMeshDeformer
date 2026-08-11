// ============================================================================
// MeshDeformer.cs —— 网格变形器（继承 MeshDeformerBase 的具体实现）
// 核心机制：按 Mode 对普通网格或蒙皮网格变形；蒙皮模式经 TryGetSkinnedBuffer()
// 读取 GPU 蒙皮后的顶点缓冲再变形；编辑器 Update 检测模式切换，切换前恢复
// 旧网格避免 sharedMesh 悬空。
// ============================================================================
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>网格变形器：通过「模式」选择对普通网格或蒙皮网格应用晶格变形。</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public class MeshDeformer : MeshDeformerBase
	{
		/// <summary>变形目标的类型。</summary>
		public enum Mode
		{
			/// <summary>网格：对 MeshFilter / MeshRenderer 的网格应用变形。</summary>
			[InspectorName("网格")] Mesh,
			/// <summary>蒙皮网格：对 SkinnedMeshRenderer 的蒙皮网格应用变形（GPU 蒙皮后变形）。</summary>
			[InspectorName("蒙皮网格")] Skinned,
		}

		#region Constants

		/// <summary>「模式」字段在 Inspector 中的 Tooltip 文案。</summary>
		private const string ModeTooltip =
			"变形目标的类型：\n" +
			" - 网格：对 MeshFilter/MeshRenderer 的网格变形。\n" +
			" - 蒙皮网格：对 SkinnedMeshRenderer 的蒙皮网格变形。";

		/// <summary>GPU 蒙皮未启用时的错误提示文案（蒙皮网格变形依赖 GPU 蒙皮）。</summary>
		private const string GpuSkinningError =
			"尚未在 Player Settings 中启用 GPU 蒙皮！蒙皮网格变形将不会生效。\n" +
			"请打开 Player Settings（编辑 > 项目设置... > 播放器），确保 GPU 蒙皮已启用或设置为 GPU/GPU Batched。";

		#endregion

		#region Fields

		/// <summary>变形目标类型（网格 / 蒙皮网格），在 Inspector 中切换。</summary>
		[SerializeField, Tooltip(ModeTooltip)]
		private Mode _mode = Mode.Mesh;

		// 网格模式
		/// <summary>网格模式使用的 MeshFilter 组件（懒获取并缓存）。</summary>
		private MeshFilter _meshFilter;
		/// <summary>网格模式使用的 MeshRenderer 组件（懒获取并缓存）。</summary>
		private MeshRenderer _meshRenderer;

		// 蒙皮网格模式
		/// <summary>蒙皮网格模式使用的 SkinnedMeshRenderer 组件（懒获取并缓存）。</summary>
		private SkinnedMeshRenderer _skinnedMeshRenderer;
		/// <summary>GPU 蒙皮后的顶点缓冲（每次 Enqueue 时从渲染器重新获取）。</summary>
		private GraphicsBuffer _skinnedVertexBuffer;
		/// <summary>蒙皮模式的本地→世界矩阵（基于根骨骼），供 compute shader 计算蒙皮结果变换。</summary>
		private Matrix4x4 _skinnedLocalToWorld;

		/// <summary>当前生效的模式（与 _mode 区分：_mode 是序列化值，可能在 Inspector 中被即时修改）。</summary>
		private Mode _currentMode;

		#endregion

		#region Properties

		/// <summary>变形目标类型；设置新值时先恢复旧渲染器网格，再切换并重新初始化。</summary>
		public Mode DeformMode
		{
			get => _mode;
			set
			{
				if (value == _mode) return;

				// 切换模式前先恢复渲染网格，避免工作副本被销毁后 sharedMesh 悬空
				RestoreOriginalMesh();

				_mode = value;
				_currentMode = value;
				if (enabled) OnEnable();
			}
		}

		/// <summary>获取当前物体上的 MeshRenderer 组件（懒加载并缓存）。</summary>
		private MeshRenderer MeshRenderer => (_meshRenderer == null)
			? _meshRenderer = GetComponent<MeshRenderer>()
			: _meshRenderer;

		/// <summary>获取当前物体上的 MeshFilter 组件（懒加载并缓存）。</summary>
		private MeshFilter MeshFilter => (_meshFilter == null)
			? _meshFilter = GetComponent<MeshFilter>()
			: _meshFilter;

		/// <summary>获取当前物体上的 SkinnedMeshRenderer 组件（懒加载并缓存）。</summary>
		private SkinnedMeshRenderer SkinnedMeshRenderer => (_skinnedMeshRenderer == null)
			? _skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>()
			: _skinnedMeshRenderer;

		#endregion

		#region Protected Methods

		/// <inheritdoc cref="MeshDeformerBase.GetMesh"/>
		/// <summary>按当前模式获取渲染器上的网格（蒙皮模式返回 sharedMesh，否则 MeshFilter.sharedMesh）。</summary>
		protected override Mesh GetMesh()
		{
			if (_mode == Mode.Skinned) return SkinnedMeshRenderer != null ? SkinnedMeshRenderer.sharedMesh : null;
			return MeshFilter != null ? MeshFilter.sharedMesh : null;
		}

		/// <inheritdoc cref="MeshDeformerBase.SetMesh"/>
		/// <summary>按当前模式把网格写入渲染器；蒙皮模式保证至少一根骨骼使 GPU 蒙皮走正确缓冲路径。</summary>
		protected override void SetMesh(Mesh mesh)
		{
			if (_mode == Mode.Skinned)
			{
#if UNITY_EDITOR
				// 编辑器下先检查 GPU 蒙皮开关，未启用则输出错误
				CheckGpuSkinning();
#endif
				if (SkinnedMeshRenderer == null) return;

				// 写入蒙皮网格
				SkinnedMeshRenderer.sharedMesh = mesh;

				// 保证渲染器至少有一根骨骼，确保渲染时使用正确的顶点缓冲
				if (SkinnedMeshRenderer.bones.Length == 0)
				{
					SkinnedMeshRenderer.bones = new[] { transform };
				}
				return;
			}

			if (MeshFilter == null) return;
			MeshFilter.sharedMesh = mesh;
		}

		/// <inheritdoc cref="MeshDeformerBase.Release"/>
		/// <summary>释放基类全部缓冲与工作副本，再释放蒙皮模式的顶点缓冲。</summary>
		protected override void Release()
		{
			base.Release();

			_skinnedVertexBuffer?.Release();
			_skinnedVertexBuffer = null;
		}

		/// <inheritdoc cref="MeshDeformerBase.Enqueue"/>
		/// <summary>按模式与可见性把变形任务入队；蒙皮模式额外记录根骨骼矩阵并入队 EnqueueSkinned。</summary>
		protected override void Enqueue(bool ignoreMode)
		{
			if (_mode == Mode.Skinned)
			{
				if (SkinnedMeshRenderer == null) return;

				bool isVisible = SkinnedMeshRenderer.isVisible;

#if UNITY_EDITOR
				// 编辑器非运行态且可见时强制更新，保证编辑器里拖拽晶格能即时看到效果
				ignoreMode |= !Application.isPlaying && isVisible;
#endif

				if (ignoreMode || (UpdateMode == UpdateMode.Always) ||
					(isVisible && (UpdateMode == UpdateMode.WhenVisible)))
				{
					DeformFeature.Enqueue(this);
				}

				// 蒙皮任务入队条件：强制 / 可见 / 允许离屏更新时也计算
				if (ignoreMode || isVisible || SkinnedMeshRenderer.updateWhenOffscreen)
				{
					// 蒙皮结果基于根骨骼坐标系计算（无根骨骼则用自身）
					Transform root = (SkinnedMeshRenderer.rootBone != null)
						? SkinnedMeshRenderer.rootBone
						: transform;

					_skinnedLocalToWorld = Matrix4x4.TRS(root.position,
						root.rotation, Vector3.one);

					DeformFeature.EnqueueSkinned(this);
				}
				return;
			}

			if (MeshRenderer == null) return;

			bool isVisibleMesh = MeshRenderer.isVisible;

#if UNITY_EDITOR
			// 编辑器非运行态且可见时强制更新
			ignoreMode |= !Application.isPlaying && isVisibleMesh;
#endif

			if (ignoreMode || (UpdateMode == UpdateMode.Always) ||
				(isVisibleMesh && (UpdateMode == UpdateMode.WhenVisible)))
			{
				DeformFeature.Enqueue(this);
			}
		}

		/// <inheritdoc cref="MeshDeformerBase.TryGetSkinnedBuffer"/>
		/// <summary>获取 GPU 蒙皮后的顶点缓冲；每次先释放再重新获取（蒙皮渲染器可能悄悄切换缓冲）。</summary>
		internal override bool TryGetSkinnedBuffer(out GraphicsBuffer skinnedBuffer)
		{
			if ((_mode != Mode.Skinned) || (SkinnedMeshRenderer == null))
			{
				skinnedBuffer = null;
				return false;
			}

			// 理想是缓存缓冲，但蒙皮渲染器可能悄悄换用新缓冲且代码无法感知，故每帧重新获取
			_skinnedVertexBuffer?.Release();
			_skinnedVertexBuffer = null;

			// 更新蒙皮顶点缓冲：开启 Raw 目标后从渲染器获取当前顶点缓冲
			SkinnedMeshRenderer.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
			_skinnedVertexBuffer = SkinnedMeshRenderer.GetVertexBuffer();

			skinnedBuffer = _skinnedVertexBuffer;
			return skinnedBuffer != null;
		}

		/// <inheritdoc cref="MeshDeformerBase.SkinnedLocalToWorld"/>
		/// <summary>蒙皮模式的本地→世界矩阵：基于根骨骼（无根骨骼则用自身）的位置与旋转构造。</summary>
		internal override Matrix4x4 SkinnedLocalToWorld
		{
			get
			{
				if ((_mode == Mode.Skinned) && (SkinnedMeshRenderer != null))
				{
					Transform root = (SkinnedMeshRenderer.rootBone != null)
						? SkinnedMeshRenderer.rootBone
						: transform;

					return Matrix4x4.TRS(root.position, root.rotation, Vector3.one);
				}

				return base.SkinnedLocalToWorld;
			}
		}

		#endregion

		#region Private Methods

		/// <summary>切换模式前把渲染网格恢复为原始目标网格，避免工作副本被销毁后 sharedMesh 悬空；按旧模式恢复。</summary>
		private void RestoreOriginalMesh()
		{
			// 无目标网格时不动渲染器，避免把 sharedMesh 误置为 null
			if (TargetMesh == null) return;

			if (_currentMode == Mode.Skinned)
			{
				if (SkinnedMeshRenderer != null) SkinnedMeshRenderer.sharedMesh = TargetMesh;
			}
			else if (MeshFilter != null)
			{
				MeshFilter.sharedMesh = TargetMesh;
			}
		}

		#endregion

		#region Unity Methods

#if UNITY_EDITOR
		/// <inheritdoc cref="MeshDeformerBase.Update"/>
		/// <summary>编辑器每帧回调：检测模式切换并重新初始化（先恢复旧渲染器网格），否则走基类逻辑。</summary>
		protected override void Update()
		{
			if (_mode != _currentMode)
			{
				// 先恢复旧 renderer 网格，避免工作副本被销毁后 sharedMesh 悬空
				RestoreOriginalMesh();

				_currentMode = _mode;
				if (enabled) OnEnable();
				return;
			}

			base.Update();
		}

		/// <summary>检查 GPU 蒙皮是否启用，未启用时输出错误日志（仅编辑器调用）。</summary>
		private static void CheckGpuSkinning()
		{
			if (!UnityEditor.PlayerSettings.gpuSkinning)
			{
				Debug.LogError(GpuSkinningError);
			}
		}
#endif

		#endregion
	}
}
