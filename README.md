# EasyMeshDeformation（FFD 变形）

基于 FFD（自由变形，Free-Form Deformation）的 Unity 网格变形系统

---

## 功能特性

- **FFD 变形**：用 3D 控制点网格（DeformerCube）包裹物体，拖动控制点即实时变形网格。
- **两种模式（一个组件）**：`MeshDeformer` ->Mesh or Skinned Mesh。
- **GPU / CPU 双实现**：运行时用 Compute Shader（`DeformCompute.compute`）逐帧变形，编辑器与 CPU 侧用 `DeformSolver`（等价实现，供烘焙/预览）。
- **三种计算方式**：Linear (Sharp) / Linear (Smooth) / Cubic。
- **Global**：关闭时晶格外的顶点按控制点偏移衰减（越界偏移归零）；开启时变形延续到晶格外部。
- **乘数**：每个晶格项一个「Multiplier」，全局缩放变形强度（0~1）。
- **独立组件**：`TransformDeformer` 对目标 Transform 的位置 / 旋转 / 缩放应用晶格变形。
- **GPU 回读**：`MeshDeformerExtensions.GetDeformedMesh()` 从 GPU 顶点缓冲回读变形结果，供固定变形、破碎/切割前结算等用途。
- **配套工具**：`MeshCombiner`（网格融合器，合并前可自动回读 GPU 变形网格）、`Samples/throw`（砸墙示例：对象池 + 砸点叠加 + 双阈值事件）。

---

## 目录结构

```
Assets/EasyMeshDeformation/
├── MainScript/                   # 主脚本
│   ├── MeshDeformer.cs           # 主组件：两种模式（Mesh / Skinned Mesh）
│   ├── DeformerCube.cs           # 晶格：3D 控制点网格 + 选择衰减 + 生效控制点
│   └── TransformDeformer.cs      # 独立组件：对 Transform 应用晶格变形
├── Runtime/                      # 运行时代码
│   ├── Components/
│   │   ├── MeshDeformerBase.cs   # 抽象基类：网格缓冲、入队、生命周期
│   │   └── DeformerHandle.cs     # 单个控制点
│   ├── Extensions/               # 扩展方法（适配晶格、GPU 回读变形网格）
│   ├── Utility/                  # 数据结构与工具（DeformerItem/DeformerMask/MeshInfo/DeformSolver…）
│   ├── DeformFeature.cs          # 系统核心：PlayerLoop 注入 + CommandBuffer 调度
│   ├── DeformShaderProperties.cs # Compute Shader 参数与关键字
│   └── AssemblyInfo.cs           # InternalsVisibleTo（编辑器程序集）
├── Editor/                       # 编辑器代码（Inspector / Scene 视图编辑器 / 烘焙 / 设置）
├── Resources/
│   └── DeformCompute.compute     # FFD GPU 内核（Deform）
├── MeshCombiner/                 # 网格融合器（合并前可回读 GPU 变形网格）
├── Samples/
│   ├── Grab/                     # 拖拽变形示例
│   └── throw/                    # 砸墙示例：对象池 + 砸点叠加 + 双阈值事件
└── README.md
```

---

## 快速开始（Mesh 变形）

1. 创建或选择一个带 `MeshFilter` / `MeshRenderer` 的物体（如 Cube）。若面数太少，可先用 `MeshSubdivider` 细分（`细分次数` 建议 2~3）。
2. 给物体挂 **MeshDeformer** 组件
3. 在空物体上挂 **DeformerCube** 组件，把晶格摆到能包住目标物体的位置。
4. 把 DeformerCube 拖入 MeshDeformer 的晶格列表（Items）。
5. 选中 DeformerCube，对着格子点击并控制
6. 效果实时显示（编辑器与运行时一致）。

---

## GPU 回读与固定变形（MeshCombiner）

变形发生在 GPU 顶点缓冲（compute shader 每帧写入），`MeshFilter.sharedMesh` 的 CPU 数据**始终是未变形的原始网格**。若要在破碎/切割前拿到变形结果：

```csharp
using EasyMeshDeformation;

// 方式一：只拿变形后的 Mesh 副本（不影响原物体）
MeshDeformerBase deformer = GetComponent<MeshDeformerBase>();
Mesh deformed = deformer.GetDeformedMesh();          // 普通网格
// Mesh deformed = deformer.GetDeformedSkinnedMesh(); // 蒙皮网格

// 方式二：MeshCombiner 合并 + 写回 sharedMesh（一步到位，会把多网格合并为单网格）
GetComponent<MeshCombiner>().CombineMeshes(false);
```

注意：刚放置 defoCb 后需先 `yield return null` 等一帧，让 compute shader 把最新变形写入 GPU 缓冲，再回读，否则会缺失最后一帧的变形。

---

## 技术说明

### FFD 算法

- 顶点从物体空间转换到晶格局部空间，计算所在单元与单元内位置。
- 按插值方式取相邻控制点的偏移加权求和（Linear 2³=8 点 / Cubic 4³=64 点）。
- 结果再转换回物体空间写回顶点缓冲。
- Global=false 时，越界控制点偏移按 0 处理（变形在晶格外衰减）；Global=true 时钳制到边缘控制点（变形延续到框外）。

### GPU 执行链路

1. `MeshDeformer` 每帧通过 `DeformFeature.Enqueue / EnqueueSkinned` 入队。
2. `DeformFeature` 在 PlayerLoop 的 `PostLateUpdate` 注入两个系统：
   - 普通变形：在 `UpdateAllSkinnedMeshes` **之前**（先晶格变形，后蒙皮系统使用）；
   - 蒙皮变形：在 `UpdateAllSkinnedMeshes` **之后**（先 GPU 蒙皮，再施加晶格变形）。
3. 用 `CommandBuffer` 每帧：把工作副本缓冲恢复为原始顶点 → 设置关键字/参数 → `DispatchCompute` 执行 `Deform` 内核。

### 两种模式的区别

| 模式 | 目标组件 | 变形对象 | 时机 |
|---|---|---|---|
| Mesh | MeshFilter+MeshRenderer | 顶点 | 蒙皮前 |
| Skinned Mesh | SkinnedMeshRenderer | 蒙皮后顶点 | 蒙皮后 |

### 平台要求

- 需要支持 **Compute Shader** 的图形 API（DX11+、Metal、Vulkan、GLES3.1+ 等）。
- Skinned Mesh 模式需要在 Player Settings 开启 **GPU Skinning**，并对模型开启 Read/Write。

---

## 许可证与来源

根据资源 *Lattice Modifier*（Heath）改造。遵循 MIT 许可证。
