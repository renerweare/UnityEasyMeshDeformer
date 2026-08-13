// ============================================================================
// GrabController.cs —— 鼠标拖拽晶格变形示例
// 按下鼠标点击物体 → 把 DeformerCube 移过来，拖拽其控制点 (0,0,0) 拉扯Mesh，
// 松开后控制点以弹簧物理回弹到基准位置；每次拖拽轮换晶格池中的下一个晶格。
// ============================================================================
using UnityEngine;

namespace EasyMeshDeformation
{
	/// <summary>
	/// 通过鼠标拖拽晶格控制点实现「拉扯变形」的示例控制器。
	/// </summary>
	public class GrabController : MonoBehaviour
	{
		/// <summary>被拖拽的控制点坐标（固定为晶格第一个控制点）。</summary>
		private readonly Vector3Int Handle = new(0, 0, 0);

		/// <summary>控制点的弹簧物理状态（位置 + 速度）。</summary>
		private struct State
		{
			public Vector3 Position;
			public Vector3 Velocity;
		}

		[SerializeField, Tooltip("被点击物体的碰撞体：鼠标按下时对它发射线，决定拖拽起点。")]
		private MeshCollider _targetCollider;

		[SerializeField, Tooltip("相机：用于把鼠标位置转换成射线。")]
		private Camera _camera;

		[SerializeField, Tooltip("弹簧摩擦力：控制点回弹时的速度衰减系数，越大停下越快。")]
		private float _friction;

		[SerializeField, Tooltip("弹簧加速度：控制点回归基准位置的回弹力度，越大回弹越猛。")]
		private float _acceleration;

		[SerializeField, Tooltip("晶格池：每次按下鼠标轮换一个 DeformerCube 供拖拽，松手后各自回弹。")]
		private DeformerCube[] _deformerCubes;

		private int _cubeIndex;
		private bool _grabbing;
		private Plane _grabPlane;
		private Vector3 _grabPosition;
		private State[] _states;

		private void Start()
		{
			// 校验必要引用，缺失时禁用组件并提示
			if ((_camera == null) || (_deformerCubes == null) || (_deformerCubes.Length == 0) || (_targetCollider == null))
			{
				Debug.LogError("GrabController 缺少必要引用：需要相机（_camera）、" +
					"至少一个 DeformerCube（_deformerCubes）以及被点击物体的碰撞体（_targetCollider）。组件已禁用。", this);
				enabled = false;
				return;
			}
			for (int i = 0; i < _deformerCubes.Length; i++)
			{
				if (_deformerCubes[i] == null)
				{
					Debug.LogError($"GrabController：_deformerCubes 第 {i} 个晶格为空。组件已禁用。", this);
					enabled = false;
					return;
				}
			}

			// 记录各控制点的基准位置作为弹簧目标
			_states = new State[_deformerCubes.Length];
			for (int i = 0; i < _deformerCubes.Length; i++)
			{
				_states[i].Position = _deformerCubes[i].GetHandleBaseWorldPosition(Handle);
			}
		}

		private void Update()
		{
			if (Input.GetMouseButtonDown(0)) StartGrab();
			if (Input.GetMouseButtonUp(0)) ReleaseGrab();

			// 弹簧回弹（拖拽中会被 UpdateGrab 覆盖）
			for (int i = 0; i < _deformerCubes.Length; i++)
			{
				UpdateCube(i);
			}

			if (_grabbing) UpdateGrab();
		}

		/// <summary>
		/// 弹簧物理：加速度趋向基准位置，摩擦力衰减速度，积分后写回控制点。
		/// </summary>
		private void UpdateCube(int index)
		{
			DeformerCube cube = _deformerCubes[index];
			ref State state = ref _states[index];

			Vector3 target = cube.GetHandleBaseWorldPosition(Handle);
			Vector3 accel = _acceleration * (target - state.Position) - _friction * state.Velocity;

			state.Velocity += accel          * Time.deltaTime;
			state.Position += state.Velocity * Time.deltaTime;

			cube.SetHandleWorldPosition(Handle, state.Position);
		}

		/// <summary>
		/// 按下鼠标：射线与碰撞体相交时开始拖拽，把晶格移过来使控制点 (0,0,0) 落在点击位置。
		/// </summary>
		private void StartGrab()
		{
			Ray ray = _camera.ScreenPointToRay(Input.mousePosition);

			if (_targetCollider.Raycast(ray, out RaycastHit hitInfo, 100f))
			{
				Vector3 position = hitInfo.point;
				DeformerCube cube = _deformerCubes[_cubeIndex];
				ref State state = ref _states[_cubeIndex];

				// 记录点击位置，并创建「过点击位置、面向相机」的拖拽平面
				_grabPosition = position;
				_grabPlane = new Plane(-_camera.transform.forward, _grabPosition);
				_grabbing = true;

				// 平移晶格使控制点 (0,0,0) 落在点击位置
				Vector3 offset = cube.transform.TransformVector(-cube.GetHandleBasePosition(Handle));
				cube.transform.position = _grabPosition + offset;
				state = new State() { Position = _grabPosition };
			}
		}

		/// <summary>
		/// 拖拽中：控制点跟随鼠标在拖拽平面上移动，并估算速度用于松手回弹。
		/// </summary>
		private void UpdateGrab()
		{
			Ray ray = _camera.ScreenPointToRay(Input.mousePosition);

			if (_grabPlane.Raycast(ray, out float dist))
			{
				Vector3 position = ray.GetPoint(dist);
				DeformerCube cube = _deformerCubes[_cubeIndex];
				ref State state = ref _states[_cubeIndex];

				// 估算鼠标速度：本帧位移 / 帧时长，与旧速度平滑混合
				Vector3 velocity = Vector3.Lerp(
					(position - state.Position) / Time.deltaTime,
					state.Velocity,
					0.8f
				);

				state = new State() { Position = position, Velocity = velocity };
				cube.SetHandleWorldPosition(Handle, position);
			}
		}

		/// <summary>
		/// 松开鼠标：结束拖拽，切换到下一个晶格。
		/// </summary>
		private void ReleaseGrab()
		{
			_grabbing = false;
			_cubeIndex = (_cubeIndex + 1) % _deformerCubes.Length;
		}
	}
}
