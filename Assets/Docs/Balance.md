# 配置来源与调参

`Resources/GameContent.json` 配置属性、药物和内容规则；其中 `Devices` 始终由当前 `Resources/Devices/**/*.mres` 完整生成。运行时装置直接使用 MRes 目录。`Resources/GameBalance.json` 保留血量、抽血档位、发射间隔、库存容量、阶段目标和战役规则；盘面由场景组件配置。

## 属性的唯一入口

在 `GameContent.json` 的 `Attributes` 中按 `Id` 查找，修改 `DefaultValue`。`MinValue/MaxValue` 是药物、装置等效果结算后的属性上下限。

机器倍率、全局倍率、赌池倍率的合并上限读取对应属性 `MaxValue`。RUSH 改为全盘固定 100 点累积条、基础持续 10 秒；旧持续时间及概率属性不再控制核心机制，详见 `Device_Lua_API.md`。

| 调整内容 | GameContent 属性 | 已移除的 JSON / Excel 重复字段 |
|---|---|---|
| 弹珠半径 | `BALL_RADIUS` | `Marble.MinRadius / MaxRadius` |
| 弹珠质量 | `BALL_MASS` | `Marble.MassPerBlood` |
| 基础价值 | `BALL_BASE_VALUE` | `Marble.BaseScore` |
| 单位投入耗血 | `BLOOD_COST` | `Marble.BloodCostMultiplier` |
| 发射速度 | `BALL_LAUNCH_SPEED` | `Marble.LaunchSpeed`、控制器 `BallLaunchSpeed` |
| 弹性 | `BALL_BOUNCE` | `Marble.BaseRestitution` |
| 球的生命周期 | `BALL_LIFETIME` | `Marble.MaxLifeSeconds` |
| 机器倍率 | `MACHINE_MULT` | `Marble.LauncherScoreMultiplier` |
| RUSH 条值及持续时间 | `RushSystem`：上限 100，10 秒 + 触发球额外时长，条值不衰减 | 旧概率、时长和衰减字段仅兼容 |
| 赌博概率及倍率 | `GAMBLE_LOSS_CHANCE / RETURN_CHANCE / WIN_CHANCE / QUADRUPLE_CHANCE` | `Campaign.GamblingWinChance / GamblingWinMultiplier` |
| 剩余血量兑换率 | `BLOOD_DIVIDEND_RATE` | `Campaign.CashoutCoinsPerBlood` |

半径按现有单位转换：**实际世界半径 = 最终 BALL_RADIUS × 0.16**。最低档默认属性值 `0.5` 对应半径 `0.08`、直径 `0.16`。高档位以 `Investment / MinInvestment` 为投入倍率：半径按立方根增长，质量线性增长，基础价值按 `FlowValueExponent` 次方增长；当前指数为 `1.15`。凝血剂等药物仍可通过体积倍率影响半径，按体积倍率的立方根换算。

发射速度直接采用 `BALL_LAUNCH_SPEED`，单位为 m/s，不再乘旧发射速度；最终仍受 `BALL_MAX_SPEED` 限制。弹性在属性范围内结算；与静态或运动学障碍物碰撞时，按该球属性校正反弹，障碍物的高弹性材质不会把 `BALL_BOUNCE = 0` 抬高。超过 `1` 的属性值提供额外反弹增益。

抽血档位仍在 `MinInvestment/MaxInvestment` 间选择投入量。基础耗血为「投入量 × BLOOD_COST」，随后应用属性 ADD/MUL/SET 和上下限；批量发射按球数收费。低档位用于增加携带 RUSH 贡献的球数，高档位用于 Rush 窗口内快速兑现价值。提现为「剩余血量 × BLOOD_DIVIDEND_RATE × 当前阶段 CashoutRate × COIN_GAIN_RATE」，金币向下取整。

场景 `MarbleZone` 的倍率和 `RushEnabled` 开关仍生效；`BaseRushChance` 仅兼容，新 RUSH 不做概率抽取。流失区按贡献原值入账，普通/赌博得分区按 1.5 倍入账。

装置条目的 `Cooldown` 是每种装置的基础秒数，初始化到该装置的 `DEVICE_COOLDOWN`；属性效果在此基础上修改。`DEVICE_TRIGGER_RATE` 和 `MACHINE_MULT` 在球与装置上各有作用域，装置初始乘数为中性值 `1`，避免把属性默认值重复计算。装置 `Radius` 表示装置占地半径，与弹珠 `BALL_RADIUS` 含义不同。

## 装置和药物

装置的名称、价格、参数和 `StartingCount` 在 `Resources/Devices/*.mres` 中配置；药物在 `GameContent.json` 的 `Drugs` 中修改。`Devices` 是 MRes 目录自动生成的离线分析快照。`StartingCount = 0` 不赠送，正整数表示开局数量；传送门每份生成一对，容量按两个实体计算。

打开项目/脚本重载、导入/删除/移动 MRes、导入 `GameContent.json`、进入 Play 或构建 Player 时都会同步。同步完整替换 `Devices`，移除当前包目录中已不存在的条目；名称、描述、买卖价格、稀有度和耐久取包内 Metadata，其余参数取包内 `game/device.json`。旧 JSON、Excel 不再补回装置或覆盖包内配置，属性、药物和内容规则保留。Play 中不修改当前会话，退出后同步，重新进入 Play 使用新配置。

手动同步菜单为 `Tools/MRes/Rebuild Device Catalog`。Unity 未打开时可在项目根目录运行 `python Assets/Editor/Balance/sync_mres_devices.py`；加 `--check` 只检查是否与当前 MRes 一致。脚本仅解析快照所需的 Metadata/业务参数，完整资源、Shader、Lua 校验由 Unity 完成；包损坏或 ID 重复会报错，不使用旧列表兜底。离线数值工具继续读取这份最新快照，历史分析结果和显式选择的旧实验快照不会被重写。

给程序/策划交接的工作簿是 `Assets/Docs/血色柏青哥_道具药物装置_规范修订版.xlsx`。优先查看其中的“运行时平衡表”：它汇总全部 27 个装置/药物的价格、当前效果、时长、叠加规则、证据等级、旧值/新值和调整原因。该页用于审阅与配置交接，运行时装置来源是 `Resources/Devices/*.mres`，属性和药物来源是 `Resources/GameContent.json`。

Rush 药物的专用数值也已数据化：`RushChanceAdd` 保留肾上腺素旧概率数据但不触发新 RUSH，`RushScoreMultiplier` 控制烧烧果实在 Rush 结算时的额外倍率。商店每回合从完整目录等概率抽取 3 个不同装置、1 个药物，取消 Rush 保底；付费刷新采用同样规则，购买不会重抽其他货位。每次新开局生成独立商店种子，不改变计分随机流。`Campaign.ShopOfferCount` 为总数（4），`ShopDrugOfferCount` 为药物数（1，旧 Excel 可省略），其余货位为装置；目录不足不跨品类补位。

`GameBalance` 的装置和药物列表直接引用同一份内容目录；会话复制时深拷贝整个目录。基础 JSON 和基础 Excel 不再保存旧装置、药物列表。

当前共 34 个属性、35 种装置、12 种药物；装置包含 BouncePad、黏黏、弹弹、吸吸、加速力场、传送门、黑洞、反重力、大炮、沙包、大石碎胸口、矿老板、淘金热、猪猪存钱罐、赚赚、摇钱树、分流器、大喷菇、炸弹、复制体、造血工厂、弹珠机关枪、草皮、故障装置、more is more、less is more、雪糕机、大胃袋、星星灯、种植牙、有锅气、降降火、台球、超大杯全冰美式和 Multi Ding。装置数量随实际 MRes 文件变化，全部由 `Resources/Devices/*.mres` 加载，详见 [MRes 装置目录](../Resources/Devices/README.md)。

装置每次只累加“倍率 - 1”，到得分区以 `1 + Σ(倍率 - 1)` 统一计算；两次 1.5 得到 2，两次 1.2 得到 1.4，基础 1 只算一次。Lua 使用 `AddScoreMultiplier` 传入完整倍率，通用装置修正和旧倍率钉共用规则；全局、药物和得分区倍率仍按各自结算层处理。

## JSON 与 Excel

日常直接修改 JSON，保存后退出 Play 并重新进入。运行会话使用深拷贝，重开当前局不重新读取磁盘配置。

- `Config/GameBalance.xlsx` 只保留 `Marble`、`Campaign`、`Stages` 和说明页；保存会自动导入基础 JSON，也可使用 `Marbles ECS/Import Game Balance Excel`。
- 规范工作簿不再自动覆盖 `GameContent.json`。只有显式执行 `Marbles ECS/Import Attribute and Content Excel`，才会采用表中的属性和药物窗口等数值；需要使用该菜单时，先把希望保留的 JSON 修改同步到表格。
- “运行时平衡表”是汇总审阅页，不被 Importer 自动读取；显式导入读取的是属性字典与“设计总表”，药物配方保留已注册内容，装置每次直接解析当前 MRes 文件。
- 旧属性表将发射速度标为倍率 `x`；导入器统一标记为 `m/s`，数值本身不做额外缩放。
- `Editor/Balance/build_content.py` 只补充缺失的属性和药物，保留其现有数值及内容规则；`Devices` 每次直接从当前 MRes 完整生成，不保留旧 JSON 中的装置行。数据完全一致时不重写文件。

基础 Excel 不识别已删除的属性字段、`Devices/Drugs/Board/Zones` 工作表，导入失败时不会发布 JSON。JSON 中遗留的旧属性字段被忽略，不能重新控制内容属性。`GameContent.json` 是场景启动的必需资源，缺失时报错，不再静默切回旧数值。

代码保留了不读取内容目录的旧纯 C# 模拟 API，供独立原型和旧验证程序使用；其兼容字段标记为 `[NonSerialized]`，不会出现在 JSON、Excel 或 Inspector 中。战役与非战役游戏场景都使用内容属性路径。

## 场景 Inspector

| 组件 | 字段 | 用途 |
|---|---|---|
| `MarbleGameController` | `GridSize / PlacementClearance` | 吸附网格、放置间距 |
| `MarbleGameController` | `PlacementObstacleRoots` | 障碍物根对象；检测其实际启用的非 Trigger Collider |
| `DevicePlacementUI` | `BoardRoot / PlacementPlane / PlacementPlaneSize / DevicePlaneDistance` | 放置坐标、平面、范围与高度 |
| `MarbleGameController` | `LaunchPoint / LauncherMoveHalfWidth / LauncherMoveSpeed` | 发射点及移动范围、移动速度 |
| `MarbleGameController` | `LaunchOffset / ShowLaunchGizmos` | 发射点局部 XYZ 偏移；绿色线框球标记实际发射中心，黄色连线表示偏移，青色箭头表示发射方向 |
| `MarbleGameController` | `FixedStep / MaxStepsPerFrame` | 模拟步长、每帧最大模拟次数 |
| `MarbleZone` | `Kind / Multiplier / BaseRushChance / RushEnabled` | 区域行为；范围由 Trigger Collider 决定 |
| `MarbleBoardView` | `PinHeight` | 旧程序 HUD 的钉子高度；Prefab 自己的外观由 Transform 决定 |

`TestScene` 的放置障碍物已绑定 `PhysicsRoot/MarblePins`。不要把底板或动态装置的父对象加入障碍物列表。移动、缩放、旋转、禁用或删除固定钉后，放置检查跟随真实 Collider；不再按行列公式预留位置。增幅塔之间保留 `0.01` 间距规则，网格吸附后重新检查放置范围。

程序不生成盘面、墙体、固定钉和终点；这些均在场景中手工制作。发射滑条使用发射点启动时的局部 Z 坐标为中心，与实际移动轴一致。

发射中心为 `LaunchPoint.TransformPoint(LaunchOffset)`，偏移随发射点的移动、旋转和缩放变化，默认 `(0, 0, 0)`。编辑和运行时均可通过 Scene 视图的 Gizmos 预览；标记大小只用于定位，不代表弹珠半径。多球发射以此中心排列。

## 验证

Unity 菜单 `Marbles ECS/Validate Attribute and Content Runtime` 验证配置优先级、旧 JSON 字段忽略、Excel 输出、属性、装置、药物、库存、赌博、提现、放置和物理表现。测试会进入 Play；如不希望占用当前场景，可在独立临时项目中运行。
