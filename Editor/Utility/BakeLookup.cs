// ============================================================================
// BakeLookup.cs
// 概述：烘焙结果缓存表：以"目标Mesh + 修改器配置"为键缓存变形Mesh，
// 批量烘焙时复用相同配置的计算结果，避免重复执行昂贵的变形计算。
// 配置比对：BakedSettings 记录应用方式、晶格变换矩阵、分辨率与手柄偏移，
// 通过浮点阈值 Threshold 判定是否一致。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>存储变形Mesh的字典，以及对相似/相同修改器的查找表（按目标Mesh分组，每条记录绑定配置快照）。</summary>
	internal class BakeLookup
	{
		/// <summary>浮点比对阈值：配置差异小于该值即视为一致。</summary>
		private const float Threshold = 0.0001f;

		/// <summary>烘焙缓存字典：键为目标Mesh，值为该Mesh下的一组烘焙记录。</summary>
		private readonly Dictionary<Mesh, List<Bake>> _bakedMeshes = new();

		/// <summary>清空全部缓存：销毁临时生成的变形Mesh并清空字典。</summary>
		internal void Clear()
		{
			foreach ((Mesh mesh, List<Bake> bakes) in _bakedMeshes)
			{
				foreach (Bake bake in bakes)
				{
					Object.DestroyImmediate(bake.DeformedMesh);
				}
			}
			_bakedMeshes.Clear();
		}

		/// <summary>尝试从缓存中获取与指定修改器配置匹配的变形Mesh。</summary>
		/// <param name="modifier">当前要烘焙的修改器组件。</param>
		/// <param name="mesh">找到的缓存变形Mesh；未命中时为 null。</param>
		/// <returns>true 表示命中可复用，false 表示需要重新计算。</returns>
		internal bool TryGet(MeshDeformer modifier, out Mesh mesh)
		{
			mesh = null;

			// 按目标Mesh定位分组；不存在则判定未命中
			if (!_bakedMeshes.TryGetValue(modifier.TargetMesh, out List<Bake> bakes))
				return false;

			// 生成配置快照，与每条缓存记录比对
			BakedSettings bakeSettings = new(modifier);

			foreach (Bake bake in bakes)
			{
				// 配置一致（分辨率、矩阵、手柄偏移均在阈值内）则复用该Mesh
				if (bakeSettings.Equals(bake.Settings))
				{
					mesh = bake.DeformedMesh;
					return true;
				}
			}

			return false;
		}

		/// <summary>将一次烘焙结果（配置快照 + 变形Mesh）存入缓存。</summary>
		/// <param name="modifier">产生该Mesh的修改器组件（用于记录配置快照）。</param>
		/// <param name="mesh">计算出的变形Mesh。</param>
		internal void Add(MeshDeformer modifier, Mesh mesh)
		{
			// 记录修改器配置快照，供后续 TryGet 比对
			Bake bake = new()
			{
				Settings = new(modifier),
				DeformedMesh = mesh,
			};

			// 目标Mesh还没有分组时先创建列表
			if (!_bakedMeshes.TryGetValue(modifier.TargetMesh, out List<Bake> bakes))
			{
				bakes = new();
				_bakedMeshes.Add(modifier.TargetMesh, bakes);
			}

			bakes.Add(bake);
		}

		/// <summary>一条烘焙记录：配置快照（Settings）与产出的变形Mesh（DeformedMesh）。</summary>
		private struct Bake
		{
			/// <summary>烘焙时使用的修改器配置快照。</summary>
			public BakedSettings Settings;

			/// <summary>由该配置计算得到的变形Mesh。</summary>
			public Mesh DeformedMesh;

			/// <summary>比较两条记录是否配置一致（仅比较 Settings）。</summary>
			public bool Equals(Bake bake)
			{
				return Settings.Equals(bake.Settings);
			}
		}

		/// <summary>烘焙时使用的"修改器 + 晶格"配置快照：记录应用方式与所有晶格条目的关键参数。</summary>
		private struct BakedSettings
		{
			/// <summary>变形应用方式（作用于整个Mesh的全局配置）。</summary>
			public ApplyMethod ApplyMethod;

			/// <summary>参与变形的每个晶格条目的配置快照列表。</summary>
			public List<BakedDeformerCube> Items;

			/// <summary>从修改器组件创建配置快照：遍历所有 DeformerItem 生成晶格条目快照。</summary>
			public BakedSettings(MeshDeformer modifier)
			{
				ApplyMethod = modifier.ResolvedApplyMethod;
				Items = new();
				foreach (DeformerItem item in modifier.Items)
				{
					Items.Add(new BakedDeformerCube(modifier, item));
				}
			}

			/// <summary>比对两份配置快照：先比应用方式，再逐条比对晶格条目。</summary>
			public bool Equals(BakedSettings modifier)
			{
				// 全局应用方式不一致则判定不相同
				if (ApplyMethod != modifier.ApplyMethod)
					return false;

				// 晶格条目数量必须相同
				if (Items.Count != modifier.Items.Count)
					return false;
				// 逐条比对每个晶格条目的配置
				for (int i = 0; i < Items.Count; i++)
				{
					if (!Items[i].Equals(modifier.Items[i]))
						return false;
				}

				return true;
			}
		}

		/// <summary>单个晶格条目在烘焙时使用的配置快照：条目、空间变换矩阵、分辨率与手柄偏移。</summary>
		private struct BakedDeformerCube
		{
			/// <summary>晶格条目（用于比对全局开关、插值方式与遮罩）。</summary>
			public DeformerItem Item;

			/// <summary>修改器局部空间 → 晶格局部空间的变换矩阵（体现两物体相对位置与朝向）。</summary>
			public Matrix4x4 ModifierToDeformerCube;

			/// <summary>晶格的三维分辨率（各轴向控制点数量）。</summary>
			public Vector3Int Resolution;

			/// <summary>晶格所有控制点的偏移数组（相对晶格原点的局部偏移）。</summary>
			public Vector3[] HandleOffsets;

			/// <summary>从修改器与晶格条目创建配置快照：记录空间矩阵、分辨率与手柄偏移。</summary>
			public BakedDeformerCube(MeshDeformer modifier, DeformerItem item)
			{
				DeformerCube cube = item.Cube;

				Item = item;
				ModifierToDeformerCube = cube.transform.worldToLocalMatrix *
					modifier.transform.localToWorldMatrix;
				Resolution = cube.Resolution;
				HandleOffsets = cube.Offsets.ToArray();
			}

			/// <summary>比对两个晶格条目快照（核心缓存比对算法）：分辨率、条目参数、手柄偏移与空间矩阵。</summary>
			public bool Equals(BakedDeformerCube cube)
			{
				// 分辨率必须完全一致
				if (Resolution != cube.Resolution)
					return false;

				// 全局开关、插值方式与遮罩必须一致
				if ((Item.Global != cube.Item.Global) ||
					(Item.Interpolation != cube.Item.Interpolation) ||
					!Item.Mask.Equals(cube.Item.Mask))
					return false;

				// 手柄偏移逐点比较（用平方距离避免开方）
				if (HandleOffsets.Length != cube.HandleOffsets.Length)
					return false;
				for (int i = 0; i < HandleOffsets.Length; i++)
				{
					float distance = (HandleOffsets[i] - cube.HandleOffsets[i]).sqrMagnitude;
					if (distance > Threshold)
						return false;
				}

				// 逐元素比较 4×4 变换矩阵
				for (int i = 0; i < 4; i++)
				{
					for (int j = 0; j < 4; j++)
					{
						float distance = Mathf.Abs(ModifierToDeformerCube[i, j] - cube.ModifierToDeformerCube[i, j]);
						if (distance > Threshold)
							return false;
					}
				}

				return true;
			}
		}
	}
}
