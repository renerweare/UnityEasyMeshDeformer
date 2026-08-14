using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MeshCombiner))]
public class MeshCombinerEditor : Editor
{
	public override void OnInspectorGUI()
	{
		MeshCombiner meshCombiner = (MeshCombiner)target;

		GUI.enabled = false;
		EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(meshCombiner), typeof(MeshCombiner), false);
		GUI.enabled = true;

		// 是否把容器自身网格也纳入合并（固定变形网格用）
		meshCombiner.IncludeSelfMesh = EditorGUILayout.Toggle("Include Self Mesh", meshCombiner.IncludeSelfMesh);

		// 一键合并（固定变形网格）
		if(GUILayout.Button("Combine Meshes"))
		{
			meshCombiner.CombineMeshes(true);
		}
	}
}
