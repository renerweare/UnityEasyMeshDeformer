# EasyMeshDeformation（FFD 晶格变形）

基于 FFD（自由变形，Free-Form Deformation）的 Unity Mesh 变形系统。感谢 *Lattice Modifier* 提供的灵感与资源，本项目在其基础上借鉴并升级改造：中文化、统一命名（Deformer/Deform 风格）、整合到单个组件、简化 Inspector，并新增「选择衰减」与「仅使用生效控制点」等编辑器功能。

## 功能

- **FFD 晶格变形**：用 DeformerCube（3D 控制点网格）包裹物体，拖动控制点即实时变形 Mesh。
- **两种模式（一个组件）**：`MeshDeformer` 通过「模式」下拉切换：
  - **Mesh**：变形 `MeshFilter` + `MeshRenderer` 的 Mesh。
  - **Skinned Mesh**：变形 `SkinnedMeshRenderer` 的 Skinned Mesh（先 GPU 蒙皮，后晶格变形）。
- **GPU / CPU 双实现**：运行时用 Compute Shader（`DeformCompute.compute`）逐帧变形；CPU 侧 `DeformSolver` 供编辑器预览与烘焙。
- **三种插值方式**：线性（清晰）/ 线性（平滑）/ 三次方。
- **Global 开关**：晶格外变形衰减（关）/ 延续（开）。
- **顶点遮罩（乘数）**：每个晶格项一个乘数（0~1）控制变形强度。
- **编辑器友好**：控制点框选、选择衰减、仅使用生效控制点、一键适配晶格、烘焙静态 Mesh、保存变形 Mesh。

## 快速开始

1. 新建或选中带 `MeshFilter` / `MeshRenderer` 的物体（面数太少可先挂 `MeshSubdivider` 细分）。
2. 给物体挂 **MeshDeformer**。
3. 创建 **DeformerCube**（菜单 `GameObject > Effects > DeformerCube`），右键「适配到 Transform...」让它包住物体。
4. 选中 DeformerCube，框选控制点并拖动 → 实时变形（编辑器与运行时一致）。

## 组件

| 组件 | 说明 |
|---|---|
| **MeshDeformer** | 主组件：模式（Mesh / Skinned Mesh）、目标 Mesh、更新模式、晶格列表（晶格 / 插值 / Global / 乘数） |
| **DeformerCube** | 晶格：分辨率、选择衰减、仅使用生效控制点 |
| **TransformDeformer** | 独立组件：对目标 Transform 的位置 / 旋转 / 缩放应用晶格变形 |

## 平台要求

- 需要支持 Compute Shader 的图形 API（DX11+、Metal、Vulkan、GLES3.1+ 等）。
- Skinned Mesh 模式需在 Player Settings 开启 **GPU 蒙皮**。

## 致谢

感谢 *Lattice Modifier*（Heath）提供的灵感与资源，本项目在其基础上进行借鉴与升级改造。

## 许可证

MIT
