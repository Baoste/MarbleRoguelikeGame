# 每日挑战运行架构

每日流程和原生产流程共用 `MarbleSimulation` 的 World。`TestScene` 通过 `StartWithDayCampaign` 从 `Resources/GameDays.json` 启动 Day0。勾选 `EnableRandomStartingDevices` 会直接启动完整的弹珠机战役，发放全目录装置并启用全部生产功能。调用者也可显式调用 `TryStartConfiguredDayCampaign` 或 `TryStartDayCampaign`。关闭场景的每日挑战启动选项也可直接使用机台流程，旧场景验证菜单在各自的 PlayMode 会话选择这个流程。

```mermaid
flowchart LR
    W["WakeUp · Morning Prep"] -->|"完成前置任务并进入机器"| P["Production"]
    P -->|"计划中的最后 Stage 达标"| S["DaySettlement"]
    S -->|"提交当天结算"| R["Rest"]
    R -->|"仍有日期"| N["下一天 WakeUp"]
    R -->|"序列结束"| C["Campaign Completed"]
    P -->|"生产失败，恢复检查点"| W
    P --- Q["Build → Playing → Draining → RoundSettled"]
    Q -->|"仍有生产 Stage"| H["Shop → Build"]
```

## 权威状态与命令

| 所在位置 | 职责 |
| --- | --- |
| `SimulationContext.RoundEntity` | 原 `RoundData` 仍持有内层阶段，`CampaignData` 保存生产分数、Stage 区间与经济数据。开启 Day 后附加 `DayRuntimeData`、事件执行记录 Buffer 和活动规则 Buffer。 |
| `DayStateMachine`、`DayFlowSystem` | 校验命令与状态，串行提交转换、发送进入和退出通知，并独占 Day Index 写入。 |
| `GameDaysLoader`、`DayCampaignDefinition` | 复用项目的配置 JSON 读取器检查语法和字段类型。校验日期顺序和引用后发布不可变定义，配置通过后才允许切换正在运行的会话。 |
| `MarbleGameController.Day` | 提供只读进度、三个外层阶段完成命令和启动入口。Controller 在固定步之间轮询待完成的 Action，并在恢复时同步发射器和盘面。 |
| `DayEventSystem`、`DayRuleSystem` | 监听低频边界、执行类型化事件、管理规则来源和 Feature 生命周期。数值效果由 `AttributeRuntime` 与 `AttributeMath` 计算。 |

`DayProgressSnapshot` 给出当前 Sequence/Day ID、进度、Attempt、上一阶段、转换原因、结算标记、生产计划 ID、已结算回合数、生产 Tick 和随机状态。外层 UI 从快照取得 `Version` 并传给 `TryCompleteWakeUp`、`TryCompleteDaySettlement` 或 `TryCompleteRest`。版本过期、阶段不匹配和生命周期尚未结束时，命令返回具体原因。有效命令可以等待异步退出 Action，期间重复提交仍会被拒绝。退出通知完成后清理阶段规则，提交状态，再应用进入规则和通知。

启动 Day 时重用整局初始化。激活后，生产失利会自动恢复到当日检查点，旧重开按钮仅供原流程使用。本期的推进 UI 接入点为只读快照和上述命令。当天提交由 `TryCompleteDaySettlement` 对应的转换记录一次，已有 Stage 结算依旧在内层完成。完成 Rest 后才增加 `CompletedDays`，且只有这一个转换能更改日期。最后一天完成后，Campaign 进入 `Completed`，供诊断读取的 Day 阶段保留为 `Rest`。

## 生产计划与结算

`DayProductionPlan` 用首尾 Stage ID 绑定现有 Score 目标的连续区间。Stage 的累计分、越级奖励、提现和商店抽样仍由原生产系统计算。`CampaignSettlementSystem` 按计划上界截断本次结算跨过的 Stage。一个计划可以跑多个生产回合，也可以因越级较早完成。非末段结算为 `Shop`，购买或刷新后仍走 `EnterBuild` 和 `BeginRound`。

Day 的 `productionFeatures` 逐项开启 `Shop`、`DevicePlacement`、`Rush` 和 `GamblingWheel`，默认四项都关闭。关闭商店时，非末段结算直接返回 Build。关闭转盘时，其现有分区按普通得分区即时计分。单 Stage 的计划可提供独立 `targetScore`，日程、HUD、提现和结算共享这个目标。Day0 使用 100 分。

适配器在现有 `BeginRound`、`EnterBuild`、`CashOut` 和完整的 `AfterPhysics` 边界取结果。`SettlementComplete` 首次生效时发布 `RoundSettled`。若同一步进入排空并完成结算，先发 `DrainingEntered`，再发 `RoundSettled`。计划范围内的 `Won` 和 `Lost` 才触发 `ProductionSucceeded` 和 `ProductionFailed`。外层发射和布置命令只在无待处理生命周期的 Production 阶段可用。

## Morning Prep

Morning Prep 属于 `WakeUp`。ECS 保存当前尝试的目标完成记录，`TryCompleteWakeUp` 在剩余目标为零时才可进入生产。`MorningPrepController` 按配置生成房间物件，等待进入事件结束和自由相机就绪后淡入，淡入结束后才启用行走。玩家用 WASD 行走，按住鼠标右键转视角，靠近纸条按 E 阅读，关闭阅读窗口时提交目标。靠近弹珠机后按 E 提升 `VCamera_Machine` 的 Cinemachine Priority，镜头切换完成后由原生产控制器接手。重试重新实例化房间物件，恢复晨间目标和相机起点。

## 事件与规则

- 每天进入时按 Trigger 建索引。每次发生以单调序号去重，事件按优先级从高到低排列，同级按 Event ID 的序数排序。Action 按数组顺序执行。条件返回 false 时保留本次 Attempt 的执行资格。
- ECS Buffer 保存 Event ID、发生序号、动作位置和执行状态。`OncePerAttempt` 每次尝试最多执行一次，`EveryOccurrence` 每个通知序号最多一次。Action 返回的 Task 未完成时，状态机和生产命令等待。失败会记录 Day ID、Event ID 和 Trigger，终止该事件的剩余 Action，继续执行同批后续事件。销毁时取消未完成 Action，并关闭其受控上下文。
- 规则由 `ruleId`、`sourceId`、作用域和运行句柄标识。Campaign 的来源为 Sequence ID，其余来源为 Day ID。Phase 伴随外层阶段进退，Temporary 在 Production 入口激活，以当天累计的已完成生产固定步计时，离开 Production 时结束。Day 规则持续到离开当天或重试，Campaign 规则持续到序列完成。
- Attribute 规则进入现有全局属性 Modifier Buffer，沿用可用操作、加法、乘法、覆盖优先级、同级后注册者优先和最终限幅。运行句柄与原有外部 Modifier 来源分开存储。Feature 作为代码注册的规则类型，按作用域激活，退出时先 `Deactivate` 再 `Dispose`。恢复检查点时重新建立活动的 Campaign Feature 和当日 Feature 的订阅。Feature 若有私有的持久玩法状态，应接入明确的 ECS 快照边界。

## 检查点、重试和物理

每次进入新 Day，在内层准备好无活动弹珠的 Build 后、安装当日规则和发出 WakeUp 通知前捕获 `DayCheckpoint`。独立的恢复对象复用组件类型和装置实体构建入口。原有 `MarbleSnapshot`、`SessionSnapshot` 与库存/商店快照继续提供外部读取。

| 状态 | 捕获与恢复 |
| --- | --- |
| 生产和资源 | `RoundData`、`CampaignData`、`PlayerData`、发射器及移动组件、商店报价、全局属性及其 Modifier Buffer、出生序列、稳定 ID 计数和固定步状态。 |
| 装置 | 库存实例、Definition ID、成对 ID、价格、位置、朝向、耐久、状态文本、实例/回合数值、装置属性、耐久去重键及效果延续组件。重建实体时重绑 Player、Board 和 Definition 引用。 |
| 日期 | 已完成天数、Day ID、日期下标和转换版本留在外层。清掉失败尝试的事件记录，恢复检查点中的规则记录，然后用相同配置重新初始化当前 Day，Attempt 增加一次。 |
| PhysX | `IMarbleDayPhysics` 负责其局部场景。ECS 删除球、清瞬态和接触/得分队列后，Bridge 退役活动球及池内对象、清扫掠与设备缓存、复原当时的破钉集合。受影响的装置表现被同步拆除并按已恢复库存重建，重新同步碰撞体和发射器。 |
| RNG | 默认恢复 WakeUp 前的 `RoundData.RandomState`。调用者可注入 `DayRetryRandomPolicy`，根据检查点和失败尝试状态返回新的共享流状态。商店、Lua、开奖仍消费同一条序列。 |

恢复时使用新的 Round ID 和表现版本，装置保存的回合编号按偏移重映射，退役球的延迟接触失效。Lua 环境及物理对象的新实例从 ECS 状态和资源重建，持久玩法数据应通过实例 Buffer 保存，表现缓存随重建及正常回合回调刷新。捕获点限定为无活动球的 Build，因此中途刚体姿态、外部场景脚本私有状态和跨应用存档属于后续扩展。PhysX 穿透概率当前由球身份派生，新的 Round ID 与 PhysX 求解均会限制完全复现。回放策略保证共享玩法随机流恢复。

跨日准备集中在 `PrepareDayProduction`。当前技术行为是补血、重新开始计划分数和生产统计、清上一轮商店报价，保留已有钱包和布局，再捕获当日基线。这一接入点留给后续确认的继承策略。失败恢复覆盖当天的购买和刷新结果。选择延续失败 RNG、开始独立子流、开局资源配置、生产与商店编排、Rest 交互、事件选项和跨应用保存仍待策划确认。

## 文件索引

| 目录 | 文件与职责 |
| --- | --- |
| `Scripts/Day/` | `DayDefinitions` 与 `DayBehaviorRegistry` 定义内容和代码注册，`DayRuntime`、`DayStateMachine`、`DayFlowSystem` 驱动流程，`DayEventSystem`、`DayRuleSystem` 管理生命周期，`DayCheckpoint` 保存恢复数据。 |
| `Scripts/Simulation/` | `MarbleSimulation.Day.cs`、`MarbleGameController.Day.cs` 对接现有固定步、外部命令、生产结果和场景投影。 |
| `Scripts/Session/`、`Scripts/Marbles/` | Campaign 结算增加 Stage 区间，库存创建和检查点重建共享装置实体构建入口。属性 Modifier 记录规则句柄，`IMarbleDayPhysics` 定义物理检查点边界。 |
| `Scripts/Physics/PhysX/`、`Scripts/UI/`、`Scripts/Presentation/` | Bridge 与盘面投影实现物理清理和重建，现有按钮遵循生产命令可用性。 |
| `Resources/`、`Scripts/Configuration/`、`Editor/Validation/` | `GameDays.json`、`GameDaysLoader.cs` 和 `GameDaysValidation.cs` 提供制作、加载和校验入口。 |
| `Tests/Day/` | `DayFrameworkTests.cs` 和 `DaySceneTests.cs` 覆盖配置、ECS 流程、实际物理和旧场景。 |

## 验证入口

运行 `Marbles ECS → Validate Day Configuration` 验证 `GameDays.json`。空序列给出 `DAY_CONFIGURATION_UNCONFIGURED`。Unity Test Runner 中 `MarblesECS.Day.EditMode.Tests` 和 `MarblesECS.Day.PlayMode.Tests` 验证状态、检查点和实际场景。编辑器内容验证继续用 `Marbles ECS → Validate Attribute and Content Runtime`。数值工具继续使用 `Tools → Pachinko → Balance`。字段和扩展入口见 [每日内容制作指南](DayFramework_Content_Guide.md)。

本次在 Unity 2022.3.62f3c1 的隔离副本中验证。新增的 10 项 EditMode 和 3 项 PlayMode 测试通过。全量 EditMode 通过 64 / 65 项，全量 PlayMode 通过 9 / 9 项。内容验证通过 11,832 条断言，空 Day 配置校验、Excel 导入和使用已有数据的 Dashboard 加载均完成。

改造前提交 `9ffbaa6` 可复现三项回归检查失败。MResPacker 物理材质测试读到的反弹系数为零，预期为 0.7。Balance 物理采样在场景发射口拒绝生成弹珠。使用现有采样报告运行经济模拟时，Python 仍读取已撤下的 `Drugs` 字段。运行报告位于项目根的 `.utmp/day-framework-9ffbaa6/`，基线报告位于 `.utmp/day-framework-baseline-9ffbaa6/`。
