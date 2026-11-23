# 每日内容制作指南

`Assets/Resources/GameDays.json` 是每日内容的运行来源。基础生产门槛、奖励和发射数值仍从 `GameBalance.xlsx` 导入。测试用日期、分数和行为保存在 `Assets/Tests/Day` 的夹具中。

## 增加 Day 和生产计划

| 位置 | 字段与含义 |
| --- | --- |
| 根对象 | `schemaVersion` 为 1，`sequenceId` 是固定序列 ID，`days` 是按 `order` 排序的条目。可包含 `productionPlans`、`events`、`rules` 和 `campaignRuleIds`。 |
| `days[]` | `dayId` 唯一，`order` 为唯一正整数，`productionPlanId` 指向计划。可用 `eventIds`、`ruleIds` 引用当天行为和规则，`morningPrep` 定义房间前置目标。 |
| `productionPlans[]` | `planId`，以及 `firstStageId`、`lastStageId`。两端都是现有 Balance Stage ID，且首端位于末端之前或相同。单 Stage 计划可填正整数 `targetScore` 作为当天分数。 |

IDs 区分大小写，允许 1 至 120 个 UTF-8 字节并要求首尾无空白。菜单 `Marbles ECS → Validate Day Configuration` 检查重复 ID、冲突顺序、Stage 范围、事件、规则与行为类型。可用 `GameDaysLoader.Parse` 在测试中从字符串加载同一 Schema。

场景逻辑在 Controller 就绪后调用 `TryStartConfiguredDayCampaign`。调用者也能用 `TryStartDayCampaign` 安装已校验的定义。显示当前状态时读取 `Controller.Day`，以展示时读取的 `Version` 提交起床、结算和休息完成命令。清空日期后，配置仍可校验，显式启动会报告未配置。

`productionFeatures` 是功能开关列表，允许 `Shop`、`DevicePlacement`、`Rush` 和 `GamblingWheel`。省略或留空会以基础生产开始。

### Morning Prep

每项 `morningPrep` 需要唯一 `objectiveId`、`type` 和 `anchorId`。`ReadNote` 额外需要显示的 `text`。在 TestScene 的 Morning Prep 对象上，把 Anchor ID 绑定到摆放物件的场景 Transform。Day0 的纸条使用 `desk`。所有目标完成且玩家到达机器交互点后，场景允许按 E 开始生产。

## 增加 Event 和 Action

在 `events[]` 声明 `eventId`、`trigger` 和非空的有序 `actions`。可用 `priority` 提高执行顺序。事件策略为 `OncePerAttempt` 或 `EveryOccurrence`，省略时采用 `OncePerAttempt`。可选的 `condition` 对象用 `type` 和 `parameters` 引用已注册条件。Action 条目也需要 `type`，`parameters` 是由该类型定义和校验的可选字符串。

可用的 Trigger 是四个外层阶段各自的 `Entered` 和 `Exited`，以及 `BuildEntered`、`PlayingEntered`、`DrainingEntered`、`ShopEntered`、`RoundSettled`、`ProductionSucceeded`、`ProductionFailed`、`DayRetried`、`DayCompleted`。成功从生产进入当天结算时用 `DaySettlementEntered`。需要处理发射到排空这一回合的结果时，用 `RoundSettled`。完成休息的通知在当天作用域结束前派发。

编写行为时，在 `DayBehaviorRegistry` 以稳定的类型键注册 `RegisterAction<T>` 或 `RegisterCondition<T>`。类型注册负责把参数字符串转换为不可变的参数值，并在加载时报告非法参数。将同一套注册传入运行入口和验证脚本。新增正式类型时，可在 `GameDaysLoader` 的默认注册创建处加入项目的公共注册。Action 可从 `DayActionContext.Notice` 读取阶段和尝试信息，并通过 `RemoveRules(sourceId, scope)` 移除准确来源的规则。同步动作返回 `Task.CompletedTask`，异步动作返回真实的完成任务并响应取消令牌。Unity 与玩法调用保持在模拟主线程。

## 增加 Rule 和 Feature

`rules[]` 每项有唯一 `ruleId`、`type` 和 `scope`。Campaign 级规则由根对象的 `campaignRuleIds` 引用。其它规则由对应日期的 `ruleIds` 引用。

| 作用域 | 激活与回收 |
| --- | --- |
| `Campaign` | 序列开启时激活，跨 Day 保持，序列结束时回收。 |
| `Day` | WakeUp 之前启动，离开当天或重试时回收。 |
| `Phase` | 需要指定 `phase`，进入这个外层阶段时激活，退出动作完成后回收。 |
| `Temporary` | 需要提供正整数 `durationTicks`。进入 Production 后激活，持续指定的生产固定步数。 |

通用 `Attribute` 类型用 `attribute` 选择现有 `GameAttribute`，用 `operation` 选择有权限的 `Add`、`Multiply` 或 `Set`，用必填数值字段 `value` 和可选 `priority` 传入修改参数。作用域由 Day 层管理，对数值的合并、运算次序和限幅继续使用属性定义。

需要独立生命周期时，用 `RegisterFeature<T>` 注册类型和参数校验。工厂每次激活构造新的 `IDayFeature`。在 `Activate` 注册所需服务或订阅，在 `Deactivate` 停止响应，在 `Dispose` 释放运行资源。订阅与可能创建的 ECS 实体都要归到相同的作用域。Feature 的快照扩展应从明确的组件和持久数据建立，场景投影在恢复时重建。运行及恢复的边界说明见 [每日挑战运行架构](DayFramework_Architecture.md)。
