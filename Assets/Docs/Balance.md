# 配置来源与调参

`Resources/GameContent.json` 是属性、装置和药物的唯一运行时配置来源。`Resources/GameBalance.json` 保留血量、抽血档位、发射间隔、库存容量、阶段目标和战役规则；盘面由场景组件配置。

## 属性的唯一入口

在 `GameContent.json` 的 `Attributes` 中按 `Id` 查找，修改 `DefaultValue`。`MinValue/MaxValue` 是药物、装置等效果结算后的属性上下限。

机器倍率、全局倍率、赌池倍率的合并上限，以及 Rush 持续时间和进度条上限，同样读取对应属性的 `MaxValue`，不再另外写死上限。

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
| Rush 持续时间及衰减 | `RUSH_DURATION / RUSH_DECAY` | `RushDurationSeconds / MaxRushSeconds / RushTimeScale / RushExtendsDuration` |
| 赌博概率及倍率 | `GAMBLE_LOSS_CHANCE / RETURN_CHANCE / WIN_CHANCE / QUADRUPLE_CHANCE` | `Campaign.GamblingWinChance / GamblingWinMultiplier` |
| 剩余血量兑换率 | `BLOOD_DIVIDEND_RATE` | `Campaign.CashoutCoinsPerBlood` |

半径按现有单位转换：**实际世界半径 = 最终 BALL_RADIUS × 0.16**。默认属性值 `0.5` 对应半径 `0.08`、直径 `0.16`；质量直接采用属性的 kg 数值。抽血档位不再隐式改变半径、质量或基础分。凝血剂等药物仍可通过体积倍率影响半径，按体积倍率的立方根换算。

发射速度直接采用 `BALL_LAUNCH_SPEED`，单位为 m/s，不再乘旧发射速度；最终仍受 `BALL_MAX_SPEED` 限制。弹性在属性范围内结算；与静态或运动学障碍物碰撞时，按该球属性校正反弹，障碍物的高弹性材质不会把 `BALL_BOUNCE = 0` 抬高。超过 `1` 的属性值提供额外反弹增益。

抽血档位仍在 `MinInvestment/MaxInvestment` 间选择投入量。基础耗血为「投入量 × BLOOD_COST」，随后应用属性 ADD/MUL/SET 和上下限；批量发射按球数收费。提现为「剩余血量 × BLOOD_DIVIDEND_RATE × 当前阶段 CashoutRate × COIN_GAIN_RATE」，金币向下取整。

场景 `MarbleZone` 的倍率、Rush 概率和触发开关，以及具体装置效果，属于不同区域或装置的规则；它们不是另一份弹珠基础属性。旧的 `Marble.BaseRushChance` 已从配置移除，区域概率在 `MarbleZone.BaseRushChance` 设置。

装置条目的 `Cooldown` 是每种装置的基础秒数，初始化到该装置的 `DEVICE_COOLDOWN`；属性效果在此基础上修改。`DEVICE_TRIGGER_RATE` 和 `MACHINE_MULT` 在球与装置上各有作用域，装置初始乘数为中性值 `1`，避免把属性默认值重复计算。装置 `Radius` 表示装置占地半径，与弹珠 `BALL_RADIUS` 含义不同。

## 装置和药物

在 `GameContent.json` 的 `Devices`、`Drugs` 列表中修改名称、价格、参数、`StartingCount` 和药物效果。`StartingCount = 0` 不赠送，正整数表示开局数量；传送门每份生成一对，容量按两个实体计算。

`GameBalance` 的装置和药物列表直接引用同一份内容目录；会话复制时深拷贝整个目录。基础 JSON 和基础 Excel 不再保存旧装置、药物列表。

当前共 34 个属性、15 种装置、12 种药物。全部装置 Prefab 在 `Prefabs/Devices`，`TestScene` 已按定义 ID 绑定。

## JSON 与 Excel

日常直接修改 JSON，保存后退出 Play 并重新进入。运行会话使用深拷贝，重开当前局不重新读取磁盘配置。

- `Config/GameBalance.xlsx` 只保留 `Marble`、`Campaign`、`Stages` 和说明页；保存会自动导入基础 JSON，也可使用 `Marbles ECS/Import Game Balance Excel`。
- 规范工作簿不再自动覆盖 `GameContent.json`。只有显式执行 `Marbles ECS/Import Attribute and Content Excel`，才会采用表中的属性和药物窗口等数值；需要使用该菜单时，先把希望保留的 JSON 修改同步到表格。
- 旧属性表将发射速度标为倍率 `x`；导入器统一标记为 `m/s`，数值本身不做额外缩放。
- `Editor/Balance/build_content.py` 只补充缺失的已注册条目，保留已有属性、价格、初始数量、配方及内容规则。目录完整时不会重写文件。

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
