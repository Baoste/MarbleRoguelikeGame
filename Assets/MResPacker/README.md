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
3. 在窗口设置 Source Prefab、Lua Script、Shader ID Registry，以及 Metadata 的名称、描述、出售价格（value）、购买价格（purchasePrice）、稀有度（rarity）、耐久（durability）和类型。
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
2. 将 `Output/BouncePad.mres` 复制到 `Resources/Devices`，等待目录自动刷新。示例 ID 为 **9000**，购买价为 5、出售价格为 120，开局赠送为 0，会加入商店候选。
3. 游戏需要包内 `game/device.json`。示例配置通过“附加业务文件”将 `Authoring/Data/BouncePad.device.json` 打包为该路径；其类型为 `Other = 255`，与七字段 Metadata 分离。

工具不会自动修改游戏场景、商店、资源目录或运行时 Shader Registry。`Output` 是制作端成品目录；当前已将示例安装为 `Resources/Devices/9000_BouncePad.mres` 并注册所需 Shader。运行目录包含本工具的 14 个制作成品和 `113_MultiDing.mres`，合计 15 种装置。

## 支持与检查

- 支持 Transform、MeshFilter、MeshRenderer、Box / Sphere / Capsule / MeshCollider、Rigidbody。
- 拒绝 Prefab Variant（包含嵌套 Variant）、丢失脚本及任何非白名单组件，包括自定义 MonoBehaviour、MRes/xLua 组件、Animator、SkinnedMeshRenderer、粒子等。
- 只接受普通 `.prefab` 资产作为入口。Prefab 可以引用从模型导入的 Mesh，但包中只保存 meshbin，不保存 FBX / OBJ 或 Unity 原始资产。
- Mesh 必须可读。拒绝骨骼数据、BlendShape、非法拓扑、越界索引及非有限值。UV0/UV1 使用二维通道。
- Shader 必须显式注册，无自动替换。支持 Float / Range / Int / Color / Vector / Texture2D 属性、关键词和材质设置；非 Texture2D 属性会报错。
- 贴图转换为 RGBA32 PNG，保存色彩空间、wrap U/V、filter、aniso、mipmap 设置。不会修改源贴图的 Read/Write 开关。
- Metadata 输出 `name`、`description`、`value`、`purchasePrice`、`rarity`、`durability`、`type` 七字段；名称和描述非空，出售价格 `value` 有限且 ≥ 0，购买价格 `purchasePrice` 有限且 ≥ 1，装置价格为整数。类型只能为 `medicine` / `device`。
- 稀有度为 `common`（普通）、`rare`（稀有）、`epic`（史诗）、`legendary`（传奇），现有包与新配置默认普通；购买价默认 5。耐久默认 20，**`-1` 代表无限耐久**，其余合法值为整数 1～10000。新配置和包显式保存，兼容读取缺少新字段的旧包时补默认值。
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

`MResPacker.MResExampleCreator.CreateAndBuild` 是生成弹射台示例和成品的批处理入口。

`MResPacker.MResBuildPipeline.BuildAuthoringProfiles` 批量重新打包 `Authoring/Profiles` 下的全部制作配置。构建包含不可读贴图时应启用图形设备，不使用 `-nographics`。

## 游戏价格与耐久

当前全部装置基础购买价为 5，`value` 保留原有数值并用于出售单个实例，旧购买价乘出售比例的计算已移除。传送门购买 5 获得配对的两扇门，每扇单独出售获得 15；两扇门耐久分别计算。商店原有价格修正继续生效。

有限耐久按本回合有效作用扣除：普通即时效果每次触发扣 1，持续力场每球每次进入扣 1。储存类在释放效果时计数：黑洞点击启动一次引力扣 1，吸吸一批释放扣 1，大炮一批成功发射扣 1，黏黏每颗释放扣 1；储存、充能、取消和空点击不扣。最后一次仍完成整批或完整持续时间，然后本回合停用；新回合恢复，`-1` 无限。详细字段、显式登记与按批去重规则见 [装置 Lua API](../Docs/Device_Lua_API.md)。

2026-10-11 储存类耐久规则更新：上述四份 Lua 与 MRes 重新打包，当前全部 10 个运行时包验证通过。Unity 玩法回归 **1399** 条断言通过，包含元数据/耐久 **166** 条；黏黏 **186**、吸吸 **257**、黑洞 **415**、大炮 **381** 条专项通过，其他装置回归继续通过。覆盖免费储存/充能、整批扣一次、最终效果完整结束、出口受阻重试、跨回合/无限耐久及状态显示。下方较早记录保留当时的断言和资源数量。

2026-10-11 元数据与耐久验证：7 份制作配置重新构建；20 个游戏包验证通过；Loader / Packer EditMode **53/53** 通过。玩法回归 **1437** 条断言（含元数据、买卖和耐久 **178** 条），另有黏黏 **183**、弹弹 **499**、吸吸 **244**、力场 **339**、传送门 **160**、黑洞 **230** 条专项断言通过。覆盖默认 20 次、无限耐久 -1、回合恢复、最后一次滞留完成、力场停留不重复扣除和黑洞失效后无残留力。

## 黏黏

`Authoring/Profiles/Sticky.asset` 是黏黏的制作配置。打开 `Tools > MRes > Packer`，选择该配置后 Analyze / Build MRes，输出 `Output/Sticky.mres`。菜单 `Tools > MRes > Create or Locate Sticky Device` 定位配置；批处理入口为 `MResPacker.MResStickyCreator.CreateAndBuild`，已有源资产不会被生成器覆盖。

- 模型：`Authoring/Models/StickyCylinder.asset`，直径 0.4、高 0.1 的薄圆柱；Prefab 使用凸 MeshCollider 触发器，不会先把弹珠反弹走。
- Prefab / 场景：`Authoring/Prefabs/Sticky.prefab`、`Authoring/Scenes/StickyAuthoring.unity`。
- Lua：`Authoring/Lua/Sticky.lua`。`OnContact` 用 `TryPrepareTrigger` 检查触发，调用独立的 `Motion:TryHold`，不扣耐久；`OnHoldEnd` 读取 `Hold.EntrySpeed` 再调用 `TryLaunch` 沿棋盘下坡释放，成功后 `TryUseOnce` 扣 1。出射受阻时改用 `TrySetVelocity` 原地向下释放。有限耐久时限制待释放数量，取消或球消失不扣。
- 参数：`Authoring/Data/Sticky.device.json` 的 `Value = 1` 是滞留秒数；`MaxTriggersPerBall = 1` 限制同球对同装置一次。ID 9001，初始数量 0，购买价 5、出售价格 120。
- 材质采用 URP/Lit，制作项目需要安装 URP。独立映射 `Authoring/Profiles/StickyShaderRegistry.asset` 将它映射为游戏已有的 `device-lit`，包内不携带 shader 源码。
- URP 材质中未赋值的全局 Lightmap 贴图数组不导出；实际赋值的非 Texture2D 贴图仍会报错，避免丢失源资源。

运行时副本为 `Assets/Resources/Devices/9001_Sticky.mres`，会进入游戏资源目录与商店候选池。重新打包后将成品覆盖到此位置，Unity 自动重建目录。完整引擎 API、滞留和失败规则统一维护在 `Assets/Docs/Device_Lua_API.md`；MResPacker 的 C# 制作工具不引用游戏运行时。

2026-10-11 验证：打包器 EditMode 测试 **21/21** 通过；黏黏真实 PhysX 专项 **153** 条断言通过，现有玩法回归 **753** 条断言通过；资源目录验证覆盖全部 **16** 个包。专项包括实际圆柱触发、零速/低速、满 1 秒滞留、倾斜棋盘下坡出射、角速度恢复、阻挡回退、独立多球计时、装置移除和对象池清理。

同日完成基础 API 拆分后重新打包并验证：黏黏及组合能力专项 **177** 条断言、玩法回归 **753** 条断言通过。补充了无回调时恢复原运动、结束后修改属性/继续滞留/消耗弹珠、独立上下文有效期、实时出口朝向和 Lua 异常隔离；验证日志中的 `EXPECTED_HOLD_COMPLETION_FAILURE` 是主动注入的测试异常。后续装置 API 按 `Assets/AGENTS.md` 与统一 API 文档中的职责单一、Lua 组合标准设计。

## 弹弹

选择 `Authoring/Profiles/Reflector.asset` 进行 Analyze / Build MRes，输出 `Output/Reflector.mres`。菜单 `Tools > MRes > Create or Locate Reflector Device` 定位制作配置；批处理入口为 `MResPacker.MResReflectorCreator.CreateAndBuild`。

- Lua：`Authoring/Lua/Reflector.lua`，独立组合 `Impact` 碰撞快照、`Vectors` 向量计算、`RandomRange` 随机数和 `Motion.TrySetVelocity`。
- 参数：`Authoring/Data/Reflector.device.json` 中 `Value = 30` 表示镜面反射方向附近 ±30°；改为 0 则严格镜面反射。保留入射速度大小，擦边时裁剪为朝向表面外侧的有效角度。
- 模型/材质：`Authoring/Models/ReflectorCylinder.asset`、`Authoring/Materials/ReflectorCoral.mat`；直径 0.4、高 0.2 的珊瑚色圆柱，URP/Lit 映射为游戏已有的 `device-lit`。
- Prefab/场景：`Authoring/Prefabs/Reflector.prefab`、`Authoring/Scenes/ReflectorAuthoring.unity`。使用实体凸 MeshCollider，以获取真实碰撞法线。
- 游戏副本：`Assets/Resources/Devices/9002_Reflector.mres`，ID 9002，购买价 5、出售价格 120、初始库存 0、每球触发次数不限。重新打包后同步此副本，Unity 自动重建资源目录。

完整接口与边界规则见 `Assets/Docs/Device_Lua_API.md` 的“弹弹”章节。

2026-10-11 验证：弹弹专项 **499** 条断言通过；包含弹弹在内的玩法验证 **1253** 条断言通过，黏黏组合能力专项 **177** 条继续通过；资源目录验证覆盖全部 **17** 个包。真实 PhysX 测试使用零自然弹性圆柱接触，确认 Lua 使用入射快照恢复速度，并在倾斜棋盘上按指定反射范围弹出。

## 吸吸

选择 `Authoring/Profiles/Collector.asset` 打包到 `Output/Collector.mres`。菜单 `Tools > MRes > Create or Locate Collector Device` 定位配置；批处理入口为 `MResPacker.MResCollectorCreator.CreateAndBuild`。

- Lua：`Authoring/Lua/Collector.lua`。首球进入范围启动磁力，附近弹珠逐步聚集，在共同截止时间向下释放。Lua 分别使用更新事件、向量运算、速度设置、位置对齐、滞留和完成回调，没有专用的 C# 吸吸效果函数。
- 参数：`Authoring/Data/Collector.device.json` 的 `Value = 2` 是磁力秒数，`Range = 1.2` 是吸引半径，`Strength = 3` 是收拢速度。后来者不延长时限；各自保留进入时速度，释放后同一个装置不再抓回该弹珠。
- 模型/材质：`Authoring/Models/CollectorCylinder.asset`、`Authoring/Materials/CollectorViolet.mat`；直径 0.4、高 0.1 的紫色圆柱，URP/Lit 使用 `device-lit` 映射。
- Prefab/场景：`Authoring/Prefabs/Collector.prefab`、`Authoring/Scenes/CollectorAuthoring.unity`。圆柱使用凸 MeshCollider 触发器，作用范围由 Lua 读取配置判断。
- 游戏副本：`Assets/Resources/Devices/9003_Collector.mres`，ID 9003，购买价 5、出售价格 120、初始库存 0，自动加入商店候选。重新打包后同步此文件，Unity 自动重建资源目录。

聚集位置会按弹珠半径留出间隔；已聚集的弹珠原地向下释放，避免统一出口造成重叠。目标受阻或来不及抵达的弹珠到时也会结束吸引。聚集不扣耐久，释放时按同一批次 key 调用 `TryUseOnce`，整批仅扣 1；最后一点耐久也完成尚未聚拢的成员。完整契约见 `Assets/Docs/Device_Lua_API.md` 的 `OnBallUpdate` 与“吸吸”章节。

2026-10-11 验证：吸吸专项 **244** 条断言通过；玩法回归 **1254** 条（包含弹弹 **499** 条）、黏黏组合能力 **177** 条通过；资源目录验证覆盖全部 **18** 个包。多球案例使用真实 PhysX 运动，覆盖不同半径、共同截止时间、后来者、零速/低速/限速、聚集间距、受阻释放、取消及跨回合清理。

## 加速力场

选择 `Authoring/Profiles/AccelerationField.asset` 构建到 `Output/AccelerationField.mres`。菜单 `Tools > MRes > Create or Locate Acceleration Field Device` 定位配置；批处理入口为 `MResPacker.MResAccelerationFieldCreator.CreateAndBuild`。

- Lua：`Authoring/Lua/AccelerationField.lua`，每个物理子步分别调用 `OverlapsBox("Field")`、`TryUse()` 与 `TryAddForce(api.Forward * api.Strength)`。区域判定和施力均为独立、可复用的引擎 API。
- 力度：`Authoring/Data/AccelerationField.device.json` 的 `Strength = 3`，表示 3 N 恒力。质量越大加速度越小，离开区域后停止加力，保留已获得的速度；旋转装置改变力的方向。
- 区域：`Authoring/Prefabs/AccelerationField.prefab` 的 `Field` 子节点，默认缩放 `(0.4, 0.24, 1.2)`，BoxCollider 是 Trigger。尺寸和几何来源于这个节点，不在 Lua 中重复写死；编辑它的缩放/Collider 后重新打包，边框与箭头可同步调整。
- 模型/材质：`Authoring/Models/AccelerationFieldBox.asset`、`Authoring/Materials/AccelerationFieldGlass.mat` 和 `AccelerationFieldCyan.mat`。透明蓝色长方形体积、细边框及三组 -Z 方向箭头，URP/Lit 通过独立 Registry 映射为 `device-lit`。
- 场景：`Authoring/Scenes/AccelerationFieldAuthoring.unity`。源模型、材质、Prefab、Lua、制作配置均位于本制作目录。
- 游戏副本：`Assets/Resources/Devices/9004_AccelerationField.mres`，ID 9004，购买价 5、出售价格 120、初始库存 0，会加入商店候选。`Radius = 0.633` 是矩形外接圆的放置半径；调整长宽时同步更新，实际力场始终按 BoxCollider 判断。重新打包后同步游戏副本。

完整 API 契约、质量/时间积分、重叠/边缘规则和通用限力说明见 `Assets/Docs/Device_Lua_API.md` 的“持续力事件”和“加速力场”章节。

2026-10-11 验证：加速力场专项 **146** 条断言、玩法回归 **1255** 条、黏黏 **177** 条、吸吸 **244** 条通过；资源目录覆盖全部 **19** 个包。专项通过实际 PhysX 验证两种质量、两种固定步及多个物理子步的 `Δv = F × t / mass`，以及旋转矩形边缘/角点、区域缩放、叠加、限力限速、离开/重入、禁用和滞留清理。旧磁铁的径向施力回归通过。

## 传送门

选择 `Authoring/Profiles/Portal.asset` 构建到 `Output/Portal.mres`。菜单 `Tools > MRes > Create or Locate Portal Device` 定位配置；批处理入口为 `MResPacker.MResPortalCreator.CreateAndBuild`。

- Lua：`Authoring/Lua/Portal.lua`。独立组合 `FindPartner`、向量投影和 `TrySetPosition`，只改变位置，保留传送前完整世界速度向量、旋转和角速度。出口朝向只在零速/平面投影近零时帮助选择放置位置，不重定向速度。
- 配对：`Authoring/Data/Portal.device.json` 的 `PurchaseQuantity = 2`，购买一次得到两个同 `PairId` 的装置，每批购买单独配对，可以分别放置、双向传送。沿用 ID 103、购买价 5（整对）、出售价格 15（单扇）、初始库存 0，不新增重复的传送门商品。
- 模型/材质：`Authoring/Models/PortalCylinder.asset`、`PortalRim.asset`；`Authoring/Materials/PortalIndigo.mat`、`PortalRim.mat`。直径 0.24、高 0.08 的靛蓝圆柱及发光圆环，URP/Lit 映射为 `device-lit`。
- Prefab/场景：`Authoring/Prefabs/Portal.prefab`、`Authoring/Scenes/PortalAuthoring.unity`。单个制作 Prefab 表示一扇门，成对创建和匹配由游戏通用商店/库存负责；使用凸 MeshCollider 触发器。
- 游戏成品覆盖 `Assets/Resources/Devices/103_Portal.mres`。成功传送锁 0.5 秒；沿用 `Cooldown = 0.5`、`MaxTriggersPerBall = 2` 的配对访问规则。

自身重力、质量等属性保持；区域力在出口按实际环境重新计算，不会把入口的力场加速度带到场外。当前传送门不应用旧 `ExitDown` 下坡覆盖。`TryTeleport` 的旧出射方向 API 契约仍保留，新效果调用独立的 `TrySetPosition`。完整位置计算、阻挡失败和加速度规则见 `Assets/Docs/Device_Lua_API.md`。

2026-10-11 验证：传送门专项 **160** 条断言通过，玩法回归 **1258** 条通过，黏黏 **177** 条、吸吸 **244** 条、加速力场 **146** 条继续通过；全部 **19** 个包通过资源验证。专项包含实际商店事务、两批独立配对、库存只剩一个位置时拒绝半对、真实圆柱双向穿越，以及入口/出口有无加速力场的四种组合。

## 黑洞

选择 `Authoring/Profiles/BlackHole.asset` 构建到 `Output/BlackHole.mres`。菜单 `Tools > MRes > Create or Locate BlackHole Device` 定位配置；批处理入口为 `MResPacker.MResBlackHoleCreator.CreateAndBuild`。

- Lua：`Authoring/Lua/BlackHole.lua`。吞噬 10 颗弹珠只充能、不计分；满充等待点击，额外的弹珠穿过。左键点击装置本体释放 10 秒全局径向引力，期间进入的弹珠按当前价值 ×10 直接结算。到期后可再次吞噬 10 颗充能。
- 参数：`Authoring/Data/BlackHole.device.json` 的 `Value = 10` 是持续秒数，`ScoreMultiplier = 10` 是结算倍率，`Strength = 8` 是最大引力 N，`Range = 1` 是半强度距离。衰减公式 `Strength / (1 + (Distance / Range)^2)` 写在 Lua 中，不设最远距离；充能颗数为 Lua 顶部 `chargeRequired`。
- 模型/材质：`Authoring/Models/BlackHoleCylinder.asset`、`BlackHoleRim.asset`；`Authoring/Materials/BlackHoleCore.mat`、`BlackHoleRim.mat`。直径 0.4、高 0.08 的黑色圆柱和紫色发光圆环，URP/Lit 映射为 `device-lit`。
- Prefab/场景：`Authoring/Prefabs/BlackHole.prefab`、`Authoring/Scenes/BlackHoleAuthoring.unity`。圆柱使用凸 MeshCollider Trigger；表现资源和制作代码均在 MResPacker 内。
- 游戏成品：`Assets/Resources/Devices/9005_BlackHole.mres`，ID 9005，购买价 5、出售价格 120、一次购买一个、初始库存 0，自动加入商店候选。重新打包后将 Output 成品同步到该路径。

引擎提供独立接口：`OnTick` / `OnActivate`、`SetStatus`、`TryConsume(false)`、`TrySettleAmount(score)`、`TryUse()`、`TryKeepActive(seconds)` 与 `AddRadialForce`。黑洞 Lua 组合充能、激活、计时、结算；吞下充能球不扣，点击启动引力扣 1，期间吞球不再扣。最后一次激活仍完整持续 10 秒。计时使用模拟时钟，没有球也会到期，暂停不倒计时，重复点击不刷新或重复扣除。每实例独立，新回合重新充能。直接结算不叠加机器/全局/药物/Rush 倍率，计分事件、小数累计、分数上限与回收由引擎处理。

接口契约见 `Assets/Docs/Device_Lua_API.md` 的“无当前弹珠的装置事件”和“黑洞”章节。真实物理专项验证位于 `Assets/Editor/Validation/BlackHoleDeviceValidation.cs`，由 `Marbles ECS/Validate Attribute and Content Runtime` 一并执行。

2026-10-11 验证：黑洞专项 **195** 条断言、玩法回归 **1259** 条断言全部通过；黏黏 **177**、弹弹 **499**、吸吸 **244**、加速力场 **146**、传送门 **160** 条继续通过，资源目录覆盖全部 **20** 个包。成品与游戏副本 SHA-256 一致。

## 反重力

选择 `Authoring/Profiles/AntiGravity.asset` 构建到 `Output/AntiGravity.mres`。菜单 `Tools > MRes > Create or Locate AntiGravity Device` 定位配置；批处理入口为 `MResPacker.MResAntiGravityCreator.CreateAndBuild`。

- Lua：`Authoring/Lua/AntiGravity.lua`，独立组合 `TryLaunch` 与 `TrySetGravityScale(-1, seconds, true)`。从棋盘上坡一侧射出，保留进入速度大小，2 秒后或接触另一装置实例时恢复重力。
- 参数：`Authoring/Data/AntiGravity.device.json` 的 `Value = 2` 控制模拟秒数；每球每个实例最多触发一次。普通固定钉和来源装置自身不提前取消。
- 模型/材质：`Authoring/Models/AntiGravityCylinder.asset`、`Authoring/Materials/AntiGravityGold.mat`，金色圆柱，直径 0.4、高 0.1，凸 MeshCollider Trigger，URP/Lit 使用 `device-lit`。
- Prefab/场景：`Authoring/Prefabs/AntiGravity.prefab`、`Authoring/Scenes/AntiGravityAuthoring.unity`。
- 游戏成品：`Resources/Devices/9006_AntiGravity.mres`；普通、耐久 20、购买价 5、出售价 120、初始库存 0。重新打包后将 Output 成品复制到此路径，目录索引和 GameContent 随 Unity 导入自动同步。

通用重力 API 不移动弹珠、不改变基础属性，不包含装置名称或出射方向。引擎只维护临时倍率、计时、接触取消和清理；具体组合在 Lua 中。到期恢复当前重力属性，不覆盖期间获得的修正；来源移除、弹珠消失、对象池复用、回合重置均有清理。接口契约见 `Docs/Device_Lua_API.md`。

2026-10-11 验证：反重力专项 **79** 条断言、玩法回归 **1355** 条断言通过，全部 **9** 个运行时包加载通过，已有装置专项继续通过。覆盖真实物理的上升加速度、两种固定步、2 秒完整时长、其他装置接触（含仅力场回调的装置）、当前属性恢复、阻挡出口、独立多球、最后一次耐久、滞留组合和对象池清理。

## 大炮

选择 `Authoring/Profiles/Cannon.asset` 构建到 `Output/Cannon.mres`。菜单 `Tools > MRes > Create or Locate Cannon Device` 定位配置；批处理入口为 `MResPacker.MResCannonCreator.CreateAndBuild`。

- Lua：`Authoring/Lua/Cannon.lua`，每 0.5 秒绕棋盘法向转 45°，最多存 5 颗，点击时锁定可见朝向，以速度 6 将已存球全部排开发射。
- 参数：`Authoring/Data/Cannon.device.json` 的 `Value = 5` 为容量，`Range = 0.5` 为旋转间隔秒数，`Strength = 6` 为发射速度。
- 模型/材质：`Authoring/Models/CannonCylinder.asset`；`Authoring/Materials/CannonBronze.mat` 与 `CannonBore.mat`。圆柱底座直径 0.5、高 0.2，带朝本地 -Z 的炮管和深色炮口，整体在放置半径 0.25 内；底座为凸 MeshCollider Trigger。
- Prefab/场景：`Authoring/Prefabs/Cannon.prefab`、`Authoring/Scenes/CannonAuthoring.unity`。
- 游戏成品：`Resources/Devices/9007_Cannon.mres`。普通、耐久 20、购买价 5、出售价 120、初始库存 0；打包后复制到游戏目录，索引和 GameContent 由 Unity 自动同步。

基础 API 分别负责手动滞留 `TryHoldUntilReleased()`、实时数量 `HeldCount`、解除滞留 `ReleaseHeld()`、绝对旋转偏移 `TrySetYaw(degrees)`；Lua 组合 `TryMove` 计算存放与发射位置。容量、角度和批量间距均在 Lua。存入不扣耐久，每批首次成功发射用 `TryUseOnce` 扣 1，整批其余成员及受阻重试不再扣。全部受阻或空点击不扣；等待重试不接收新球。最后一点耐久仍能完成五球，受阻留存等待再次点击。满仓球穿过；正常寿命与回合清理继续生效。

2026-10-11 验证：大炮专项 **367** 条断言通过，玩法回归 **1361** 条断言通过，全部 **10** 个运行时包加载通过，原有装置专项继续通过。覆盖倾斜棋盘八向与炮口一致性、五球间距、同帧六球容量、真实触发和点击、最后耐久完成、阻挡重试、独立实例、寿命/对象池/跨回合清理，以及缺失或异常结束回调。`EXPECTED_CANNON_COMPLETION_FAILURE` 为故意注入的异常隔离测试。

## 沙包

选择 `Authoring/Profiles/Sandbag.asset` 进行 Analyze / Build MRes，输出 `Output/Sandbag.mres`。菜单 `Tools > MRes > Create or Locate Sandbag Device` 定位制作配置；批处理入口为 `MResPacker.MResSandbagCreator.CreateAndBuild`，已有源资产不会被覆盖。

- Lua：`Authoring/Lua/Sandbag.lua`，每次真实撞击连击数 +1，给当前弹珠增加 `连击数 × 基础值`；不同球共享该沙包的连击，不同沙包独立。首击 +1、第二击 +2；每次刷新窗口 `max(0.3, 3 / √连击数)` 秒，超时清零。已得加值保留到弹珠正常结算。
- 参数：`Authoring/Data/Sandbag.device.json` 中 `Value = 1` 为基础加值，`Range = 3` 为初始窗口秒数，`Strength = 0.3` 为最短窗口秒数。`Cooldown = 0`、`MaxTriggersPerBall = 0`；同球可再次撞击，不用公共冷却阻挡同一步的其他球。
- 模型/材质：`Authoring/Models/SandbagCylinder.asset`，棕黄色 `SandbagCanvas.mat` 配红褐色 `SandbagBand.mat`，圆柱直径 0.4、高 0.2；URP/Lit 映射到 `device-lit`。
- Prefab/场景：`Authoring/Prefabs/Sandbag.prefab`、`Authoring/Scenes/SandbagAuthoring.unity`，使用实体凸 MeshCollider，不改写弹珠的物理反弹。
- 运行时包：`Resources/Devices/9008_Sandbag.mres`。普通、耐久 20、购买价 5、出售价 120、初始库存 0；每次有效撞击扣 1，第 20 次仍生效，`-1` 无限。

连击、缩短窗口与状态文字由 Lua 的 `OnContact` / `OnTick` 实现。新通用接口 `AddBase(attribute, amount)` 只负责增减基础属性，保留现有倍率且避免重复放大；碰撞、`TryUse`、时钟和 `SetStatus` 复用既有 API。修改参数后重新打包并同步运行时包，Unity 将完整刷新装置目录与 `GameContent.json.Devices`。统一文档见 [装置 Lua API](../Docs/Device_Lua_API.md)。

2026-10-11 验证：沙包专项 **188** 条断言、玩法回归 **1593** 条断言通过，全部 **11** 个运行时包加载验证通过。专项覆盖连击递增、共同续连、同帧不同球、超时及 0.3 秒下限、跨实例/回合状态、有限与无限耐久、已有倍率下的基础加值、正常结算和实际 PhysX 碰撞进入/持续贴住。

## 大石碎胸口

选择 `Authoring/Profiles/StoneBurst.asset` 进行 Analyze / Build MRes，输出 `Output/StoneBurst.mres`。菜单 `Tools > MRes > Create or Locate Stone Burst Device` 定位制作配置；批处理入口 `MResPacker.MResStoneBurstCreator.CreateAndBuild`，保留已有制作资源。

- Lua：`Authoring/Lua/StoneBurst.lua`。真实撞击累计 8 次后，向棋盘平面四周每隔 22.5° 发射一颗，共 16 颗；输入球正常反弹，不消耗，输出球各价值 1、速度 6、不扣玩家血量。每轮成功发射扣 1 耐久，然后冷却 5 秒，期间撞击不蓄力。
- 参数：`Authoring/Data/StoneBurst.device.json` 的 `Value = 8` 为蓄力次数，`Strength = 6` 为速度，`Range = 5` 为冷却秒数。数量和价值为 Lua 顶部的 `projectileCount = 16`、`projectileValue = 1`，均可编辑后重新打包。
- 模型/材质：`Authoring/Models/StoneBurstCylinder.asset`，直径 0.5、高 0.2 的灰色石柱，配 `StoneBurstSlate.mat` 与橙色 `StoneBurstEmber.mat`；URP/Lit 使用 `device-lit` 映射。
- Prefab/场景：`Authoring/Prefabs/StoneBurst.prefab`、`Authoring/Scenes/StoneBurstAuthoring.unity`，实体凸 MeshCollider 接受真实撞击。
- 运行时包：`Resources/Devices/9009_StoneBurst.mres`。普通、耐久 20、购买价 5、出售价 120、初始库存 0；第 20 轮也完整发射，`-1` 无限。

Lua 组合 `Visit`、`Vectors.Rotate`、`CreateSpawnBatch` 和 `TryUse`；根据球半径计算均匀出生位置，避免相邻球重叠。生成中途受阻或达到总数上限时整批撤销，保留满蓄力，每 0.1 模拟秒重试，不扣耐久、不提前冷却。成功生成的球独立运行并参与正常结算；来源 ID 由引擎记录，Lua 排除自身生成球及其复制/分裂后代为自身蓄力。所有基础 API 与生命周期规则见 [装置 Lua API](../Docs/Device_Lua_API.md)。

2026-10-11 验证：大石碎胸口专项 **536** 条断言通过，玩法回归 **2135** 条断言通过，全部 **12** 个运行时包加载通过。覆盖真实圆柱碰撞、八次蓄力、倾斜棋盘 16 个等距方向、不重叠出生点、原球保留、免费生成与独立计分、来源过滤、5 秒冷却边界、数量上限/中途物理失败回滚与自动重试、最终一次与无限耐久、跨回合清理及批对象有效期。

## 矿老板

选择 `Authoring/Profiles/GoldMine.asset` 进行 Analyze / Build MRes，输出 `Output/GoldMine.mres`。菜单 `Tools > MRes > Create or Locate Gold Mine Device` 定位制作配置；批处理入口 `MResPacker.MResGoldMineCreator.CreateAndBuild`，保留已有制作资源。

- Lua：`Authoring/Lua/GoldMine.lua`。每次真实撞击使弹珠基础价值 -2，最低 0，金币 +4；价值为 0 或不足 2 也触发。每座矿每回合最多产出 20 金币，即成功触发 5 次。额度耗尽后不再扣值或耐久，下一回合恢复。
- 参数：`Authoring/Data/GoldMine.device.json` 的 `Value = 2` 为扣除的基础价值，`Strength = 4` 为单次金币数，`Range = 20` 为每回合金币上限。金币参数取整，自定义上限无法整除时最后一次只发剩余额度。Metadata 的 `value` 是出售价。
- 模型/材质：`Authoring/Models/GoldMineCylinder.asset`，直径 0.5、高 0.2 的石质圆柱配金色矿脉和顶部金块；`GoldMineStone.mat`、`GoldMineGold.mat` 使用 URP/Lit 和 `device-lit` 映射。
- Prefab/场景：`Authoring/Prefabs/GoldMine.prefab`、`Authoring/Scenes/GoldMineAuthoring.unity`，实体凸 MeshCollider，弹珠保持正常碰撞。
- 运行时包：`Resources/Devices/9010_GoldMine.mres`。普通、耐久 20、购买价 5、出售价 120、初始库存 0。每次实际产出扣 1 耐久，最后一点也完整生效，`-1` 无限耐久仍受每回合产量限制。

不新增专用引擎接口，Lua 组合 `Visit`、`TryUse`、`AddBase`、`AddCoins`、`RoundId` 和 `SetStatus`。同一步多球各算一次并共享剩余额度，持续贴住不重复触发。负增量使用未修正的基础价值，再正常应用已有修正，与沙包的加值规则一致；金币直接加到账户，不转为回合分数。完整契约见 [装置 Lua API](../Docs/Device_Lua_API.md)。

2026-10-11 验证：矿老板专项 **110** 条断言通过，玩法回归 **2251** 条断言通过，全部 **13** 个运行时包加载通过。覆盖低值/零值球、五次产出与第六次免费碰撞、多球同时命中、实例/回合独立额度、最后一点与无限耐久、配置额度、价值修正与结算，以及真实圆柱碰撞和持续贴住去重。

## 淘金热

选择 `Authoring/Profiles/GoldRush.asset` 进行 Analyze / Build MRes，输出 `Output/GoldRush.mres`。菜单 `Tools > MRes > Create or Locate Gold Rush Device` 定位制作配置；批处理入口 `MResPacker.MResGoldRushCreator.CreateAndBuild`，保留已有制作资源。

- Lua：`Authoring/Lua/GoldRush.lua`。一次运输一颗弹珠，隐藏并等待 10 秒；期间拒绝新球。结束时等概率返还原球并加 2 金币、消耗球加 5 金币、或消耗球且无收益。返还沿棋盘下坡、保留原价值和进入速度大小；出口受阻时在原位置向下恢复。
- 参数：`Authoring/Data/GoldRush.device.json` 的 `Value = 10` 为运输时长，`Strength = 2` 为返还球结果的金币数，`Range = 5` 为仅金币结果的金币数。概率在 Lua 顶部三个权重中配置，默认 `1, 1, 1`；权重非负且总和大于 0。
- 模型/材质：`Authoring/Models/GoldRushCube.asset`、`GoldRushWheel.asset`；四份 `GoldRush*.mat` 分别用于岩石、木支架、金属轨道/车轮和矿洞暗部。模型包含木架矿洞、轨道与洞口停放的矿车；放置半径 0.6，URP/Lit 映射为 `device-lit`。
- Prefab/场景：`Authoring/Prefabs/GoldRush.prefab`、`Authoring/Scenes/GoldRushAuthoring.unity`；一个入口 BoxCollider 触发器，运输时其他球正常经过。
- 运行时包：`Resources/Devices/9011_GoldRush.mres`。普通、耐久 20、购买价 5、出售价 120、初始库存 0。三种结果完成时均扣 1 耐久，入库/取消不扣；最后一点正常完成，`-1` 无限。

新增通用 `Motion.TrySetHeldVisible(visible)`，仅设置已有滞留的显示，释放/取消/回收时由引擎恢复。Lua 组合它与 `TryHold`、`RandomRange`、`TryLaunch`、`TrySetVelocity`、`TryConsume`、`AddCoins` 和 `TryUseOnce` 实现效果；原有 API 保持兼容。容量、计时和结果均按库存实例独立，跨回合清理；寿命及排空规则照常推进。完整接口和行为见 [装置 Lua API](../Docs/Device_Lua_API.md)。

2026-10-11 验证：淘金热专项 **398** 条断言通过，玩法回归 **2257** 条断言通过，全部 **14** 个运行时包加载通过。覆盖等权随机结果、完整十秒等待、忙碌锁定、同一步多球与实例隔离、返回速度/价值和受阻回退、最后一点/无限耐久、显示 API 所有权与有效期，以及移除/寿命/回合/对象池清理和真实洞口触发。

## 猪猪存钱罐与装置倍率累加

选择 `Authoring/Profiles/PiggyBank.asset` 进行 Analyze / Build MRes，输出 `Output/PiggyBank.mres`。菜单 `Tools > MRes > Create or Locate Piggy Bank Device` 定位制作配置；批处理入口 `MResPacker.MResPiggyBankCreator.CreateAndBuild`，保留已有制作资源。

- Lua：`Authoring/Lua/PiggyBank.lua`。每次真实撞击按当前金币提供 `1 + 0.1 × floor(金币/10)` 的倍率，不花金币；不足 10 金币不生效、不扣耐久。
- 参数：`Authoring/Data/PiggyBank.device.json` 的 `Value = 10` 为每档金币数，`Strength = 0.1` 为每档倍率增量；`Range = 0.2` 保留通用合法值，本效果未使用。
- 模型/材质：`Authoring/Models/PiggyBankSphere.asset`、`PiggyBankCube.asset` 与三份 `PiggyBank*.mat`。粉色陶瓷小猪，包含猪鼻、鼻孔、眼睛、耳朵、四脚、尾巴及投币口。URP/Lit 映射为 `device-lit`。
- Prefab/场景：`Authoring/Prefabs/PiggyBank.prefab`、`Authoring/Scenes/PiggyBankAuthoring.unity`；实体 CapsuleCollider 接收撞击，普通物理负责反弹，放置半径 0.36。
- 运行时包：`Resources/Devices/9012_PiggyBank.mres`。普通、耐久 20、购买价 5、出售价 120、初始库存 0；有效撞击扣 1，最后一次仍生效，`-1` 无限。持续贴住、同球同一步重复事件和非实体撞击不计数。

所有装置只累加“倍率 - 1”：最终为 `1 + Σ(每次倍率 - 1)`，两次 1.5 得到 2；两次 1.2 得到 1.4，再加 Multi Ding 提供的 0.85 得到 2.25。基础 1 只算一次。撞击时只记录，得分区再计算。`AddScoreMultiplier(amount)` 仍接收完整倍率，由引擎减 1；旧 `MultiplyScore` 兼容名语义相同。装置通用倍率修正、旧倍率钉和复制球继承使用同一累计规则；`Authoring/Lua/MultiDing.lua` 与运行时包已同步。`Coins` 为只读钱包查询。原有全局、药物、得分区和黑洞显式结算继续按各自规则处理。完整契约见 [装置 Lua API](../Docs/Device_Lua_API.md)。

2026-10-11 首次验证：猪猪存钱罐专项 **114** 条断言、玩法回归 **2378** 条断言及全部 **15** 个包加载通过；该记录对应此前的累加规则，当前增量规则以以下最新验证为准。

2026-10-11 增量累加规则验证：猪猪存钱罐与倍率专项 **120** 条断言、玩法回归 **2384** 条断言、全部 **15** 个包加载通过。覆盖两次 ×1.5 最终 ×2 并到得分区结算、混合装置、旧 API 兼容、通用触发修正、复制继承、中性倍率和边界输入。引擎、Lua、两个受影响的 MRes 及装置目录已同步。
