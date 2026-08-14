using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 启动（Start）时重新绑定（Rebind）Animator 的实用组件。
	/// 适用场景：对「带 Animator 控制表情（blend shapes）的Skinned Mesh」使用晶格变形时，需重新绑定 Animator 才能让变形后的Mesh正确参与骨骼/表情动画。
	/// </summary>
	[ExecuteAlways]
	[RequireComponent(typeof(Animator))]
	public class RebindOnStart : MonoBehaviour
	{
		private const string AnimatorTooltip = 
			"启动时要重新绑定的 Animator。";

		/// 要重新绑定的 Animator（为空时回退到同物体上的 Animator 组件）。
		[SerializeField, Tooltip(AnimatorTooltip)] 
		private Animator _animator;

		/// 启动时重新绑定 Animator（优先使用指定实例，为空则从当前物体获取）。
		void Start()
		{
			if (_animator != null || TryGetComponent(out _animator))
			{
				_animator.Rebind();
			}
		}
	}
}
