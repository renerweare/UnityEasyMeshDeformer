# EasyMeshDeformation（FFD 晶格变形）

基于 FFD（自由变形，Free-Form Deformation）的 Unity Mesh 变形系统。

## 功能

- **FFD 晶格变形**：用 DeformerCube（控制点网格）包裹部分物体，拖动控制点即实时变形 Mesh。
- **两种模式（一个组件）**：`MeshDeformer` 通过「模式」下拉切换：
  - **Mesh**：变形 `MeshFilter` + `MeshRenderer` 
  - **Skinned Mesh**：变形 `SkinnedMeshRenderer` 
- **GPU / CPU 双实现**：运行时用 Compute Shader（`DeformCompute.compute`）逐帧变形；CPU 侧 `DeformSolver` 供编辑器预览与烘焙。
<<<<<<< Updated upstream
- **三种插值方式**：线性（清晰）/ 线性（平滑）/ 三次方。
- **Global**：晶格外变形衰减（关）/ 延续（开）。
- **乘数**：每个晶格项一个乘数（0~1）控制变形强度。
=======
- **三种插值方式**：Linear (Sharp) / Linear (Smooth) / Cubic。
- **Global 开关**：晶格外变形衰减（关）/ 延续（开）。
- **Multiplier**：每个晶格项一个乘数（0~1）控制变形强度。
- **GPU 回读**：`GetDeformedMesh()` 从 GPU 顶点缓冲回读变形结果，供固定变形、破碎/切割前结算。
- **配套工具**：`MeshCombiner`（网格融合器，合并前自动回读 GPU 变形网格）、`Samples/throw`（砸墙示例：对象池 + 砸点叠加 + 双阈值事件）。
>>>>>>> Stashed changes

## 快速开始

1. 新建或选中带 `MeshFilter` / `MeshRenderer` 的物体
2. 给物体挂 **MeshDeformer**。
3. 在一个空物体上挂 **DeformerCube**，把晶格摆到能包住目标的位置。
4. 把 DeformerCube 拖入 MeshDeformer 的晶格列表（Items）。
5. 选中 DeformerCube，框选控制点并拖动 → 实时变形（编辑器与运行时一致）。

## 组件

| 组件 | 说明 |
|---|---|
| **MeshDeformer** | 主组件：模式（Mesh / Skinned Mesh）、目标 Mesh、更新模式、晶格列表（DeformerCube / Compute Method / Global / Multiplier） |
| **DeformerCube** | 晶格：分辨率、选择衰减、仅使用生效控制点 |
| **TransformDeformer** | 独立组件：对目标 Transform 的位置 / 旋转 / 缩放应用晶格变形 |
| **MeshCombiner** | 网格融合器：合并容器与子物体网格，合并前自动回读 GPU 变形结果（固定变形网格） |

## 目录

```
EasyMeshDeformation/
├── MainScript/       # MeshDeformer / DeformerCube / TransformDeformer
├── Runtime/          # 运行时（组件、扩展、工具、DeformFeature）
├── Editor/           # 编辑器（Inspector / Scene 编辑器 / 烘焙 / 设置）
├── Resources/        # DeformCompute.compute
├── MeshCombiner/     # 网格融合器
└── Samples/          # 示例（Grab 拖拽 / throw 砸墙）
```

## 平台要求

- 需要支持 Compute Shader 的图形 API（DX11+、Metal、Vulkan、GLES3.1+ 等）。
- Skinned Mesh 模式需对模型开启 Read/Write，并在 Player Settings 开启 GPU Skinning。

## 致谢

感谢 *Lattice Modifier*（Heath）提供的灵感与资源，本项目在其基础上进行借鉴与升级改造。

## 许可证

MIT
