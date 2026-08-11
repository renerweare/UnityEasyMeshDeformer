// ============================================================================
// Array3D.cs
// 概述：三维数组工具类：以"行主序"（X 最快、Z 最慢）把三维坐标扁平化存储
// 在一维 List 中，提供按坐标索引的读写、Resize 与 Clear。
// 索引算法：index = x + sizeX * y + sizeX * sizeY * z。
// ============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>三维数组工具类：把三维网格数据扁平化存储在一维列表中。</summary>
	/// <typeparam name="T">数组元素类型（如 float、Vector3）。</typeparam>
	public class Array3D<T>
	{
		/// <summary>数组的三维尺寸（各轴元素数量）。</summary>
		private Vector3Int _size;

		/// <summary>扁平化存储的一维列表（容量 = x * y * z）。</summary>
		private List<T> _list = new();

		/// <summary>当前三维尺寸。</summary>
		public Vector3Int Size => _size;

		/// <summary>元素总数（x × y × z）。</summary>
		private int Count => _size.x * _size.y * _size.z;

		/// <summary>按 Vector3Int 坐标读写元素。</summary>
		/// <param name="coords">三维坐标。</param>
		public T this[Vector3Int coords]
		{
			get => _list[GetIndex(coords.x, coords.y, coords.z)];
			set => _list[GetIndex(coords.x, coords.y, coords.z)] = value;
		}

		/// <summary>按三个整数坐标读写元素。</summary>
		public T this[int x, int y, int z]
		{
			get => _list[GetIndex(x, y, z)];
			set => _list[GetIndex(x, y, z)] = value;
		}

		/// <summary>默认构造：空数组（尺寸为零）。</summary>
		public Array3D() { }

		/// <summary>按指定尺寸构造数组。</summary>
		/// <param name="size">三维尺寸。</param>
		public Array3D(Vector3Int size) => Resize(size);

		/// <summary>按三个轴的尺寸构造数组。</summary>
		public Array3D(int x, int y, int z) : this(new Vector3Int(x, y, z)) { }

		/// <summary>调整数组尺寸：尺寸未变化时仅清空元素，变化时重建列表并置为默认值。</summary>
		/// <param name="size">新的三维尺寸。</param>
		public void Resize(Vector3Int size)
		{
			if (_size == size)
			{
				Clear();
				return;
			}

			_size = size;
			_list.Clear();
			for (int i = 0; i < Count; i++)
			{
				_list.Add(default);
			}
		}

		/// <summary>按三个整数轴尺寸调整数组（转发到 Vector3Int 版本）。</summary>
		public void Resize(int x, int y, int z)
		{
			Resize(new Vector3Int(x, y, z));
		}

		/// <summary>把所有元素重置为默认值（不改变尺寸）。</summary>
		public void Clear()
		{
			for (int i = 0; i < Count; i++)
			{
				_list[i] = default;
			}
		}

		/// <summary>三维坐标 → 一维索引的映射（行主序，X 最快、Z 最慢）。</summary>
		private int GetIndex(int x, int y, int z)
		{
			return x + (_size.x * y) + (_size.x * _size.y * z);
		}
	}
}
