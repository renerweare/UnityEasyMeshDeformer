using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

using static EasyMeshDeformation.DeformShaderProperties;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 晶格变形系统的高层控制器：注入 PlayerLoop，收集并执行本帧的普通/蒙皮变形。
	/// </summary>
	public static class DeformFeature
	{
		#region Constants

		/// 晶格控制点最大数量（决定 DeformBuffer 缓冲大小），可自行修改。
		internal const int MaxHandles = 1024;

		/// 计算着色器文件名（相对 Resources，对应 Assets/EasyMeshDeformation/Resources/DeformCompute.compute）。
		internal const string ComputeShaderName = "DeformCompute";

		#endregion

		#region Fields

		/// 系统是否已初始化（防止重复初始化/重复注册 PlayerLoop）。
		private static bool _initialised = false;

		/// 当前使用的计算着色器实例（编辑器下可能被切换为副本实例，见 Editor Bug Workaround 区域）。
		private static ComputeShader _compute;
		/// 当前使用的着色器属性集合（与 _compute 配对）。
		private static DeformShaderProperties _properties;
		/// Deform 内核线程组大小（X 维度），用于计算分派的线程组数量。
		private static uint _deformGroupSize;

		/// 存放所有晶格控制点偏移量的 GPU 缓冲（StructuredBuffer&lt;float3&gt;，按 MaxHandles 申请）。
		private static ComputeBuffer _cubeBuffer;
		/// 缓存晶格三轴分辨率（复用数组，避免每帧分配）。
		private static readonly int[] _cubeResolution = new int[3];

		/// 每帧复用的命令缓冲区（普通变形与蒙皮变形共用，串行执行不会冲突）。
		private static readonly CommandBuffer _cmd = new();

		/// 本帧排队等待普通（非蒙皮）变形的组件队列。
		private static readonly List<MeshDeformerBase> _modifiers = new();
		/// 本帧排队等待蒙皮变形的组件队列。
		private static readonly List<MeshDeformerBase> _skinnedModifiers = new();

		#endregion

		#region Public Methods

		/// 把需要普通变形的组件加入本帧处理队列（ApplyModifiers 执行完毕后清空）。
		internal static void Enqueue(MeshDeformerBase modifier)
		{
			if (!_initialised) return;
			_modifiers.Add(modifier);
		}

		/// 把需要蒙皮变形的组件加入本帧处理队列（ApplySkinnedModifiers 执行完毕后清空）。
		internal static void EnqueueSkinned(MeshDeformerBase modifier)
		{
			if (!_initialised) return;
			_skinnedModifiers.Add(modifier);
		}

		/// 初始化系统：加载计算着色器、创建控制点缓冲、注册 PlayerLoop 回调（游戏启动时自动调用）。
		[RuntimeInitializeOnLoadMethod]
		internal static void Initialise()
		{
			if (_initialised) return;

			// 无图形设备（如批处理模式）时无需初始化
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
				return;

			// 平台不支持计算着色器时给出错误并提前退出
			if (!SystemInfo.supportsComputeShaders)
			{
				Debug.LogError("不支持计算着色器！晶格修改器将无法工作。");
				return;
			}

			_compute = Resources.Load<ComputeShader>(ComputeShaderName);

			if (_compute == null)
			{
				Debug.LogError($"无法加载晶格计算着色器。请确保它位于 Resources 文件夹内且命名为 {ComputeShaderName}");
				return;
			}

			if (!Application.isEditor)
				Application.quitting += Cleanup;

			_cubeBuffer = new(MaxHandles, 3 * sizeof(float));

			_properties = new(_compute);
			_properties.DisableAllKeywords();

			_compute.GetKernelThreadGroupSizes(0, out _deformGroupSize, out uint _, out uint _);
			_compute.SetBuffer(0, DeformBufferId, _cubeBuffer);

#if UNITY_EDITOR
			// 创建编辑器专用的着色器副本实例（用于规避 Unity 编辑器崩溃 bug）
			SetupComputeInstances();
#endif

			AddToPlayerLoop();

			_initialised = true;
		}

		/// 清理系统资源：释放控制点缓冲、清空队列、从 PlayerLoop 移除回调。
		internal static void Cleanup()
		{
			if (!_initialised) return;

			if (!Application.isEditor)
				Application.quitting -= Cleanup;

			_cubeBuffer?.Release();
			_cubeBuffer = null;

			_modifiers.Clear();
			_skinnedModifiers.Clear();

			RemoveFromPlayerLoop();

			_initialised = false;
		}

		#endregion

		#region Private Methods

		/// 对所有排队的普通Mesh组件执行变形（PlayerLoop 回调）。
		private static void ApplyModifiers()
		{
			if (_modifiers.Count == 0 || _cubeBuffer == null) return;

			_cmd.Clear();
			_cmd.name = "DeformerCube Modifiers";

			for (int i = 0; i < _modifiers.Count; i++)
			{
				ApplyModifier(_cmd, _modifiers[i]);
			}

			Graphics.ExecuteCommandBuffer(_cmd);

			_modifiers.Clear();
		}

		/// 向命令缓冲区写入单个普通Mesh组件的变形命令：恢复原始顶点数据 → 设置参数/关键字 → 分派内核。
		internal static void ApplyModifier(CommandBuffer cmd, MeshDeformerBase modifier)
		{
			if ((modifier == null) || !modifier.IsValid) return;

#if UNITY_EDITOR
			// 编辑器下切换到对应的着色器副本实例（规避编辑器崩溃 bug）
			SwapComputeInstance(cmd, modifier);
#endif

			SetupModifier(cmd, modifier);

			cmd.SetComputeBufferParam(_compute, 0, VertexBufferId, modifier.VertexBuffer);

			// 先用「原始Mesh副本」覆盖顶点缓冲，确保每帧从原始Mesh开始变形（非破坏性叠加）
			cmd.CopyBuffer(modifier.CopyBuffer, modifier.VertexBuffer);

			MeshInfo info = modifier.MeshInfo;
			List<DeformerItem> cubes = modifier.Items;
			Matrix4x4 localToWorld = modifier.LocalToWorld;
			int groups = info.VertexCount / (int)_deformGroupSize + 1;

			ApplyItems(cmd, modifier, cubes, localToWorld, info, groups);
		}

		/// 对所有排队的Skinned Mesh组件执行变形（PlayerLoop 回调，位于 UpdateAllSkinnedMeshes 之后，作用在蒙皮结果上）。
		private static void ApplySkinnedModifiers()
		{
			if (_skinnedModifiers.Count == 0 || _cubeBuffer == null) return;

			_cmd.Clear();
			_cmd.name = "Skinned DeformerCube Modifiers";

			for (int i = 0; i < _skinnedModifiers.Count; i++)
			{
				ApplySkinnedModifier(_cmd, _skinnedModifiers[i]);
			}

			Graphics.ExecuteCommandBuffer(_cmd);

			_skinnedModifiers.Clear();
		}

		/// 向命令缓冲区写入单个Skinned Mesh组件的变形命令。注意：Skinned Mesh不恢复 CopyBuffer（蒙皮结果每帧由骨骼系统重新写入）。
		private static void ApplySkinnedModifier(CommandBuffer cmd, MeshDeformerBase modifier)
		{
			if ((modifier == null) || !modifier.IsValid || !modifier.TryGetSkinnedBuffer(out var skinnedBuffer)) return;

#if UNITY_EDITOR
			// 编辑器下切换到对应的着色器副本实例（规避编辑器崩溃 bug）
			SwapComputeInstance(cmd, modifier);
#endif

			MeshInfo info = modifier.MeshInfo;

			SetupModifier(cmd, modifier);

			// 把蒙皮结果顶点缓冲绑定到内核（变形直接作用在蒙皮输出上）
			cmd.SetComputeBufferParam(_compute, 0, VertexBufferId, skinnedBuffer);

			Matrix4x4 localToWorld = modifier.SkinnedLocalToWorld;
			List<DeformerItem> cubes = modifier.Items;
			int groups = info.VertexCount / (int)_deformGroupSize + 1;

			ApplyItems(cmd, modifier, cubes, localToWorld, info, groups);
		}

		/// 设置组件级计算着色器参数：顶点数、缓冲步长、属性偏移、附加缓冲等。
		private static void SetupModifier(CommandBuffer cmd, MeshDeformerBase modifier)
		{
			ApplyMethod applyMethod = modifier.ResolvedApplyMethod;
			MeshInfo info = modifier.MeshInfo;

			cmd.SetKeyword(_compute, _properties.NormalsKeyword, applyMethod == ApplyMethod.PositionNormalTangent);

			cmd.SetComputeIntParam(_compute, VertexCountId, info.VertexCount);
			cmd.SetComputeIntParam(_compute, BufferStrideId, info.BufferStride);
			cmd.SetComputeIntParam(_compute, PositionOffsetId, info.PositionOffset);

			// 枚举按顺序递增，>= PositionNormalTangent 即同时包含法线/切线
			if (applyMethod >= ApplyMethod.PositionNormalTangent)
			{
				cmd.SetComputeIntParam(_compute, NormalOffsetId, info.NormalOffset);
				cmd.SetComputeIntParam(_compute, TangentOffsetId, info.TangentOffset);
			}

			cmd.SetKeyword(_compute, _properties.MultipleBuffersKeyword, info.HasAdditionalBuffer());

			if (info.HasAdditionalBuffer())
			{
				cmd.SetComputeIntParam(_compute, AdditionalStrideId, info.AdditionalStride);
				cmd.SetComputeBufferParam(_compute, 0, AdditionalBufferId, modifier.AdditionalBuffer);
			}
		}

		/// 按插值方式设置对应关键字（线性清晰为默认，无需关键字）。
		private static void SetupDeformerInterpolation(CommandBuffer cmd, InterpolationMethod method)
		{
			// 先禁用两个插值关键字，保证同一时间只启用一个
			cmd.DisableKeyword(_compute, _properties.InterpolationSmooth);
			cmd.DisableKeyword(_compute, _properties.InterpolationCubic);

			switch (method)
			{
				case InterpolationMethod.LinearSmooth:
					cmd.EnableKeyword(_compute, _properties.InterpolationSmooth);
					break;

				case InterpolationMethod.Cubic:
					cmd.EnableKeyword(_compute, _properties.InterpolationCubic);
					break;
			}
		}

		/// 按顶点遮罩类型设置对应的关键字与参数（常数/顶点色/UV/贴图）。
		private static void SetupDeformerMask(CommandBuffer cmd, in DeformerMask.VertexSettings mask, in MeshInfo info)
		{
			// 先禁用所有遮罩关键字，保证同一时间只启用一个
			cmd.DisableKeyword(_compute, _properties.MaskConstantKeyword);
			cmd.DisableKeyword(_compute, _properties.MaskColorKeyword);
			cmd.DisableKeyword(_compute, _properties.MaskUVKeyword);
			cmd.DisableKeyword(_compute, _properties.MaskTextureKeyword);

			switch (mask.Type)
			{
				case DeformerMask.VertexSettings.MaskType.Constant:
					cmd.EnableKeyword(_compute, _properties.MaskConstantKeyword);
					cmd.SetComputeFloatParam(_compute, MaskMultiplierId, mask.Multiplier);
					break;

				case DeformerMask.VertexSettings.MaskType.Color:
					// 顶点色遮罩：从附加缓冲读取顶点色，取指定通道
					cmd.EnableKeyword(_compute, _properties.MaskColorKeyword);
					cmd.SetComputeIntParam(_compute, MaskOffsetId, info.ColorOffset);
					cmd.SetComputeIntParam(_compute, MaskChannelId, (int)mask.Channel);
					cmd.SetComputeFloatParam(_compute, MaskMultiplierId, mask.Multiplier);
					break;

				case DeformerMask.VertexSettings.MaskType.UV:
					// UV 遮罩：从附加缓冲读取指定 UV 组，取指定通道
					cmd.EnableKeyword(_compute, _properties.MaskUVKeyword);
					cmd.SetComputeIntParam(_compute, MaskOffsetId, info.GetTexCoordOffset((int)mask.UV));
					cmd.SetComputeIntParam(_compute, MaskChannelId, (int)mask.Channel);
					cmd.SetComputeFloatParam(_compute, MaskMultiplierId, mask.Multiplier);
					break;

				case DeformerMask.VertexSettings.MaskType.Texture:
					// 贴图遮罩：用顶点的 UV 采样贴图，取指定通道
					cmd.EnableKeyword(_compute, _properties.MaskTextureKeyword);
					cmd.SetComputeTextureParam(_compute, 0, MaskTextureId, mask.Texture);
					cmd.SetComputeIntParam(_compute, MaskOffsetId, info.GetTexCoordOffset((int)mask.UV));
					cmd.SetComputeIntParam(_compute, MaskChannelId, (int)mask.Channel);
					cmd.SetComputeFloatParam(_compute, MaskMultiplierId, mask.Multiplier);
					break;
			}
		}

		/// 对一组晶格项逐个写入变形命令并分派 Deform 内核（支持按材质索引 UseIndices 筛选顶点）。
		private static void ApplyItems(CommandBuffer cmd, MeshDeformerBase modifier, List<DeformerItem> cubes, Matrix4x4 localToWorld, in MeshInfo info, int groups)
		{
			for (int i = 0; i < cubes.Count; i++)
			{
				DeformerItem deformerItem = cubes[i];
				DeformerCube cube = deformerItem.Cube;
				DeformerMask mask = deformerItem.Mask;
				int threadGroups = groups;

				// 跳过无效（为空或未启用）的晶格
				if ((cube == null) || !cube.isActiveAndEnabled) continue;

				// 控制点数量超出缓冲容量时报错并跳过
				if (cube.Offsets.Count > MaxHandles)
				{
					Debug.LogError($"晶格的控制点数量超过 {MaxHandles}，将被跳过", cube);
					continue;
				}

				// 设置晶格项参数：越界归零（Global=false 时启用）、插值方式、遮罩
				cmd.SetKeyword(_compute, _properties.ZeroOutsideKeyword, !deformerItem.Global);
				SetupDeformerInterpolation(cmd, deformerItem.Interpolation);
				SetupDeformerMask(cmd, mask.Vertex, info);

				Matrix4x4 objectToDeform = cube.transform.worldToLocalMatrix * localToWorld;
				Matrix4x4 deformToObject = objectToDeform.inverse;
				cmd.SetComputeMatrixParam(_compute, ObjectToDeformId, objectToDeform);
				cmd.SetComputeMatrixParam(_compute, DeformToObjectId, deformToObject);

				_cubeResolution[0] = cube.Resolution.x;
				_cubeResolution[1] = cube.Resolution.y;
				_cubeResolution[2] = cube.Resolution.z;
				cmd.SetComputeIntParams(_compute, DeformResolutionId, _cubeResolution);

				cmd.SetBufferData(_cubeBuffer, cube.Offsets);

				// 判断是否启用「按材质索引筛选顶点」：仅当遮罩类型为材质且索引合法时
				bool useIndices = (mask.Selection.Type == DeformerMask.SelectionSettings.MaskType.Material) &&
								  (mask.Selection.Index >= 0) &&
								  (mask.Selection.Index < modifier.IndexBuffers.Count);

				cmd.SetKeyword(_compute, _properties.UseIndicesKeyword, useIndices);

				if (useIndices)
				{
					// 绑定顶点索引映射缓冲，并按索引数量重新计算线程组数量
					ComputeBuffer indexBuffer = modifier.IndexBuffers[mask.Selection.Index];
					cmd.SetComputeBufferParam(_compute, 0, IndexToVertexMapId, indexBuffer);
					cmd.SetComputeIntParam(_compute, IndexCountId, indexBuffer.count);

					threadGroups = indexBuffer.count / (int)_deformGroupSize + 1;
				}

				// 分派 Deform 内核（内核索引 0），按 X 方向展开线程
				cmd.DispatchCompute(_compute, 0, threadGroups, 1, 1);
			}
		}

		/// 把变形系统注入 PlayerLoop：普通变形插在 UpdateAllSkinnedMeshes 之前，蒙皮变形插在其之后。
		private static void AddToPlayerLoop()
		{
			var loop = PlayerLoop.GetCurrentPlayerLoop();

			int postLateUpdateIndex = Array.FindIndex(loop.subSystemList, system => system.type == typeof(PostLateUpdate));
			var postLateUpdate = loop.subSystemList[postLateUpdateIndex];

			var postLateSystems = new List<PlayerLoopSystem>(postLateUpdate.subSystemList);
			var skinned = postLateSystems.FindIndex(system => system.type == typeof(PostLateUpdate.UpdateAllSkinnedMeshes));

			// 在蒙皮系统之前插入普通变形回调，使蒙皮可基于已变形的Mesh进行
			postLateSystems.Insert(skinned, new()
			{
				updateDelegate = ApplyModifiers,
				type = typeof(DeformFeature)
			});

			// 在蒙皮系统之后插入蒙皮变形回调，使变形作用在蒙皮结果之上
			postLateSystems.Insert(skinned + 2, new()
			{
				updateDelegate = ApplySkinnedModifiers,
				type = typeof(DeformFeature)
			});

			postLateUpdate.subSystemList = postLateSystems.ToArray();
			loop.subSystemList[postLateUpdateIndex] = postLateUpdate;

			PlayerLoop.SetPlayerLoop(loop);
		}

		/// 从 PlayerLoop 中移除所有与 DeformFeature 相关的回调（清理时调用）。
		private static void RemoveFromPlayerLoop()
		{
			var loop = PlayerLoop.GetCurrentPlayerLoop();

			int postLateUpdateIndex = Array.FindIndex(loop.subSystemList, system => system.type == typeof(PostLateUpdate));
			var postLateUpdate = loop.subSystemList[postLateUpdateIndex];

			var postLateSystems = new List<PlayerLoopSystem>(postLateUpdate.subSystemList);
			postLateSystems.RemoveAll(system => system.type == typeof(DeformFeature));

			postLateUpdate.subSystemList = postLateSystems.ToArray();
			loop.subSystemList[postLateUpdateIndex] = postLateUpdate;

			PlayerLoop.SetPlayerLoop(loop);
		}

		#endregion

		#region Editor Bug Workaround

#if UNITY_EDITOR
		/// 计算着色器副本。用于规避编辑器崩溃 bug：同一实例上使用过多关键字组合会崩溃，故为每组关键字组合准备独立副本。
		private struct ComputeInstance
		{
			/// 着色器副本。
			public ComputeShader Shader;
			/// 与该副本对应的属性（关键字与参数 ID）集合。
			public DeformShaderProperties Properties;
		}

		/// 需要创建的副本数量，等于 ApplyMethod 枚举取值数量。
		private const int InstanceCount = 3;
		/// 副本实例数组，按下标与 ApplyMethod 对应。
		private static readonly ComputeInstance[] _computeInstances = new ComputeInstance[InstanceCount];

		/// 创建若干着色器副本（DontSave 不参与场景保存），并预绑定控制点缓冲。
		private static void SetupComputeInstances()
		{
			for (int i = 0; i < InstanceCount; i++)
			{
				ref ComputeInstance instance = ref _computeInstances[i];

				instance.Shader = UnityEngine.Object.Instantiate(_compute);
				instance.Shader.hideFlags = HideFlags.DontSave;
				instance.Shader.SetBuffer(0, DeformBufferId, _cubeBuffer);

				instance.Properties = new(instance.Shader);
				instance.Properties.DisableAllKeywords();
			}
		}

		/// 按组件的 ApplyMethod 切换到对应副本；同时兼容「禁用域重载」场景（重新绑定缓冲防引用失效）。
		private static void SwapComputeInstance(CommandBuffer cmd, MeshDeformerBase modifier)
		{
			ComputeInstance instance = _computeInstances[(int)modifier.ResolvedApplyMethod];

			_compute = instance.Shader;
			_properties = instance.Properties;

			// 针对部分用户「禁用域重载」时的 bug 的规避：重新绑定控制点缓冲
			cmd.SetComputeBufferParam(_compute, 0, DeformBufferId, _cubeBuffer);
		}
#endif

		#endregion
	}
}
