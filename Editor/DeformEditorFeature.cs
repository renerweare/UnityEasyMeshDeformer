// ============================================================================
// 文件：DeformEditorFeature.cs
// 作用：把运行时变形系统接入编辑器生命周期：程序集重载后初始化；
//       场景保存前/后还原/应用变形Mesh，避免把变形结果写进场景文件；
//       光照烘焙前烘焙静态Mesh；右键菜单提供「保存变形Mesh」入口。
// ============================================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyMeshDeformation.Editor
{
	/// <summary>编辑器侧集成入口：管理 DeformFeature 的初始化、清理与场景事件订阅。</summary>
	internal static class DeformEditorFeature
	{
		/// <summary>是否已完成初始化（防止重复订阅事件）。</summary>
		private static bool _initialised = false;

		/// <summary>等待资源加载的最大尝试次数（每次编辑器刷新重试一次）。</summary>
		private static int _waitAttempts = 10;

		/// <summary>初始化变形系统（程序集重载后由 Unity 自动调用）。</summary>
		[InitializeOnLoadMethod]
		private static void Initialise()
		{
			if (_initialised) return;
			if (!WaitForAssetsLoaded()) return;

			DeformFeature.Initialise();

			// 订阅编辑器事件：退出、属性右键菜单、程序集重载、场景保存、光照烘焙
			EditorApplication.quitting += Cleanup;
			EditorApplication.contextualPropertyMenu += OnContextualPropertyMenu;
			AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
			EditorSceneManager.sceneSaved += OnSceneSaved;
			EditorSceneManager.sceneSaving += OnSceneSaving;
			Lightmapping.bakeStarted += OnLightmapBakeStarted;

			_initialised = true;
		}

		/// <summary>清理所有相关系统（编辑器关闭或程序集重载前调用）。</summary>
		private static void Cleanup()
		{
			if (!_initialised) return;

			DeformFeature.Cleanup();

			EditorApplication.quitting -= Cleanup;
			EditorApplication.contextualPropertyMenu -= OnContextualPropertyMenu;
			AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
			EditorSceneManager.sceneSaved -= OnSceneSaved;
			EditorSceneManager.sceneSaving -= OnSceneSaving;
			Lightmapping.bakeStarted -= OnLightmapBakeStarted;

			_initialised = false;
		}

		/// <summary>程序集重载前清理 DeformFeature，避免持有失效的运行时资源。</summary>
		private static void OnBeforeAssemblyReload()
		{
			DeformFeature.Cleanup();
		}

		/// <summary>多次调度 Initialise 以等待资源（计算着色器）加载完成。</summary>
		/// <returns>资源是否已就绪。</returns>
		private static bool WaitForAssetsLoaded()
		{
			EditorApplication.delayCall -= Initialise;

			if (Resources.Load(DeformFeature.ComputeShaderName) != null) return true;

			_waitAttempts -= 1;

			if (_waitAttempts >= 0)
			{
				// 还有剩余尝试次数：下一帧再试
				EditorApplication.delayCall += Initialise;
			}
			else
			{
				// 多次尝试后仍未成功：提示着色器的路径与命名要求
				Debug.LogError($"无法加载晶格计算着色器。请确保它位于 Resources 文件夹内且命名为 {DeformFeature.ComputeShaderName}");
			}
			return false;
		}

		#region Scene Cleanup

		/// <summary>场景保存完成后恢复所有Mesh为变形后版本。</summary>
		private static void OnSceneSaved(Scene scene)
		{
			ApplyMeshes();
		}

		/// <summary>场景保存前把所有Mesh还原为未变形版本，避免变形结果写进场景文件。</summary>
		private static void OnSceneSaving(Scene scene, string path)
		{
			ResetMeshes();
		}

		/// <summary>光照贴图烘焙开始前，先烘焙所有静态变形Mesh。</summary>
		private static void OnLightmapBakeStarted()
		{
#pragma warning disable 0618
			MeshDeformer[] components = Object.FindObjectsOfType<MeshDeformer>();
#pragma warning restore 0618

			// 清空缓存后为所有静态变形器执行一次烘焙
			DeformerBaker.Clear();
			foreach (MeshDeformer modifier in components)
			{
				if (modifier.gameObject.isStatic) DeformerBaker.Bake(modifier, true);
			}
		}

		/// <summary>把场景中所有启用的变形器的Mesh还原为未变形版本。</summary>
		private static void ResetMeshes()
		{
#pragma warning disable 0618
			MeshDeformerBase[] components = Object.FindObjectsOfType<MeshDeformerBase>();
#pragma warning restore 0618

			for (int i = 0; i < components.Length; i++)
			{
				if (components[i].isActiveAndEnabled) components[i].ResetMesh();
			}
		}

		/// <summary>把场景中所有启用的变形器的Mesh恢复为变形后版本。</summary>
		private static void ApplyMeshes()
		{
#pragma warning disable 0618
			MeshDeformerBase[] components = Object.FindObjectsOfType<MeshDeformerBase>();
#pragma warning restore 0618

			for (int i = 0; i < components.Length; i++)
			{
				if (components[i].isActiveAndEnabled) components[i].ApplyMesh();
			}
		}

		#endregion

		#region Context Menus

		/// <summary>弹出保存面板，把变形后的普通Mesh另存为资产文件。</summary>
		/// <param name="modifier">要导出Mesh的变形器组件。</param>
		private static void SaveDeformedMesh(MeshDeformerBase modifier)
		{
			string path = EditorUtility.SaveFilePanelInProject(
				"保存变形Mesh",
				modifier.gameObject.name,
				"asset",
				"请输入要保存变形Mesh的路径。"
			);

			// 用户取消保存时 path 为空，直接返回
			if (!string.IsNullOrEmpty(path))
			{
				Mesh mesh = modifier.GetDeformedMesh();
				if (mesh != null) AssetDatabase.CreateAsset(mesh, path);
			}
		}

		/// <summary>弹出保存面板，把变形后的Skinned Mesh另存为资产文件。</summary>
		/// <param name="modifier">要导出Skinned Mesh的变形器组件。</param>
		private static void SaveDeformedSkinnedMesh(MeshDeformerBase modifier)
		{
			string path = EditorUtility.SaveFilePanelInProject(
				"保存变形Skinned Mesh",
				modifier.gameObject.name,
				"asset",
				"请输入要保存变形Skinned Mesh的路径。"
			);

			// 用户取消保存时 path 为空，直接返回
			if (!string.IsNullOrEmpty(path))
			{
				Mesh mesh = modifier.GetDeformedSkinnedMesh();
				if (mesh != null) AssetDatabase.CreateAsset(mesh, path);
			}
		}

		/// <summary>菜单校验：右键组件标题栏时，目标为 MeshDeformer 才启用「保存变形Mesh...」。</summary>
		[MenuItem("CONTEXT/MeshDeformer/保存变形Mesh...", true)]
		private static bool SaveDeformedMeshValidate(MenuCommand command)
		{
			return command.context is MeshDeformer;
		}

		/// <summary>菜单回调：保存普通变形Mesh。</summary>
		[MenuItem("CONTEXT/MeshDeformer/保存变形Mesh...")]
		private static void SaveDeformedMesh(MenuCommand command)
		{
			if (command.context is not MeshDeformer modifier) return;
			SaveDeformedMesh(modifier);
		}

		/// <summary>菜单校验：仅当组件处于 Skinned 模式时「保存变形Skinned Mesh...」可用。</summary>
		[MenuItem("CONTEXT/MeshDeformer/保存变形Skinned Mesh...", true)]
		private static bool SaveDeformedSkinnedMeshValidate(MenuCommand command)
		{
			if (command.context is not MeshDeformer modifier) return false;
			return modifier.DeformMode == MeshDeformer.Mode.Skinned;
		}

		/// <summary>菜单回调：保存蒙皮变形Mesh。</summary>
		[MenuItem("CONTEXT/MeshDeformer/保存变形Skinned Mesh...")]
		private static void SaveDeformedSkinnedMesh(MenuCommand command)
		{
			if (command.context is not MeshDeformer modifier) return;
			SaveDeformedSkinnedMesh(modifier);
		}

		/// <summary>属性右键菜单回调：在组件字段上右键时追加「保存变形Mesh」等菜单项。</summary>
		private static void OnContextualPropertyMenu(GenericMenu menu, SerializedProperty property)
		{
			if (property.serializedObject.targetObject is not MeshDeformer modifier) return;

			menu.AddItem(new GUIContent("保存变形Mesh..."), false, () => SaveDeformedMesh(modifier));

			// Skinned 模式：额外提供保存蒙皮变形Mesh入口
			if (modifier.DeformMode == MeshDeformer.Mode.Skinned)
			{
				menu.AddItem(new GUIContent("保存变形Skinned Mesh..."), false, () => SaveDeformedSkinnedMesh(modifier));
			}
		}

		#endregion
	}
}
