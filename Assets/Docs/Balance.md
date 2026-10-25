# 配置来源与调参

`Resources/GameContent.json` 配置属性和内容规则；其中 `Devices` 始终由当前 `Resources/Devices/**/*.mres` 完整生成。运行时装置直接使用 MRes 目录。`Resources/GameBalance.json` 保留血量、抽血档位、发射间隔、库存容量、阶段目标和战役规则；盘面由场景组件配置。

`TestScene` 的 `PhysicsRoot/新盘面_局部X上坡_Z横向/玻璃罩碰撞_不遮挡操作` 限制弹珠飞离盘面。罩面平行盘面，内侧距离盘面 1.5 个世界单位、厚度 0.1，三个子对象补齐左右侧和上沿的碰撞边界，底部得分出口保持开放。罩体仅含静态 BoxCollider，不渲染；使用 Ignore Raycast 层，保留弹珠碰撞和装置操作。可在该对象的 BoxCollider 调整尺寸；若调整罩面高度，同时调整侧面子碰撞体，避免留下空隙。

布置阶段可以调整已有装置：左键点击盘面上的装置后跟随鼠标，再次左键确认新位置；`R` 旋转预览，右键或 `Esc` 取消并恢复原位置、朝向。位置必须通过盘面边界、障碍物和装置间距检查；确认前不修改装置数据，移动保留耐久、配对和 Lua 状态。即使没有待放置的新装置，只要仍拥有装置，商店结束后也会进入布置阶段。

界面底部红色售出区仅在布置阶段显示。选中装置后显示其 Metadata `value` 售价；将鼠标移到红色区域，再次左键才确认出售并获得金币。悬停或背包拖拽松手不直接出售，右键可取消；传送门按单件出售，配对的另一件保留。场景入口为 `DeviceEditCanvas/DeviceSellArea`，可用菜单 `Marbles ECS/UI/配置装置移动与售出区` 给其他场景配置相同界面。

## 属性的唯一入口

在 `GameContent.json` 的 `Attributes` 中按 `Id` 查找，修改 `DefaultValue`。`MinValue/MaxValue` 是装置等效果结算后的属性上下限。

机器倍率、全局倍率的合并上限读取对应属性 `MaxValue`。RUSH 改为全盘固定 100 点累积条、基础持续 2 秒；旧持续时间及概率属性不再控制核心机制，详见 `Device_Lua_API.md`。

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
| RUSH 条值及持续时间 | `RushSystem`：上限 100，2 秒 + 触发球额外时长，条值不衰减 | 旧概率、时长和衰减字段仅兼容 |
| 赌博转盘 | `GamblingWheelRules`：八档等概率，2 秒起步、最短 0.2 秒 | 旧输/返还/双倍/四倍规则、概率和赌池修正均停用 |
| 剩余血量兑换率 | `BLOOD_DIVIDEND_RATE` | `Campaign.CashoutCoinsPerBlood` |

半径按现有单位转换：**实际世界半径 = 最终 BALL_RADIUS × 0.16**。最低档默认属性值 `0.5` 对应半径 `0.08`、直径 `0.16`。高档位以 `Investment / MinInvestment` 为投入倍率：半径按立方根增长，质量线性增长，基础价值按 `FlowValueExponent` 次方增长；当前指数为 `1.15`。通用体积缩放按体积倍率的立方根换算半径。旧药物不再影响发射参数。

发射速度直接采用 `BALL_LAUNCH_SPEED`，单位为 m/s，不再乘旧发射速度；最终仍受 `BALL_MAX_SPEED` 限制。弹性在属性范围内结算；与静态或运动学障碍物碰撞时，按该球属性校正反弹，障碍物的高弹性材质不会把 `BALL_BOUNCE = 0` 抬高。超过 `1` 的属性值提供额外反弹增益。

抽血档位仍在 `MinInvestment/MaxInvestment` 间选择投入量。基础耗血为「投入量 × BLOOD_COST」，随后应用属性 ADD/MUL/SET 和上下限；批量发射按球数收费。低档位用于增加携带 RUSH 贡献的球数，高档位用于 Rush 窗口内快速兑现价值。提现为「剩余血量 × BLOOD_DIVIDEND_RATE × 当前阶段 CashoutRate × COIN_GAIN_RATE」，金币向下取整。

发射频率跟随空格操作：点按一次最多发射一次，按得越快出球越快；超过上限的请求直接丢弃。按下时请求第一发，继续长按则让自动连发频率从 0 线性增加，`Marble.HoldAccelerationSeconds` 默认 **2 秒**达到极速。松开立即取消未消费请求和连发进度，再按重新加速；暂停、回合结束和重开也会清空输入。每个模拟步最多消费一次发射请求，冷却、满球、血量不足或生成失败均不排队补发。

战役的两种间隔在 `Resources/GameBalance.json` 的 `Marble` 中独立配置，`Config/GameBalance.xlsx` 同步维护：

- `FireIntervalSeconds`：长按加速完成后的连发间隔，默认 **0.15 秒/次，约 6.67 次/秒**。
- `MaxFireIntervalSeconds`：点按限速间隔，默认 **0.15 秒/次**。两次成功发射之间至少间隔此时长，太快的点按直接丢弃；数值越小，允许点按越快。按下长按键时的第一发也按点按规则检查。
- `HoldAccelerationSeconds`：长按从 0 加速到上述连发速度的时间，默认 **2 秒**。

两种间隔不要求大小关系；例如长按间隔 0.3、点按间隔 0.1，表示长按最快约 3.33 次/秒、点按最快 10 次/秒。切换输入方式仍从上一次成功发射计时，不另送一发；松开清除输入但不重置发射时间。RUSH 分别给两种基础频率额外 **+5 次/秒**，进入或结束时立即按当前状态检查冷却。单次发射可被 `BALL_SPAWN_COUNT` 改成多球，频率上限限制发射次数。场景 Inspector 的 `BallsPerSecond` 仅控制独立模式长按频率，快照 `ShotsPerSecond` 也表示长按极速。这里的频率不改变弹珠飞行速度，飞行速度仍由 `BALL_LAUNCH_SPEED` 决定。

场景 `MarbleZone` 的倍率和 `RushEnabled` 开关仍生效；旧 `BaseRushChance` 字段及其传递、校验已移除，新 RUSH 不做概率抽取。流失区按贡献原值入账，普通/赌博得分区在 `RushEnabled` 开启时按 1.5 倍入账。

得分区新增全盘共享的**得分叠加**。`MarbleZone.ScoreStackEnabled` 默认开启；全局加成初始为 **0**，区域 `Multiplier` 默认 **1**，因此初始有效倍率为 **×1**，没有固定的 ×2 起点。每颗进入参与区域的球先使全局加成增加 **0.05**，再用「区域原倍率＋当前全局加成」结算：原倍率为 1 时，首球按 ×1.05、第二球按 ×1.10、第三球按 ×1.15……计算，超时恢复 ×1。不同区域共享加成，各自保留配置的原倍率；要让某区域从 ×1 开始，将其 `Multiplier` 设为 1。关闭开关的区域不增加、不刷新，也不享受这份加成；流失区始终不参与。普通得分区和赌博得分区都可参与，同球重复碰撞只结算一次，同一步的多球按生成顺序逐颗累加。

首次进球开始 **1 秒**倒计时；之后每次参与进球的刷新时长为 `max(0.1, 1 / (1 + 进球前全局加成))` 秒，前三次约为 1、0.952、0.909 秒。超时仅清空加成，保留已得分；暂停时计时暂停，回合结束、下一回合和重新开局均清空。三个可调参数位于 `Marble.ScoreStackIncrement / ScoreStackInitialSeconds / ScoreStackMinimumSeconds`，JSON 和基础 Excel 同步维护。

通常结算式为「球价值 ×（得分区原倍率＋全局加成）× 装置累计倍率」。机器、全局属性倍率和得分上限仍按原顺序处理；赌博区把该分值存入转盘奖池；`FlatScore` 仍在区域/装置乘法之后相加。有效区域倍率沿用原数值范围上限 1000，内容属性的机器倍率上限仍生效。RUSH 不放大这份加成。黑洞等 Lua 直接指定金额的结算不触发、不刷新得分区叠加，也不会被自动加乘；装置和弹珠本身的倍率数据不因全局叠加而改变。

界面右上角、道具库按钮下方显示两条状态进度条：上方 RUSH 在未触发时显示 0～100 的充能进度，触发后按「剩余时间 / 本次总时长」缩短，普通为 2 秒、降降火延长后为 7 秒；下方显示全局得分叠加的实际加成（例如 `+0.10`）和剩余时间，每次有效进球刷新后回满，再按本次缩短后的窗口倒计时。暂停时两条均冻结；叠加过期后下条清空，RUSH 结束后上条回到充能模式，非游玩阶段隐藏面板。显示读取模拟状态，不用 UI 自己的计时器。

场景节点为 `StatusHudCanvas/StatusBars`，使用屏幕空间 Canvas、右上角锚点和 1920×1080 比例缩放；面板不拦截点击。文字与条的引用在 `ScoreListener`，菜单 `Marbles ECS/UI/配置右上角状态进度条` 可对当前场景重新配置，完成后手动保存场景。

物理采样报告升级为 `SchemaVersion = 2`，JSON 与 CSV 不再输出基础 RUSH 概率。项目根目录 `Tools/Balance` 的历史概率模型尚未迁移至贡献累加机制，不能用新采样报告推导当前 RUSH 平衡；遇到新格式会明确报错，旧报告仍可用于历史分析。

装置条目的 `Cooldown` 是每种装置的基础秒数，初始化到该装置的 `DEVICE_COOLDOWN`；属性效果在此基础上修改。`DEVICE_TRIGGER_RATE` 和 `MACHINE_MULT` 在球与装置上各有作用域，装置初始乘数为中性值 `1`，避免把属性默认值重复计算。装置 `Radius` 表示装置占地半径，与弹珠 `BALL_RADIUS` 含义不同。

## 装置与后续药物计划

2026-10-22：旧药物系统已移除，后续将用 Lua 重新实现。当前没有药物商品、初始库存、服药入口或药效计时；本次未接入新的 Lua 药物系统。

装置的名称、价格、参数和 `StartingCount` 在 `Resources/Devices/*.mres` 中配置；旧 `Drugs` 已删除。`Devices` 是 MRes 目录自动生成的离线分析快照。`StartingCount = 0` 不赠送，正整数表示开局数量；传送门每份生成一对，容量按两个实体计算。

打开项目/脚本重载、导入/删除/移动 MRes、导入 `GameContent.json`、进入 Play 或构建 Player 时都会同步。同步完整替换 `Devices`，移除当前包目录中已不存在的条目；名称、描述、买卖价格、稀有度和耐久取包内 Metadata，其余参数取包内 `game/device.json`。旧 JSON、Excel 不再补回装置或覆盖包内配置，属性和内容规则保留。Play 中不修改当前会话，退出后同步，重新进入 Play 使用新配置。

手动同步菜单为 `Tools/MRes/Rebuild Device Catalog`。Unity 未打开时可在项目根目录运行 `python Assets/Editor/Balance/sync_mres_devices.py`；加 `--check` 只检查是否与当前 MRes 一致。脚本仅解析快照所需的 Metadata/业务参数，完整资源、Shader、Lua 校验由 Unity 完成；包损坏或 ID 重复会报错，不使用旧列表兜底。离线数值工具继续读取这份最新快照，历史分析结果和显式选择的旧实验快照不会被重写。

给程序/策划交接的工作簿是 `Assets/Docs/血色柏青哥_道具药物装置_规范修订版.xlsx`。优先查看其中的“运行时平衡表”：其中的旧药物行仅作为历史设计参考，不代表当前游戏内容。该页不自动覆盖配置，运行时装置来源是 `Resources/Devices/*.mres`，属性来源是 `Resources/GameContent.json`。

商店当前只出售 MRes 装置，每回合从当前完整目录等概率、无重复抽取 `Campaign.ShopOfferCount` 个装置（默认 4），不设 RUSH 保底；目录不足时只显示实际可用装置。付费刷新采用同样规则，购买不会重抽其他货位。商店与初始装置、装置 Lua、赌博开奖共用整局唯一随机序列；生成和刷新商品会推进后续玩法的随机状态。目录按 ID 排序，相同种子和调用次序可复现抽取结果。旧药物商品、初始库存、服药和药效代码保持删除，后续药物将改用 Lua。

当前默认开局空背包，不赠送装置：`TestScene` 关闭 `EnableRandomStartingDevices`，全部 MRes 的 `StartingCount` 为 `0`。进入游戏和重新开局都直接开始第一回合，之后通过商店购买装置。

控制器的 `EnableRandomStartingDevices` 保留为测试选项，手动开启后可启用每局随机初始装置，替代固定 `StartingCount`；抽取数量由 `RandomStartingDeviceCount` 控制。进入下一回合不重新抽取，成套装置完整赠送。初始抽取直接消耗统一随机序列，不再提供初始装置或商店的独立种子。

在场景的 `MarbleGameController` Inspector「随机数」分组中，`每次开新局刷新随机种子`（`RefreshRandomSeedOnNewRun`）默认勾选：每次进入游戏或重新开局生成一个新的非零整局种子；取消勾选时使用 `Resources/GameBalance.json` 的 `Marble.RandomSeed`（当前为 `12345`）。进入下一回合、打开商店或道具库均不重新播种；只在需要随机结果时依次取数。`LastRunRandomSeed` 记录本次实际种子，方便复现。固定种子只保证相同随机调用次序的结果，不保证物理在不同平台完全一致。战役和独立模式均遵守此开关。

`GameBalance` 的装置列表直接引用同一份内容目录；会话复制时深拷贝整个目录。基础 JSON 和基础 Excel 不再保存旧装置、药物列表。

当前共 34 个属性、35 种装置、0 种药物；装置包含 BouncePad、黏黏、弹弹、吸吸、加速力场、传送门、黑洞、反重力、大炮、沙包、大石碎胸口、矿老板、淘金热、猪猪存钱罐、赚赚、摇钱树、分流器、大喷菇、炸弹、复制体、造血工厂、弹珠机关枪、草皮、故障装置、more is more、less is more、雪糕机、大胃袋、星星灯、种植牙、有锅气、降降火、台球、超大杯全冰美式和 Multi Ding。装置数量随实际 MRes 文件变化，全部由 `Resources/Devices/*.mres` 加载，详见 [MRes 装置目录](../Resources/Devices/README.md)。

装置每次只累加“倍率 - 1”，到得分区以 `1 + Σ(倍率 - 1)` 统一计算；两次 1.5 得到 2，两次 1.2 得到 1.4，基础 1 只算一次。Lua 使用 `AddScoreMultiplier` 传入完整倍率，通用装置修正和旧倍率钉共用规则；全局和得分区倍率仍按各自结算层处理。

## 赌博转盘

所有 `MarbleZone.Kind = RandomScore` 共用一个奖池。进球按普通得分公式计算，包含当时的基础价值（含临时 RUSH 价值）、装置累计倍率、区域倍率和开启的全局得分叠加，连同小数一起入池，尚未开奖不计入已确认分。RUSH 贡献仍在进球时按普通得分区规则处理，不等待开奖。

每次进球把本轮球数 `n` 加 1，并刷新等待时间为 `max(0.2, 2 / sqrt(n))` 秒。第 1 球为 2 秒，第 4 球为 1 秒，第 16 球为 0.5 秒；到期或回合结束时统一开奖。本轮归零后，下轮重新从 2 秒开始，一回合可多次开奖。恰在截止时到达的球先结算旧池，再进入新池。零价值球也计数，空池不开奖。暂停冻结倒计时；重开清空奖池、计数和结果。

八档为 **−2、−1、0、1、5、10、20、50**，每档 **12.5%**；开奖只消耗整局共享随机序列的一次抽取。`奖池总值 × 开奖倍率` 加到本回合分；负结果扣本回合分，最低 0，不扣金币、血量或已完成回合累计分。小数先参与乘法，再沿用得分整数与余数规则；正向结果仍受回合得分上限约束。最后一轮开奖在通关判断前完成，UI 不延迟结算。

`TestScene` 左上角的 `GamblingWheelCanvas` 显示持续旋转的八扇区模型、奖池/球数、倒计时条和最近实际增减分。最近中奖扇区高亮，动画不调用随机数。模型源资产为 `Prefabs/UI/GamblingWheel.prefab`、`Art/UI/GamblingWheel`；配置入口为 `Marbles ECS/UI/配置左上角赌博转盘`。模型由独立相机绘制，展示不拦截鼠标；商店/布置阶段隐藏。

旧 `RandomScoreResultUI`、回合末闪字等待流程、`RandomScoreRevealDelay` 和独立待定分面板已删除。旧 `GAMBLE_*`、`BALL_GAMBLE_VALUE_MOD` 属性 ID 仅为已有内容文件的兼容结构保留，**不再影响奖池金额、概率或倍率**；无需重新打包装置。

## JSON 与 Excel

日常直接修改 JSON，保存后退出 Play 并重新进入。运行会话使用深拷贝，重开当前局不重新读取磁盘配置。

- `Config/GameBalance.xlsx` 只保留 `Marble`、`Campaign`、`Stages` 和说明页；保存会自动导入基础 JSON，也可使用 `Marbles ECS/Import Game Balance Excel`。
- 规范工作簿不再自动覆盖 `GameContent.json`。只有显式执行 `Marbles ECS/Import Attribute and Content Excel`，才会采用表中的属性数值；需要使用该菜单时，先把希望保留的 JSON 修改同步到表格。
- “运行时平衡表”是汇总审阅页，不被 Importer 自动读取；显式导入只读取属性字典，忽略旧药物设计行，装置每次直接解析当前 MRes 文件。
- 旧属性表将发射速度标为倍率 `x`；导入器统一标记为 `m/s`，数值本身不做额外缩放。
- `Editor/Balance/build_content.py` 只补充缺失的属性，保留其现有数值及内容规则；`Devices` 每次直接从当前 MRes 完整生成，不保留旧 JSON 中的装置行。数据完全一致时不重写文件。

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
| `MarbleZone` | `Kind / Multiplier / FlatScore / RushEnabled / ScoreStackEnabled` | 区域行为；`ScoreStackEnabled` 控制是否参与全局得分叠加，范围由 Trigger Collider 决定 |
| `MarbleBoardView` | `PinHeight` | 旧程序 HUD 的钉子高度；Prefab 自己的外观由 Transform 决定 |

`TestScene` 的放置障碍物已绑定 `PhysicsRoot/MarblePins`。不要把底板或动态装置的父对象加入障碍物列表。移动、缩放、旋转、禁用或删除固定钉后，放置检查跟随真实 Collider；不再按行列公式预留位置。增幅塔之间保留 `0.01` 间距规则，网格吸附后重新检查放置范围。

程序不生成盘面、墙体、固定钉和终点；这些均在场景中手工制作。发射滑条使用发射点启动时的局部 Z 坐标为中心，与实际移动轴一致。

发射中心为 `LaunchPoint.TransformPoint(LaunchOffset)`，偏移随发射点的移动、旋转和缩放变化，默认 `(0, 0, 0)`。编辑和运行时均可通过 Scene 视图的 Gizmos 预览；标记大小只用于定位，不代表弹珠半径。多球发射以此中心排列。

## 验证

Unity 菜单 `Marbles ECS/Validate Attribute and Content Runtime` 验证配置优先级、旧 JSON 字段忽略、Excel 输出、属性、装置、库存、赌博、提现、放置和物理表现。测试会进入 Play；如不希望占用当前场景，可在独立临时项目中运行。
