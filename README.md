# EasyMeshDeformation（晶格变形 / FFD）

基于 FFD（自由变形，Free-Form Deformation）的 Unity 网格变形系统。对原 *Lattice Modifier* 插件进行复刻与改造：统一命名（Deformer/Deform 风格）、中文化、整合两种模式（网格 / 蒙皮网格）到同一个组件、简化 Inspector、加入编辑器友好的「选择衰减」与「仅使用生效控制点」功能。

---

## 功能特性

- **FFD 晶格变形**：用 3D 控制点网格（DeformerCube）包裹物体，拖动控制点即实时变形网格。
- **两种模式（一个组件）**：`MeshDeformer` 组件通过「模式」下拉切换：
  - **网格**：变形 `MeshFilter` + `MeshRenderer` 的网格。
  - **蒙皮网格**：变形 `SkinnedMeshRenderer` 的蒙皮网格（先 GPU 蒙皮，后晶格变形）。
- **GPU / CPU 双实现**：运行时用 Compute Shader（`DeformCompute.compute`）逐帧变形，编辑器与 CPU 侧用 `DeformSolver`（等价实现，供烘焙/预览）。
- **三种插值方式**：线性（清晰）/ 线性（平滑）/ 三次方（Cubic）。
- **Global 开关**：关闭时晶格外的顶点按控制点偏移衰减（越界偏移归零）；开启时变形延续到晶格外部。
- **顶点遮罩（乘数）**：每个晶格项一个「乘数」，全局缩放变形强度（0~1）。
- **编辑器友好**：
  - Scene 视图框选控制点、扩展/收缩/反向/全选/外部控制点。
  - **选择衰减**：拖动选中点时带动周围控制点，范围与强度参数化。
  - **仅使用生效控制点**：只让选中的控制点推动变形，其余视为未动（"部分区域变形"的纯编辑器方案）。
  - 一键适配晶格到物体、烘焙静态网格、保存变形网格。

---

## 目录结构

```
Assets/EasyMeshDeformation/
├── Runtime/                      # 运行时程序集（EasyMeshDeformation.Runtime）
│   ├── Components/               # 组件
│   │   ├── MeshDeformer.cs       # 主组件：两种模式（网格/蒙皮网格）
│   │   ├── MeshDeformerBase.cs   # 抽象基类：网格缓冲、入队、生命周期
│   │   ├── DeformerCube.cs       # 晶格：3D 控制点网格 + 选择衰减 + 生效控制点
│   │   └── DeformerHandle.cs     # 单个控制点
│   ├── Extensions/               # 扩展方法（适配晶格、回读变形网格等）
│   ├── Utility/                  # 数据结构与工具（DeformerItem/DeformerMask/MeshInfo/DeformSolver…）
│   ├── DeformFeature.cs          # 系统核心：PlayerLoop 注入 + CommandBuffer 调度
│   ├── DeformShaderProperties.cs # Compute Shader 参数与关键字
│   └── AssemblyInfo.cs
├── Editor/                       # 编辑器程序集（EasyMeshDeformation.Editor）
│   ├── Editors/                  # Inspector / Scene 视图编辑器
│   ├── Utility/                  # 绘制器、Gizmo、烘焙、偏好设置等
│   ├── DeformEditorFeature.cs    # 编辑器生命周期（场景保存还原、烘焙、右键菜单）
│   ├── DeformProcessScene.cs     # 构建时烘焙静态变形
│   └── DeformSettings.cs         # 编辑器偏好（晶格颜色/大小/发光）
├── Resources/
│   └── DeformCompute.compute     # FFD GPU 内核（Deform）
└── README.md
```

另有一个独立辅助脚本（不依赖本包）：

```
Assets/MeshSubdivider.cs          # 网格细分器：把 Cube 等网格每个面中点细分加密，挂上即生效
```

---

## 快速开始（网格变形）

1. 创建或选择一个带 `MeshFilter` / `MeshRenderer` 的物体（如 Cube）。若面数太少，先挂 `MeshSubdivider` 细分（`细分次数` 建议 2~3）。
2. 给物体挂 **MeshDeformer** 组件（自动生成一个空的晶格项）。
3. 在层级中创建 **DeformerCube**（菜单 `GameObject > Effects > DeformerCube`），选中后用右键菜单「适配到 Transform...」选物体，让晶格包住它。
4. 选中 DeformerCube，在 Scene 视图中**框选控制点**，用移动/旋转/缩放工具拖动：
   - 移动单个控制点 → 局部凸起/凹陷；
   - 框选一排点整体移动 → 网格弯曲；
   - 框选全部点整体移动/旋转/缩放 → 网格整体跟随晶格姿态（平移/旋转/缩放）。
5. 效果实时显示（编辑器与运行时一致）。

---

## Inspector 字段说明

### MeshDeformer 组件

| 字段 | 说明 |
|---|---|
| **模式** | 网格 / 蒙皮网格 |
| **目标网格**（网格/蒙皮模式） | 要变形的网格；留空时自动取 MeshFilter/SkinnedMeshRenderer 的 sharedMesh |
| **更新模式** | 何时变形：手动 / 可见时（默认）/ 始终 |
| **晶格（Items）** | 晶格列表，按顺序应用；每项含： |
| &nbsp;&nbsp;晶格（Cube） | 引用的 DeformerCube |
| &nbsp;&nbsp;插值方式 | 线性（清晰）/ 线性（平滑）/ 三次方 |
| &nbsp;&nbsp;Global | 晶格外是否延续变形 |
| &nbsp;&nbsp;乘数 | 该晶格的变形强度（0~1，默认 1） |

### DeformerCube 组件

| 字段 | 说明 |
|---|---|
| **分辨率** | 每个轴的控制点数量（≥2） |
| **选择衰减** | 见下方「编辑器操作」 |
| **仅使用生效控制点** | 只让「生效控制点」列表中的点推动变形 |

---

## 编辑器操作（DeformerCube 选中时）

### 控制点选择

- **左键框选**：拖出矩形选择控制点。
- **Shift**：加选 / 减选（对称差集）。
- **右键菜单**：扩展选择 / 收缩选择 / 反向选择 / 选择全部 / 选择外部控制点 / 重置选中的控制点 / 重置全部控制点 / 复制索引等。

### 选择衰减

在 DeformerCube 上配置，编辑时拖动选中控制点会带动周围控制点：

- **是否影响**：总开关。
- **范围模式**：
  - 按分辨率比例：影响格子数 = 最大分辨率 × 比例（0~1）；
  - 手动指定：直接填影响的控制点个数。
- **影响强度**（0~1）：周围控制点的跟随程度。

### 仅使用生效控制点（部分区域变形）

无需顶点色/UV/贴图，纯编辑器操作：

1. 框选要"生效"的控制点。
2. 右键 → **将选中的控制点设为生效**。
3. 右键 → **仅使用生效控制点**（勾上）。

之后只有生效控制点推动变形，未生效的控制点（显示为灰色）不产生形变。生效列表为空时视为全部生效。

### 其它

- **适配到渲染器 / 适配到 Transform**：让晶格自动包裹目标。
- **晶格偏好设置...**：打开 `Preferences > 晶格`，调节控制点大小/颜色/线框/发光。
- 组件右键菜单：**保存变形网格... / 保存变形蒙皮网格...**（把 GPU 变形结果导出为 Mesh 资源）。

---

## 技术说明

### FFD 算法

- 顶点从物体空间转换到晶格局部空间，计算所在单元与单元内位置。
- 按插值方式取相邻控制点的偏移加权求和（线性 2³=8 点 / 三次 4³=64 点）。
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
| 网格 | MeshFilter+MeshRenderer | 顶点 | 蒙皮前 |
| 蒙皮网格 | SkinnedMeshRenderer | 蒙皮后顶点 | 蒙皮后 |

### 平台要求

- 需要支持 **Compute Shader** 的图形 API（DX11+、Metal、Vulkan、GLES3.1+ 等）。
- 蒙皮网格模式需要在 Player Settings 开启 **GPU 蒙皮**。

---

## 常见问题（FAQ）

- **Q：变形后网格疯狂发光 / 出现黑色块？**
  A：目标网格缺少切线（TBN 矩阵异常）导致。系统会自动补算切线；若仍异常，检查模型导入设置的 Tangents。

- **Q：拉控制点没效果？**
  A：确认 ① 晶格项已绑定 DeformerCube 且其激活；② 顶点在晶格范围内；③ 网格模式有 MeshFilter/MeshRenderer。

- **Q：蒙皮网格模式报"GPU 蒙皮未启用"？**
  A：在 `Edit > Project Settings > Player > Other Settings` 勾选 GPU Skinning。

---

## 许可证与来源

本项目为学习/复刻用途，基于开源资源 *Lattice Modifier*（Heath）改造：重命名标识符、中文化、整合模式、简化 Inspector 并新增编辑器工具（选择衰减、生效控制点）。请遵循原资源许可证。
