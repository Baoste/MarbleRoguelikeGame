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

工具不会自动修改游戏场景、商店、资源目录或运行时 Shader Registry。`Output` 是制作端成品目录；当前已将示例安装为 `Resources/Devices/9000_BouncePad.mres` 并注册所需 Shader。运行目录保留本工具的 7 个制作成品和 `113_MultiDing.mres`，合计 8 种装置。

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

有限耐久按本回合有效作用扣除：碰撞/吞噬一颗扣 1，持续力场每颗弹珠每次进入扣 1，黑洞点击不另扣。最后一次作用仍生效，随后停用，已开始的滞留正常释放；新回合恢复。`-1` 永不扣减。游戏商店、库存和棋盘显示对应价格、稀有度与耐久。详细字段、`Trigger` / `TryUse` 的组合规则及兼容说明见 [装置 Lua API](../Docs/Device_Lua_API.md)，该文档补充原始工作流要求中的四字段 Schema。

2026-10-11 元数据与耐久验证：7 份制作配置重新构建；20 个游戏包验证通过；Loader / Packer EditMode **53/53** 通过。玩法回归 **1437** 条断言（含元数据、买卖和耐久 **178** 条），另有黏黏 **183**、弹弹 **499**、吸吸 **244**、力场 **339**、传送门 **160**、黑洞 **230** 条专项断言通过。覆盖默认 20 次、无限耐久 -1、回合恢复、最后一次滞留完成、力场停留不重复扣除和黑洞失效后无残留力。

## 黏黏

`Authoring/Profiles/Sticky.asset` 是黏黏的制作配置。打开 `Tools > MRes > Packer`，选择该配置后 Analyze / Build MRes，输出 `Output/Sticky.mres`。菜单 `Tools > MRes > Create or Locate Sticky Device` 定位配置；批处理入口为 `MResPacker.MResStickyCreator.CreateAndBuild`，已有源资产不会被生成器覆盖。

- 模型：`Authoring/Models/StickyCylinder.asset`，直径 0.4、高 0.1 的薄圆柱；Prefab 使用凸 MeshCollider 触发器，不会先把弹珠反弹走。
- Prefab / 场景：`Authoring/Prefabs/Sticky.prefab`、`Authoring/Scenes/StickyAuthoring.unity`。
- Lua：`Authoring/Lua/Sticky.lua`。`OnContact` 调用独立的 `Motion:TryHold`；`OnHoldEnd` 读取 `Hold.EntrySpeed` 再调用 `TryLaunch` 沿棋盘下坡方向释放。出射受阻时由 Lua 调用 `TrySetVelocity` 原地向下释放。
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

聚集位置会按弹珠半径留出间隔；已聚集的弹珠原地向下释放，避免统一出口造成重叠。目标受阻或来不及抵达的弹珠到时也会结束吸引。完整事件契约和边界规则见 `Assets/Docs/Device_Lua_API.md` 的 `OnBallUpdate` 与“吸吸”章节。

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

引擎提供独立通用接口：`OnTick` / `OnActivate`、`SetStatus`、`TrySettle(score)`。黑洞 Lua 组合这些接口与已有 `Consume(false)`、`AddRadialForce`，负责全部充能状态、时间和公式。Controller 自动添加点击与状态显示；计时使用模拟时钟，没有球也会结束，暂停不倒计时，重复点击不刷新。每个实例独立，新回合重置充能。直接结算不再乘机器/全局/药物/Rush 倍率，正常计分事件、小数累计、分数上限与回收仍由引擎处理。

接口契约见 `Assets/Docs/Device_Lua_API.md` 的“无当前弹珠的装置事件”和“黑洞”章节。真实物理专项验证位于 `Assets/Editor/Validation/BlackHoleDeviceValidation.cs`，由 `Marbles ECS/Validate Attribute and Content Runtime` 一并执行。

2026-10-11 验证：黑洞专项 **195** 条断言、玩法回归 **1259** 条断言全部通过；黏黏 **177**、弹弹 **499**、吸吸 **244**、加速力场 **146**、传送门 **160** 条继续通过，资源目录覆盖全部 **20** 个包。成品与游戏副本 SHA-256 一致。
