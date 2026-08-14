// ============================================================================
// MeshDeformerBase.cs —— Mesh变形器抽象基类
// 核心机制：复制目标Mesh为工作副本并建立 GPU 顶点缓冲，每帧经 DeformFeature
// 入队后由 compute shader 在 GPU 上做 FFD 变形，全程不读回 CPU；更新时机由
// UpdateMode 控制，OnDisable 时恢复原始Mesh并释放全部 GPU 缓冲。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;

namespace EasyMeshDeformation
{
	/// <summary>Mesh变形器抽象基类：负责目标Mesh复制、GPU 顶点缓冲创建释放与每帧入队变形任务。</summary>
	abstract public class MeshDeformerBase : MonoBehaviour
	{
		#region Constants

		/// <summary>「目标Mesh」字段在 Inspector 中的 Tooltip 文案。</summary>
		private const string TargetMeshTooltip =
			"要对其应用变形并在此物体上渲染的Mesh。";

		/// <summary>「更新模式」字段在 Inspector 中的 Tooltip 文案。</summary>
		private const string UpdateModeTooltip =
			"变形何时更新。仅适用于非蒙皮格子。\n" +
			"若设为「手动」，必须调用 RequestUpdate()。";

		/// <summary>「晶格列表」字段在 Inspector 中的 Tooltip 文案。</summary>
		private const string ItemsTooltip =
			"应用于目标Mesh的格子。\n" +
			"将按顺序在蒙皮之前应用。";

		/// <summary>顶点缓冲的 GraphicsBuffer 目标类型：Raw + CopySource/CopyDestination（每帧拷贝原始顶点用于复位）。</summary>
		private const GraphicsBuffer.Target BufferTargets = GraphicsBuffer.Target.Raw
			| GraphicsBuffer.Target.CopySource | GraphicsBuffer.Target.CopyDestination;

		#endregion

		#region Fields

		/// <summary>目标Mesh：要对其应用变形并渲染的Mesh，为空时由 Initialise 从渲染器读取。</summary>
		[SerializeField, NotKeyable, Tooltip(TargetMeshTooltip)]
		private Mesh _targetMesh;

		/// <summary>变形更新时机（仅非蒙皮模式生效）：可见时 / 每帧 / 手动 RequestUpdate()。</summary>
		[SerializeField, Tooltip(UpdateModeTooltip)]
		private UpdateMode _updateMode = UpdateMode.WhenVisible;

		/// <summary>应用于目标Mesh的晶格列表，默认含一个空项保证开箱即用。</summary>
		[SerializeField, Tooltip(ItemsTooltip)]
		private List<DeformerItem> _items = new()
		{
			new DeformerItem()
			{
				Mask = { Vertex = { Type = DeformerMask.VertexSettings.MaskType.Constant, Multiplier = 1f } }
			}
		};

		/// <summary>目标Mesh的运行时工作副本：变形结果写入此Mesh，而不是污染源资源。</summary>
		private Mesh _mesh;

		/// <summary>工作副本的顶点信息（顶点数、缓冲布局等），用于创建 / 拷贝 GPU 缓冲。</summary>
		private MeshInfo _meshInfo;

		/// <summary>原始顶点缓冲的副本，每帧用于把顶点复位到未变形状态。</summary>
		private GraphicsBuffer _copyBuffer;

		/// <summary>GPU 顶点缓冲（stream 0：位置 / 法线 / 切线等）。</summary>
		private GraphicsBuffer _vertexBuffer;

		/// <summary>GPU 附加缓冲（stream 1：额外的顶点属性，如 UV）。</summary>
		private GraphicsBuffer _additionalBuffer;

		/// <summary>各子Mesh去重后的三角形索引 ComputeBuffer 列表，供 compute shader 使用。</summary>
		private List<ComputeBuffer> _indexBuffers;

		/// <summary>标记本帧是否已入队变形任务，避免同一帧重复入队。</summary>
		private bool _ranThisFrame = false;

		/// <summary>解析后的变形应用方式（位置/法线/切线 或 仅位置），由目标Mesh的顶点属性决定。</summary>
		private ApplyMethod _resolvedApplyMethod;

		/// <summary>记录上一次 Initialise 时的目标Mesh引用，用于在编辑器中检测 Inspector 的切换。</summary>
		private Mesh _currentTargetMesh;

		/// <summary>记录上一次 Initialise 时的 isStatic 状态，用于在编辑器中检测 Inspector 的切换。</summary>
		private bool _currentIsStatic;

		#endregion

		#region Properties

		/// <summary>要应用到此Mesh上的晶格（DeformerItem）列表。</summary>
		public List<DeformerItem> Items => _items;

		/// <summary>变形更新与应用的时机（仅非蒙皮模式生效）。</summary>
		public UpdateMode UpdateMode { get => _updateMode; set => _updateMode = value; }

		/// <summary>要对其应用变形的Mesh；赋值后若已启用会立即重新初始化。</summary>
		public Mesh TargetMesh
		{
			get => _targetMesh;
			set
			{
				if (value != _targetMesh)
				{
					_targetMesh = value;
					if (enabled) OnEnable();
				}
			}
		}

		/// <summary>用于渲染的Mesh（工作副本）。内部使用。</summary>
		internal Mesh Mesh => _mesh;

		/// <summary>关于此Mesh的顶点信息。内部使用。</summary>
		internal MeshInfo MeshInfo => _meshInfo;

		/// <summary>解析后的变形应用方式（缺法线/切线时降级为仅变形位置）。内部使用。</summary>
		internal ApplyMethod ResolvedApplyMethod => _resolvedApplyMethod;

		/// <summary>此Mesh顶点缓冲的副本，用于每帧复位顶点。内部使用。</summary>
		internal GraphicsBuffer CopyBuffer => _copyBuffer;

		/// <summary>此Mesh的 GPU 顶点缓冲（stream 0）。内部使用。</summary>
		internal GraphicsBuffer VertexBuffer => _vertexBuffer;

		/// <summary>此Mesh的附加缓冲（stream 1）。内部使用。</summary>
		internal GraphicsBuffer AdditionalBuffer => _additionalBuffer;

		/// <summary>此Mesh各子Mesh的（去重后的）索引缓冲列表。内部使用。</summary>
		internal List<ComputeBuffer> IndexBuffers => GetIndexBuffers();

		/// <summary>Mesh的本地→世界变换矩阵。内部使用。</summary>
		internal Matrix4x4 LocalToWorld => transform.localToWorldMatrix;

		/// <summary>应用蒙皮变形时使用的矩阵；蒙皮模式下子类可覆写（如返回根骨骼矩阵）。</summary>
		internal virtual Matrix4x4 SkinnedLocalToWorld => LocalToWorld;

		/// <summary>组件是否有效且可无错地应用变形（GPU 顶点缓冲与副本均已建立）。</summary>
		internal bool IsValid => _vertexBuffer != null && _copyBuffer != null;

		#endregion

		#region Public Methods

		/// <summary>强制本帧执行一次变形更新（即使 Manual 或不可见）；同帧多次调用只入队一次。</summary>
		public void RequestUpdate()
		{
			EnqueueIfNeeded(true);
		}

		/// <summary>把变形后的工作副本Mesh（_mesh）应用到渲染器。</summary>
		internal void ApplyMesh()
		{
			SetMesh(_mesh);
		}

		/// <summary>把原始目标Mesh（_targetMesh）恢复应用到渲染器，撤销变形显示。</summary>
		internal void ResetMesh()
		{
			SetMesh(_targetMesh);
		}

		#endregion

		#region Protected Methods

		/// <summary>尝试获取蒙皮后的顶点缓冲区（GPU 蒙皮输出）；非蒙皮模式返回 false。</summary>
		internal virtual bool TryGetSkinnedBuffer(out GraphicsBuffer skinnedBuffer)
		{
			skinnedBuffer = null;
			return false;
		}

		/// <summary>释放所有 GPU 缓冲与工作副本Mesh（Initialise 重建前与 OnDisable 时调用）。</summary>
		protected virtual void Release()
		{
			_copyBuffer?.Release();
			_copyBuffer = null;

			_vertexBuffer?.Release();
			_vertexBuffer = null;

			_additionalBuffer?.Release();
			_additionalBuffer = null;

			if (_indexBuffers != null)
			{
				foreach (ComputeBuffer indexBuffer in _indexBuffers)
				{
					indexBuffer.Release();
				}
				_indexBuffers.Clear();
				_indexBuffers = null;
			}

			if (_mesh != null)
			{
				if (Application.isPlaying)
				{
					Destroy(_mesh);
				}
				else
				{
					DestroyImmediate(_mesh);
				}
				_mesh = null;
			}
		}

		/// <summary>获取当前Mesh：从 MeshFilter 或 SkinnedMeshRenderer 读取。</summary>
		protected abstract Mesh GetMesh();

		/// <summary>设置Mesh：写入 MeshFilter 或 SkinnedMeshRenderer。</summary>
		protected abstract void SetMesh(Mesh mesh);

		/// <summary>把本变形器入队等待本帧执行变形 compute shader；ignoreMode=true 时无视 UpdateMode 强制入队。</summary>
		protected abstract void Enqueue(bool ignoreMode);

		#endregion

		#region Private Methods

		/// <summary>本帧未执行且组件有效时入队变形任务并标记，同帧只入队一次。</summary>
		private void EnqueueIfNeeded(bool ignoreMode)
		{
			if (!_ranThisFrame && IsValid)
			{
				Enqueue(ignoreMode);
				_ranThisFrame = true;
			}
		}

		/// <summary>初始化变形器：确定源Mesh → 释放旧资源 → 复制工作副本 → 建立 GPU 缓冲。</summary>
		private void Initialise()
		{
			// 顺序很重要：sharedMesh 可能正指向将被 Release 销毁的工作副本，需先取源Mesh
			if (_targetMesh == null)
			{
				_targetMesh = GetMesh();

				// 若 sharedMesh 是自己的工作副本，先复制一份独立源
				if ((_mesh != null) && (_targetMesh == _mesh))
				{
					_targetMesh = Instantiate(_mesh);
					_targetMesh.hideFlags = _mesh.hideFlags;
				}
			}

			// 释放所有旧缓冲 / 旧Mesh
			Release();

			// 记录当前状态，供编辑器 Update 检测 Inspector 中的变化
			_currentTargetMesh = _targetMesh;
			_currentIsStatic = gameObject.isStatic;

			// 仍无目标Mesh则提前退出（未指定或渲染器上无Mesh，属正常情况）
			if (_targetMesh == null)
			{
				return;
			}

			// Mesh不可读则报错并退出（静态Mesh会在运行时前烘焙，可忽略）
			if (!_targetMesh.isReadable && !gameObject.isStatic)
			{
				Debug.LogError("目标Mesh未启用读写权限。请在模型导入设置中启用。\n" +
					"或者，若打算用作静态Mesh，请将 GameObject 设为静态。", _targetMesh);
				return;
			}

			bool hasUvs = _targetMesh.HasVertexAttribute(VertexAttribute.TexCoord0);
			bool hasNormals = _targetMesh.HasVertexAttribute(VertexAttribute.Normal);
			bool hasTangents = _targetMesh.HasVertexAttribute(VertexAttribute.Tangent);

#if UNITY_EDITOR
			// 编辑器检查：无 UV 却使用 CalculateMikk 切线会导致法线错误，给出修复提示
			if (!hasUvs && hasTangents && hasNormals)
			{
				string assetPath = UnityEditor.AssetDatabase.GetAssetPath(_targetMesh);
				if (!string.IsNullOrEmpty(assetPath) && (UnityEditor.AssetImporter.GetAtPath(assetPath) is UnityEditor.ModelImporter importer))
				{
					if (importer.importTangents == UnityEditor.ModelImporterTangents.CalculateMikk)
					{
						Debug.LogWarning("在没有 UV 映射的情况下使用「Calculate Mikktspace」，Mesh法线可能出现错误。\n" +
							"要修复此问题，请在模型导入设置中将「Tangents」设为「Calculate Legacy」。\n" +
							"遗憾的是，Unity 在没有 UV 映射时会认为不需要切线，但本例中并非如此。", _targetMesh);
					}
				}
			}
#endif

			// 创建目标Mesh副本（工作副本）：变形写在此副本上，避免污染源资源
			_mesh = Instantiate(_targetMesh);
			_mesh.hideFlags |= HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
			_mesh.name = _targetMesh.name + " (DeformerCube)";
			_mesh.vertexBufferTarget |= BufferTargets;

			// 缺切线会导致渲染异常，自动补算（有 UV 和法线即可计算）
			if (!_mesh.HasVertexAttribute(VertexAttribute.Tangent))
			{
				_mesh.RecalculateTangents();
			}

			// 默认应用方式：同时变形位置 / 法线 / 切线
			_resolvedApplyMethod = ApplyMethod.PositionNormalTangent;

			// 无法线/切线时无法变形它们，降级为仅变形位置
			if (!hasNormals || !hasTangents)
			{
				Debug.LogWarning("Mesh没有法线和切线时，无法对法线和切线进行变形。", this);
				_resolvedApplyMethod = ApplyMethod.PositionOnly;
			}

			// 至少添加一根骨骼：确保带 blendShape 的Mesh渲染时走正确的顶点缓冲（GPU 蒙皮路径）
			if ((_targetMesh.blendShapeCount > 0) && (_mesh.bindposes.Length == 0))
			{
				Matrix4x4[] bindPoses = new Matrix4x4[] { Matrix4x4.identity };

				var bonesPerVertex = new NativeArray<byte>(_mesh.vertexCount, Allocator.Temp);
				var weights = new NativeArray<BoneWeight1>(_mesh.vertexCount, Allocator.Temp);

				for (int i = 0; i < _mesh.vertexCount; i++)
				{
					bonesPerVertex[i] = 1;
					weights[i] = new()
					{
						boneIndex = 0,
						weight = 0.0f
					};
				}

				_mesh.SetBoneWeights(bonesPerVertex, weights);
				_mesh.bindposes = bindPoses;

				bonesPerVertex.Dispose();
				weights.Dispose();
			}

			// 收集Mesh顶点信息（顶点数、缓冲布局等）
			_meshInfo = new(_mesh);

			// 获取 GPU 顶点缓冲（stream 0）
			_vertexBuffer = _mesh.GetVertexBuffer(0);
			// 若存在附加顶点属性（stream 1，如 UV），一并获取
			if (_meshInfo.HasAdditionalBuffer())
			{
				_additionalBuffer = _mesh.GetVertexBuffer(1);
			}

			// 创建顶点缓冲副本：每帧用于把顶点复位回原始状态
			_copyBuffer = new GraphicsBuffer(
				BufferTargets,
				_meshInfo.VertexCount,
				_meshInfo.BufferStride
			);
			Graphics.CopyBuffer(_vertexBuffer, _copyBuffer);
		}

		/// <summary>按需构建并缓存各子Mesh去重后的索引 ComputeBuffer。</summary>
		private List<ComputeBuffer> GetIndexBuffers()
		{
			if (_indexBuffers != null) return _indexBuffers;

			_indexBuffers = new();

			List<int> indices = new();
			for (int i = 0; i < _mesh.subMeshCount; i++)
			{
				// 获取第 i 个子Mesh的三角形索引
				_mesh.GetTriangles(indices, i, true);

				// 去除重复索引（compute shader 逐顶点处理，重复索引无意义）
				int[] indexSet = indices.Distinct().ToArray();

				// 写入 compute buffer 并缓存
				ComputeBuffer indexBuffer = new(indexSet.Length, sizeof(int));
				indexBuffer.SetData(indexSet);
				_indexBuffers.Add(indexBuffer);
			}

			return _indexBuffers;
		}

		#endregion

		#region Unity Methods

		/// <summary>每帧渲染后回调：入队本帧变形任务并复位帧标记。</summary>
		protected virtual void LateUpdate()
		{
			EnqueueIfNeeded(false);
			_ranThisFrame = false;
		}

		/// <summary>组件启用时初始化资源、挂载工作副本并立即入队一次变形。</summary>
		protected virtual void OnEnable()
		{
			Initialise();
			ApplyMesh();
			EnqueueIfNeeded(true);
		}

		/// <summary>组件禁用时把渲染器恢复为原始Mesh并释放全部 GPU 缓冲与工作副本。</summary>
		protected virtual void OnDisable()
		{
			ResetMesh();
			Release();
		}

#if UNITY_EDITOR
		/// <summary>编辑器每帧回调：检测 Inspector 中目标Mesh/isStatic 变化并重置，确保渲染器挂的是工作副本。</summary>
		protected virtual void Update()
		{
			// Inspector 中目标Mesh或 isStatic 变化时重建组件
			if ((_targetMesh != _currentTargetMesh) ||
				(gameObject.isStatic != _currentIsStatic))
			{
				OnEnable();
			}

			// 烘焙光照贴图期间不要替换Mesh，否则烘焙结果会丢失
			if (gameObject.isStatic && UnityEditor.Lightmapping.isRunning)
				return;

			// 确保渲染器上的Mesh是变形后的工作副本（防止外部改回 sharedMesh）
			if ((_mesh != null) && (_mesh != GetMesh()))
			{
				ApplyMesh();
			}
		}

		/// <summary>编辑器回调：选中预制体资产时重置渲染器Mesh，保证预制体预览显示源Mesh。</summary>
		protected virtual void OnValidate()
		{
			// 选中预制体资源时重置Mesh，确保预制体预览中能看到Mesh
			if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this))
			{
				void OnPreview()
				{
					// 只执行一次：先从编辑器 update 循环移除自身
					UnityEditor.EditorApplication.update -= OnPreview;

					if ((this != null) && enabled) ResetMesh();
				}

				UnityEditor.EditorApplication.update += OnPreview;
			}
		}
#endif

		#endregion
	}
}
