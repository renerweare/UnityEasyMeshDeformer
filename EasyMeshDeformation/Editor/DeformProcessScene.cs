// ============================================================================
// 文件：DeformProcessScene.cs
// 作用：场景构建（Build）回调。在构建场景时，把所有标记为 Static 的
//       MeshDeformer 变形器烘焙（Bake）成静态网格，并禁用对应的
//       DeformerCube 晶格，避免运行时继续执行变形计算。
// ============================================================================
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyMeshDeformation.Editor
{
	/// <summary>构建回调类：场景构建时烘焙所有标记为静态的变形器/晶格。</summary>
	internal class DeformProcessScene : IProcessSceneWithReport
	{
		/// <summary>回调执行顺序（0 表示默认优先级）。</summary>
		public int callbackOrder => 0;

		/// <summary>构建流程调用：遍历场景所有根对象，烘焙静态变形器并禁用静态晶格。</summary>
		/// <param name="scene">正在被处理的场景。</param>
		/// <param name="report">当前构建报告（此处未使用）。</param>
		public void OnProcessScene(Scene scene, BuildReport report)
		{
			// 清空上次烘焙的缓存数据
			DeformerBaker.Clear();

			foreach (GameObject rootObject in scene.GetRootGameObjects())
			{
				// 烘焙静态的 MeshDeformer 变形器
				MeshDeformer[] modifiers = rootObject.GetComponentsInChildren<MeshDeformer>();
				foreach (MeshDeformer modifier in modifiers)
				{
					if (modifier.gameObject.isStatic)
						DeformerBaker.Bake(modifier);
				}

				// 禁用静态的 DeformerCube 晶格：构建后不再参与运行时变形
				DeformerCube[] cubes = rootObject.GetComponentsInChildren<DeformerCube>();
				foreach (DeformerCube cube in cubes)
				{
					if (cube.gameObject.isStatic)
						cube.enabled = false;
				}
			}
		}
	}
}
