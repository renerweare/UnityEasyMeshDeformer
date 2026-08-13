# EasyMeshDeformation（晶格变形 / FFD）

基于 FFD（自由变形）的 Unity 网格变形系统，由 *Lattice Modifier* 插件复刻改造：中文化、统一命名（Deformer/Deform 风格）、整合到单个组件、简化 Inspector，并新增「选择衰减」与「仅使用生效控制点」等编辑器功能。

## 功能

- **FFD 晶格变形**：用 DeformerCube（3D 控制点网格）包裹物体，拖动控制点即实时变形网格。
- **两种模式（一个组件）**：`MeshDeformer` 通过「模式」下拉切换：
  - **网格**：变形 `MeshFilter` + `MeshRenderer` 的网格。
  - **蒙皮网格**：变形 `SkinnedMeshRenderer` 的蒙皮网格（先 GPU 蒙皮，后晶格变形）。
- **GPU / CPU 双实现**：运行时用 Compute Shader（`DeformCompute.compute`）逐帧变形；CPU 侧 `DeformSolver` 供编辑器预览与烘焙。
- **三种插值方式**：线性（清晰）/ 线性（平滑）/ 三次方。
- **Global 开关**：晶格外变形衰减（关）/ 延续（开）。
- **顶点遮罩（乘数）**：每个晶格项一个乘数（0~1）控制变形强度。
- **编辑器友好**：控制点框选、选择衰减、仅使用生效控制点、一键适配晶格、烘焙静态网格、保存变形网格。

## 快速开始

1. 新建或选中带 `MeshFilter` / `MeshRenderer` 的物体（面数太少可先挂 `MeshSubdivider` 细分）。
2. 给物体挂 **MeshDeformer**。
3. 创建 **DeformerCube**（菜单 `GameObject > Effects > DeformerCube`），右键「适配到 Transform...」让它包住物体。
4. 选中 DeformerCube，框选控制点并拖动 → 实时变形（编辑器与运行时一致）。

## 组件

| 组件 | 说明 |
|---|---|
| **MeshDeformer** | 主组件：模式（网格/蒙皮网格）、目标网格、更新模式、晶格列表（晶格 / 插值 / Global / 乘数） |
| **DeformerCube** | 晶格：分辨率、选择衰减、仅使用生效控制点 |
| **TransformDeformer** | 独立组件：对目标 Transform 的位置 / 旋转 / 缩放应用晶格变形 |

## 平台要求

- 需要支持 Compute Shader 的图形 API（DX11+、Metal、Vulkan、GLES3.1+ 等）。
- 蒙皮网格模式需在 Player Settings 开启 **GPU 蒙皮**。

## 常见问题

- **变形后网格发光 / 出现黑色块？** 目标网格缺切线导致，系统会自动补算切线；仍异常则检查模型导入设置的 Tangents。
- **拉控制点没效果？** ① 晶格项已绑定 DeformerCube 且激活；② 顶点在晶格范围内；③ 网格模式有 MeshFilter/MeshRenderer。
- **蒙皮网格报「GPU 蒙皮未启用」？** 在 `Edit > Project Settings > Player` 勾选 GPU Skinning。

## 许可证

本项目为学习/复刻用途，基于开源资源 *Lattice Modifier*（Heath）改造，请遵循原资源许可证。
