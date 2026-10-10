# MRes Packer 制作工具

本目录是独立的资源制作模块，包含 Editor 打包器和一个完整弹射台示例。整个 `MResPacker` 文件夹可复制到另一个 Unity 项目的 `Assets` 下使用。`MResPacker.Editor.asmdef` 仅包含 Editor 平台，**没有引用游戏程序集、MRes Loader 或 xLua**。工具读取 Lua 文本，不执行 Lua。

最低验证环境：Unity 2022.3。Editor 需要启用图形设备，因为不可读或压缩贴图通过 GPU 读回。测试使用 Unity Test Framework；打包器本身没有第三方依赖。Unity 6 的物理材质/阻尼 API 使用条件编译兼容，尚未在 Unity 6 实测。

## 目录

```text
MResPacker/
├─ Editor/                         独立 Editor 程序集、窗口、序列化器、Writer、校验
├─ Authoring/
│  ├─ Scenes/MResAuthoring.unity    制作场景：弹射台实例与相机
│  ├─ Prefabs/BouncePad.prefab      普通源 Prefab，无游戏脚本或 Lua 组件
│  ├─ Prefabs/AuthoringCamera.prefab
│  ├─ Models/                     可读 Mesh .asset，多 SubMesh 示例
│  ├─ Materials/                  两种材质和共享 Physics Material
│  ├─ Textures/                   棋盘格 PNG，特意关闭 Read/Write
│  ├─ Shaders/                    制作端示例 Shader，不打进 .mres
│  ├─ Lua/BouncePad.lua            独立、可直接编辑的 Lua 源文件
│  ├─ Data/BouncePad.device.json   可选游戏参数，作为 Other 条目打包
│  └─ Profiles/                   打包配置和 Shader → shaderId 注册表
├─ Output/BouncePad.mres           示例成品
└─ Tests/Editor/                  独立验收测试
```

制作场景用于观察外观和编辑布局，打包器读取的是配置中的 **Prefab 资产**。在场景实例上修改后，需要 Apply 到源 Prefab，或者直接进入 Prefab Mode 编辑。制作场景不会执行 Lua，也不是游戏关卡；相机不属于待打包的弹射台 Prefab。

## 使用

1. 打开 `Tools > MRes > Packer`，选择 `Authoring/Profiles/BouncePad.asset`。
2. 编辑源 Prefab、模型、材质或 `Authoring/Lua/BouncePad.lua`。
3. 在窗口设置 Source Prefab、Lua Script、Shader ID Registry，以及 Metadata 的 Name / Description / Value / Type。
4. 设置输出 `.mres` 路径，点 **Analyze** 查看节点、资源、碰撞体、Rigidbody 数量、Lua、Metadata、Shader IDs、不支持组件和预计文件大小。
5. 分析通过后点 **Build MRes**。构建会重新读取最新源文件，不复用过期的分析结果。

配置保存在项目内的 `.asset` 中。可以通过窗口“新建打包配置”，或 `Assets > Create > MRes Packer` 创建 Profile / Shader Registry。`Tools > MRes > Create or Locate Packer Example` 用于生成或定位示例；存在的示例配置不会被覆盖。

同一 Mesh、Material、Texture2D 或 Physics Material 按 Unity 对象身份去重。材质槽顺序和空槽、子节点顺序、局部 Transform、active 状态、layer、碰撞体与 Rigidbody 参数均保留。

## Lua 与游戏接入

Lua 文件在制作目录中独立存在，打包后嵌入 `scripts/main.lua`，由 `manifest.json` 的 `lua` 字段引用。输出为严格 UTF-8，无 BOM。Lua 可省略；脚本内容及语法由目标运行时解释，Packer 不创建 `LuaEnv`。

本示例使用当前游戏提供的接触 API：

```lua
function OnContact(api)
    if not api:Trigger(0) then return end
    local motion = api.Motion
    local source = motion.Source
    motion:TryLaunch(source, source.Forward, math.max(motion.Speed, api.Strength))
end
```

`OnContact` / `api.Motion` 属于游戏的 Lua API。其他使用方可以改成目标 Loader 约定的 `Start`、`Update`、`OnCollisionEnter` 等函数；打包器代码无需更改。

在本游戏中使用示例成品：

1. 先在目标 `Resources/Devices/ShaderRegistry.asset` 注册 `MRes/ExampleSurface`，指向本目录的 `Authoring/Shaders/ExampleSurface.shader`。目标也可以使用另一个遵守相同属性契约的 Shader。
2. 将 `Output/BouncePad.mres` 复制到 `Resources/Devices`，等待目录自动刷新。示例 ID 为 **9000**，售价为 120，开局赠送为 0，会加入商店候选。
3. 游戏需要包内 `game/device.json`。示例配置通过“附加业务文件”将 `Authoring/Data/BouncePad.device.json` 打包为该路径；其类型为 `Other = 255`，与四字段 Metadata 分离。

工具不会自动修改游戏场景、商店、资源目录或运行时 Shader Registry。`Output` 是制作端成品目录；本次示例尚未安装进游戏。

## 支持与检查

- 支持 Transform、MeshFilter、MeshRenderer、Box / Sphere / Capsule / MeshCollider、Rigidbody。
- 拒绝 Prefab Variant（包含嵌套 Variant）、丢失脚本及任何非白名单组件，包括自定义 MonoBehaviour、MRes/xLua 组件、Animator、SkinnedMeshRenderer、粒子等。
- 只接受普通 `.prefab` 资产作为入口。Prefab 可以引用从模型导入的 Mesh，但包中只保存 meshbin，不保存 FBX / OBJ 或 Unity 原始资产。
- Mesh 必须可读。拒绝骨骼数据、BlendShape、非法拓扑、越界索引及非有限值。UV0/UV1 使用二维通道。
- Shader 必须显式注册，无自动替换。支持 Float / Range / Int / Color / Vector / Texture2D 属性、关键词和材质设置；非 Texture2D 属性会报错。
- 贴图转换为 RGBA32 PNG，保存色彩空间、wrap U/V、filter、aniso、mipmap 设置。不会修改源贴图的 Read/Write 开关。
- Metadata 严格只有 `name`、`description`、`value`、`type`；名称和描述非空，价值有限且 ≥ 0，类型只能为 `medicine` / `device`。本游戏的装置售价另要求为正整数。
- 非凸 MeshCollider 不能是 Trigger，不能附着到动态 Rigidbody；运动学 Rigidbody 检测模式必须为 Discrete / ContinuousSpeculative。

## 与现有 v1 格式对齐

Writer 使用 28 字节 MRES Header、little-endian TOC、类型 1～8 / 255、Compression / Reserved = 0；不增加 SHA / CRC / Hash 字段。Mesh 使用现有 **MMSH v1** 二进制布局。

原需求中的部分 JSON 是示意形式；实际输出采用已实现的 v1 契约：

- Vector / Quaternion 为 `{x,y,z,w}` 对象，Color 为 `{r,g,b,a}`。
- 材质 Texture 属性的内部路径存于 `value`；另存 `scale` / `offset`。
- 枚举写 Unity 整数；例如物理材质 Combine 为 Average=0、Multiply=1、Minimum=2、Maximum=3。
- 贴图设置放在 Manifest 的 `textureSettings[path]` 中，mipmap 字段名为 `mipChain`。
- 原网格名称保存在 Manifest 的 `meshNames[path]` 中，以兼容既有 MMSH v1 字节布局。现有 Loader 仍使用网格虚拟路径作为运行时 Mesh 名称；该附加信息不影响几何恢复。
- Collider 始终包含 contactOffset、Physics Material 引用、include/excludeLayers、layerOverridePriority、providesContacts、hasModifiableContacts 等公共字段。旧 Unity 不支持的 API 由版本宏填默认值。
- 包最大 256 MiB，最多 65536 条目、10000 节点；单 Mesh 最多 100 万顶点 / 600 万索引 / 1024 SubMesh。PNG 最大 8192×8192，且像素数不超过 16777216。

运行时对象、源 GUID、AssetDatabase 路径、源文件绝对路径不会作为资源引用写入成品。制作端的 `.prefab` / `.asset` / `.unity` 则正常使用 Unity GUID 管理本目录内的源资产。

## 写入与验证

构建先在输出目录写唯一 `.tmp`，写完 TOC 和 Header 后 Flush，再独立读回检查 Header、范围、重叠、重复路径、层级和资源引用；通过后才将文件替换为 `.mres`。失败会删除临时文件，已有的成功成品保持不变。

在 Unity Test Runner 的 EditMode 中运行 `MResPacker.Tests`，覆盖需求 A–K，并验证非可读贴图、颜色保持、UInt32 网格、非法输入、资源去重与失败写入。测试不引用 Loader。运行图形测试时不要使用 `-nographics`。

本次在仅包含本制作目录的独立 Unity 2022.3 项目中通过 **20/20** 项测试（D3D11 / Linear）。另行验证了现有 Loader 对示例成品的读取、重建、物理参数与可见渲染，以及制作场景和资源依赖完整性；用于兼容性检查的 Loader 副本只在工作目录中，不属于本工具。

批处理示例（日志和测试结果路径请使用项目允许的工作目录）：

```text
Unity.exe -batchmode -force-d3d11 -projectPath <制作项目>
  -runTests -testPlatform EditMode -testFilter MResPacker.Tests
  -testResults <工作目录>/packer-tests.xml -logFile <工作目录>/packer-tests.log
```

`MResPacker.MResExampleCreator.CreateAndBuild` 是生成示例和成品的批处理入口。当前交付只包含一个完整制作示例，现有 15 个游戏装置仍使用原来的运行时 `.mres`。
