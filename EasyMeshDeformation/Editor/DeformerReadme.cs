// ============================================================================
// DeformerReadme.cs
// 概述：空壳 ScriptableObject 作为 README 检视面板宿主，
// DeformerReadmeEditor 展示插件介绍、文档入口、支持邮箱与社区链接。
// 链接常量（文档/社区/评价/邮箱/版本号）为固定字符串，不可改动。
// ============================================================================
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyMeshDeformation.Editor
{
	/// <summary>用于承载 README 检视面板的空壳 ScriptableObject。</summary>
	public class DeformerReadme : ScriptableObject { }

	/// <summary>为 DeformerReadme 提供自定义 Inspector：展示文档入口、支持邮箱与社区链接。</summary>
	[CustomEditor(typeof(DeformerReadme))]
	public class DeformerReadmeEditor : UnityEditor.Editor
	{
		/// <summary>在线文档地址（固定链接，不可改动）。</summary>
		private const string DocumentationUrl = "https://harryheath.com/lattice";
		/// <summary>Discord 社区邀请链接（固定链接，不可改动）。</summary>
		private const string DiscordUrl = "https://discord.gg/q4F9YbtB6V";
		/// <summary>资源商店评价页链接（固定链接，不可改动）。</summary>
		private const string ReviewUrl = "https://u3d.as/3mDH#reviews";
		/// <summary>支持邮箱地址（固定字符串，不可改动）。</summary>
		private const string Email = "support@harryheath.com";
		/// <summary>当前插件版本号（固定字符串，不可改动）。</summary>
		private const string Version = "v1.4.0";

		/// <summary>本地文档相对路径（相对包根目录，固定字符串，不可改动）。</summary>
		private static readonly string DocumentationPath = Path.Combine("Documentation", "deformer.html");

		/// <summary>绘制 README 检视面板：标题、简介、文档入口、支持邮箱、社区链接与评价入口。</summary>
		public override void OnInspectorGUI()
		{
			Header1($"Unity 晶格修改器（Mesh Deformer）- {Version}");
			Paragraph("为 Unity 添加晶格修改器，让你可以轻松地变形静态物体和" +
				"蒙皮物体，从而创作出原本很复杂的动画。");

			Paragraph("");
			// 文档小节：提供本地文档与在线文档两种打开方式
			Header2("文档");
			Paragraph("可通过以下两种方式打开文档：");

			// 第一行：本地文档链接（根据 README 资源所在目录推导完整路径）
			using (new EditorGUILayout.HorizontalScope())
			{
				string readmePath = AssetDatabase.GetAssetPath(target);
				string packagePath = Path.GetDirectoryName(readmePath);
				string relativePath = Path.Combine(packagePath, DocumentationPath);
				string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), relativePath);

				Link(relativePath, fullPath);

				// 本地文档文件不存在时给出缺失提示
				if (!File.Exists(fullPath))
				{
					Paragraph("（缺失，你是否移动了 README？）");
				}

				GUILayout.FlexibleSpace();
			}

			// 第二行：在线文档链接
			using (new EditorGUILayout.HorizontalScope())
			{
				Link(DocumentationUrl, DocumentationUrl);
				GUILayout.FlexibleSpace();
			}

			Paragraph("");
			// 支持小节：展示联系邮箱
			Header2("支持");
			Paragraph("如果你有任何问题或需要排查方面的帮助，请随时发送邮件至：");
			using (new EditorGUILayout.HorizontalScope())
			{
				Link(Email, "mailto:" + Email);
				GUILayout.FlexibleSpace();
			}

			Paragraph("");
			// 社区小节：Discord 邀请
			Header2("社区");
			Paragraph("要讨论、提问以及分享使用晶格创作的作品，欢迎加入 Discord 服务器：");
			using (new EditorGUILayout.HorizontalScope())
			{
				Link(DiscordUrl, DiscordUrl);
				GUILayout.FlexibleSpace();
			}

			Paragraph("");
			// 致谢小节：请求评价
			Header2("感谢你的支持！");
			Paragraph("如果你喜欢这个资源，请考虑给它一个好评。这对我意义重大，谢谢：");
			using (new EditorGUILayout.HorizontalScope())
			{
				Link(ReviewUrl, ReviewUrl);
				GUILayout.FlexibleSpace();
			}
		}

		#region Utility

		/// <summary>渲染一级标题（大号加粗，带上下留白）。</summary>
		private static void Header1(string text)
		{
			EditorGUILayout.Space();
			GUILayout.Label(text, Styles.Header1);
			EditorGUILayout.Space();
		}

		/// <summary>渲染二级标题（中号加粗，带上下留白）。</summary>
		private static void Header2(string text)
		{
			EditorGUILayout.Space();
			GUILayout.Label(text, Styles.Header2);
			EditorGUILayout.Space();
		}

		/// <summary>渲染正文段落（自动换行）。</summary>
		private static void Paragraph(string text)
		{
			GUILayout.Label(text, Styles.Paragraph);
		}

		/// <summary>渲染带下划线的可点击链接：文字下方画线 + 链接光标 + 点击按钮。</summary>
		/// <param name="text">链接显示文本。</param>
		/// <param name="url">点击后打开的地址。</param>
		private static void Link(string text, string url)
		{
			GUIContent content = new(text);
			Rect position = GUILayoutUtility.GetRect(content, Styles.Link);

			// 在链接文字底部画下划线
			using (new Handles.DrawingScope(Styles.Link.normal.textColor))
				Handles.DrawLine(
					new Vector3(position.xMin + Styles.Link.padding.left, position.yMax),
					new Vector3(position.xMax - Styles.Link.padding.right, position.yMax)
				);

			// 悬停时显示链接光标
			EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);

			// 点击后打开 URL
			if (GUI.Button(position, content, Styles.Link))
			{
				Application.OpenURL(url);
			}
		}

		/// <summary>README 使用的 GUIStyle 缓存（懒加载，避免每次绘制新建样式）。</summary>
		private static class Styles
		{
			/// <summary>一级标题样式缓存。</summary>
			private static GUIStyle _header1;
			/// <summary>二级标题样式缓存。</summary>
			private static GUIStyle _header2;
			/// <summary>正文段落样式缓存。</summary>
			private static GUIStyle _paragraph;
			/// <summary>链接样式缓存。</summary>
			private static GUIStyle _link;

			/// <summary>一级标题：大号加粗（字号 24）。</summary>
			public static GUIStyle Header1 => _header1 ??= new(EditorStyles.boldLabel)
			{
				fontSize = 24
			};

			/// <summary>二级标题：中号加粗（字号 18）。</summary>
			public static GUIStyle Header2 => _header2 ??= new(EditorStyles.boldLabel)
			{
				fontSize = 18
			};

			/// <summary>正文段落：普通标签，字号 14、自动换行。</summary>
			public static GUIStyle Paragraph => _paragraph ??= new(EditorStyles.label)
			{
				fontSize = 14,
				wordWrap = true
			};

			/// <summary>链接：普通标签，字号 14，文字颜色取链接标签色。</summary>
			public static GUIStyle Link => _link ??= new(EditorStyles.label)
			{
				fontSize = 14,
				normal = new()
				{
					textColor = EditorStyles.linkLabel.normal.textColor
				}
			};
		}

		#endregion
	}
}
