# 血色弹珠机：线性每日挑战框架改造任务说明（Codex）

> **FSM 修订说明：** 本版本明确采用两层 FSM。Day 外层固定为”起床 → 生产
> → 结算 → 休息”；生产内层复用现有弹珠机流程，核心概念为”Build → Playing
> → Draining →
> RoundSettlement”。如本文其他位置的旧表述与本说明冲突，以本 FSM
> 定义为准。

> **任务性质：** 在现有 Unity ECS 项目中增量增加”线性日期推进 +
> 每日独立挑战 + 当日失败重试 + 数据驱动事件/规则”的基础框架。  
> **重要限制：**
> 本阶段只完成框架，不设计、不填充任何具体日期的剧情、生产目标、特殊事件或道具配置。  

------------------------------------------------------------------------

## 0. 给 Codex 的首要指令

请先阅读当前项目的真实代码，再决定如何实现。不要把本文中的建议当作要求推翻现有架构的理由。

本任务不是重新开发弹珠机，也不是把项目从 ECS 改成 MonoBehaviour
架构。你的首要工作是：

1.  理解当前项目的 ECS World、Simulation
    生命周期、系统执行顺序、配置加载、阶段流转、快照与 PhysX 桥接方式。
2.  找到现有 Campaign / Round / Stage 机制分别代表什么，确认它们与未来
    Day（天）的关系。
3.  在不破坏现有玩法的前提下，将”线性每日挑战”作为一层可扩展的流程能力接入现有架构。
4.  尽可能复用现有组件、系统、命令入口、快照、配置加载和验证工具。
5.  只实现通用框架，不创建具体 Day 01、Day 02 等游戏内容。

**不要直接照搬其他项目的架构，不要为了实现通用性而新建一套与现有 ECS
并行的游戏框架。**

## 1. 游戏目标与本阶段范围

### 1.1 目标游戏流程

游戏采用固定顺序的线性叙事结构。每个 Day 的外层流程暂时固定为四个状态：

**起床（WakeUp）→ 生产（Production）→ 结算（DaySettlement）→
休息（Rest）**

- 游戏由一系列预先设计好的 Day 组成。
- 每个 Day
  是一个独立挑战，拥有自己的配置、生产目标、事件和规则；但本阶段不填入任何具体
  Day 内容。
- 外层 Day FSM
  只包含上述四个基础状态，不单独设置采购、准备、夜晚等外层状态。
- 生产（Production）状态内部运行现有弹珠机玩法
  FSM。其核心流程概念上为：**Build → Playing → Draining →
  RoundSettlement**。实际实现必须先审计现有状态机，并优先复用现有
  Build、Playing、Draining、Shop、Lost、Won
  等状态及其系统，不得重复创建一套平行的生产状态机。
- 外层 DaySettlement 与内层 RoundSettlement
  是两个不同层级的结算：前者负责当天挑战的结算，后者负责现有生产回合/轮次的结算。
- 玩家在 Production 阶段失败时，只重试当前
  Day：恢复当天开始时的检查点，然后回到同一天的
  WakeUp，不回到整个游戏的第一天。
- 玩家完成生产目标后进入 DaySettlement；当天结算完成后进入
  Rest；休息结束后才推进到序列中的下一天。
- 最后一个 Day 的 Rest 完成后，整个 Campaign
  才进入完成状态。CampaignCompleted 是 Campaign 终态，不是第五个 Day
  外层状态。
- 所有 Day 按固定顺序推进，不随机抽取日期，不随机生成每日事件。
- Day
  内部仍可保留现有商店刷新、物理随机性、道具组合等玩法机制；“日期和叙事固定”不等于禁止所有随机数。
- 当前没有确定任何具体每日内容，本次不得擅自设计或填入每日目标、事件、规则、剧情和奖励。

### 1.2 本阶段必须完成

- 日期序列的数据结构与加载/校验入口。
- Day 的运行时状态与外层四状态 FSM（WakeUp / Production / DaySettlement
  / Rest）。
- 现有生产玩法内层 FSM 的复用与外层/内层状态适配。
- Day 的运行时状态与流程推进能力。
- Day 与现有 Campaign / Round / Stage 机制之间的适配边界。
- Day 事件的触发和执行扩展点。
- Day 规则的注册、作用域、冲突处理和清理扩展点。
- 当天失败重试所需的状态快照与恢复流程。
- 当天成功后推进下一天的流程。
- 对配置、流程、重试和清理的自动化测试。
- 一份描述实际实现结构和扩展方法的开发文档。

### 1.3 本阶段明确不做

- 不设计 Day 01、Day 02 等具体内容。
- 不填写生产目标数值。
- 不设计停电、黑夜、药物变质等具体事件。
- 不设计具体剧情、对白、演出和结局。
- 不重做弹珠、装置、药物、商店、背包、物理或 UI 的既有玩法。
- 不引入随机地图、随机日期顺序或随机事件抽取。
- 不实现与本任务无关的存档、联网、任务编辑器或复杂可视化工具。
- 不大规模重构无关代码。

------------------------------------------------------------------------

## 2. 当前现状：已知信息与核对要求

根据当前现有项目分析文档，项目目前具有以下结构特征（实现前必须重新核实）：

- Unity 2022.3.62f3c1。
- Entities 1.0.11。
- URP 14.0.12。
- ECS 管理玩法权威状态；PhysX
  管理弹珠的位置、速度和碰撞运动；MonoBehaviour
  主要负责输入/命令桥接、物理桥接、快照读取和表现。
- `MarbleSimulation` 创建独立 Entities
  World，由控制器显式按既定顺序推进，不是简单依赖 Unity PlayerLoop
  自动更新。
- `MarbleGameController` 是重要的场景生命周期与外部调用入口。
- 当前已有 Build、Playing、Draining、Shop、Lost、Won 等阶段。
- 已有 Session 相关组件、Campaign 结算/商店系统、弹珠与装置系统、PhysX
  Bridge、快照和配置校验。
- 现有基础数值配置采用 `GameBalance.json`；内容配置包含
  `GameContent.json`；场景和装置资源也承担部分配置职责。
- 当前没有完整的跨应用持久化存档流程（请核实最新代码）。

### 2.1 优先检查的文档和代码

至少阅读：

- `AGENTS.md`
- `MarbleGameController` 的生命周期、Update、公开命令入口及其 partial
  文件
- `MarbleSimulation` 及其 Campaign、Snapshot、System 编排相关代码
- Session / Campaign / Round / Stage 相关 ECS Components
- Campaign 阶段流转、结算、商店、重开逻辑
- ECS 系统创建、更新顺序和销毁逻辑
- ECS Snapshot 的捕获与恢复实现
- `MarblePhysicsBridge` 的物理对象创建、销毁、回收、同步和重建逻辑
- `GameBalanceLoader`、GameContent 加载、配置深拷贝与校验
- 现有 Editor Validation、Balance Simulator 和测试代码

项目分析文档中列出的重点文件包括（路径可能随当前代码变化，请搜索确认）：

- `MarbleGameController.Lifecycle.cs`
- `MarbleGameController.Update.cs`
- `MarbleSimulation.cs`
- `MarbleSimulation.Campaign.cs`
- `AttributeRuntime.cs`
- `ContentSpawnSystem.cs`
- `ContentSettlementSystem.cs`
- `CampaignSettlementSystem.cs`
- `CampaignShopSystem.cs`
- `ContactResolutionSystem.cs`
- `DeviceEffectSystem.cs`
- `MarblePhysicsBridge.*.cs`

### 2.2 开始修改前先建立现状映射

先在 `Docs/` 下新增一份简短的审计文档，例如：

`Docs/DayFramework_Architecture_Audit.md`

至少回答：

1.  当前”整局 / Campaign”“阶段 / Stage”“回合 / Round”分别是什么概念？
2.  当前 `Build -> Playing -> Draining -> Shop -> ...`
    状态机具体由谁持有和修改？
3.  当前失败、胜利、重开分别会重置哪些 ECS
    组件、实体、随机数和物理对象？
4.  当前 Snapshot 是否能完整恢复 ECS 状态？是否包含
    RNG、DynamicBuffer、实体引用、布局、库存、商店、装置状态？
5.  PhysX 中的 Rigidbody / GameObject 是否能随 ECS Snapshot
    一起恢复？若不能，当前重建机制是什么？
6.  当前哪些数据是 ECS 权威状态，哪些是 MonoBehaviour 或场景状态？
7.  当前配置系统如何加载、校验、深拷贝和发布内容？
8.  当前是否已有事件、消息、命令、属性修正或效果管线可复用？
9.  当前系统的创建、更新和销毁顺序是什么？
10. 新的 Day 外层流程应该接在哪个最小边界上？

**完成审计后继续实施，不要只输出分析报告。**
如果发现关键架构事实与本文假设不同，优先遵循实际代码，并在审计文档中说明调整理由。

------------------------------------------------------------------------

## 3. 总体架构原则

### 3.1 保留现有 ECS 作为玩法状态权威

- 不新建第二个独立 ECS World 来管理 Day。
- Day 的运行时玩法状态应进入现有 `MarbleSimulation` 所属
  World，或使用项目已经存在的 Session/Simulation 状态管理方式。
- 不要在 MonoBehaviour
  中再保存一份可修改的血量、货币、库存、生产进度、装置布局或当前玩法阶段。
- MonoBehaviour 可以持有场景引用、UI
  引用和服务入口，但不能成为玩法数据的第二个权威来源。
- Day 配置是不可变的内容数据；Day Runtime 是运行时状态，两者必须分离。

### 3.2 两层 FSM：Day 外层流程与生产内层流程

必须采用清晰的两层状态机结构，不能把 Day
流程和弹珠机生产流程合并成一个巨大状态枚举。

需要明确区分：

- **Campaign：** 一次线性游戏流程/故事进度。
- **Day：** 固定日期序列中的一个独立挑战，由外层 Day FSM 管理。
- **Production Round / Stage：**
  当前弹珠机玩法中的生产回合或阶段，由内层生产 FSM 管理。

#### 外层 FSM：DayStateMachine

本阶段外层 FSM 固定为四个基础状态：

``` text
WakeUp（起床）
    |
    v
Production（生产）
    |
    v
DaySettlement（当天结算）
    |
    v
Rest（休息）
    |
    +------ 有下一天 ------> 下一天 WakeUp
    |
    +------ 已是最后一天 --> CampaignCompleted
```

生产失败是从 Production 返回同一天 WakeUp 的重试路径：

``` text
Production
    |
    +------ 生产失败
               |
               v
        RetryCurrentDay
               |
       清理并恢复当天检查点
               |
               v
        同一天 WakeUp
```

失败重试是状态转换及恢复操作，不需要增加一个长期停留的外层 FSM
状态。`CampaignCompleted` 是 Campaign 的终态，不属于 Day
的四个基础状态。

四个外层状态的职责：

| 状态            | 职责                                                                     | 退出条件                 |
|:----------------|:-------------------------------------------------------------------------|:-------------------------|
| `WakeUp`        | 初始化当天运行时、应用当天配置、触发起床阶段生命周期通知，并准备进入生产 | 当天开始条件满足         |
| `Production`    | 启动并托管现有生产内层 FSM；监听生产成功或失败结果                       | 生产目标成功，或生产失败 |
| `DaySettlement` | 执行当天级结算、提交当天结果，保证结算只执行一次                         | 当天结算完成             |
| `Rest`          | 执行当天结束/休息阶段的生命周期；不在本阶段预设具体剧情或玩法            | 休息阶段完成             |

注意：

- 本阶段不要把采购、准备、夜晚等增加为外层状态。
- 如果现有 `Shop`
  状态需要保留，应先审计它当前属于哪一段流程；它可以继续作为现有生产内层流程的一部分或既有阶段间流程，但不能因此擅自扩充
  Day 外层 FSM。
- `WakeUp`、`Production`、`DaySettlement`、`Rest` 是本阶段唯一的 Day
  外层状态。不要额外加入
  `DayStarting`、`DayActive`、`DayFailed`、`DayCompleted`
  等重复表达同一生命周期的外层状态；必要的结果、标志和进度应作为独立
  Runtime Data 表达。
- 每个 Day
  的具体事件和规则以后可以挂接到这四个状态的进入/退出触发点，但不得改变本阶段的基础外层流程。

#### 内层 FSM：现有生产玩法状态机

Production 外层状态内部运行弹珠机生产状态机。概念上的核心循环为：

``` text
Build（构筑/布局）
    |
    v
Playing（弹珠运行）
    |
    v
Draining（等待场上弹珠清空/收束）
    |
    v
RoundSettlement（生产回合结算）
    |
    +------ 需要继续下一轮 ------> Build
    |
    +------ 当天生产目标已完成 --> 向 Day FSM 报告 ProductionSucceeded
    |
    +------ 生产失败 -----------> 向 Day FSM 报告 ProductionFailed
```

这是内层玩法的概念流程，不代表要求新建同名状态或替换现有状态。实现时必须：

- 先检查当前已有的 `Build`、`Playing`、`Draining`、`Shop`、`Lost`、`Won`
  等状态的真实语义。
- 尽可能复用现有内层状态、组件、系统和结算逻辑。
- 如果现有项目没有独立的 RoundSettlement
  状态，应优先复用现有结算系统/结算事件，不要为了图示强行新增重复状态。
- 现有 `Shop` 如果属于生产回合之间的既有流程，应继续由内层流程或现有
  Campaign 机制管理，不要提升为 Day 外层状态。
- 内层 FSM 只向 Day FSM 报告生产结果，不直接修改当前 Day
  Index，也不负责推进下一天。
- 内层的 `Lost` / `Won` 必须经过适配层解释：生产失败映射为当前 Day
  失败；生产回合胜利不应未经判断就映射为整个 Campaign 完成。

`DaySettlement` 与 `RoundSettlement`
必须使用不同名称或明确的类型/作用域，避免两个层级的结算混淆。

#### FSM 实现要求

由于项目以 ECS 为玩法状态权威，状态机实现应优先使用 ECS Runtime
State、Transition Request 和系统处理，不要默认采用一组 MonoBehaviour
状态对象。

建议具备以下概念：

- **State：** 当前唯一有效状态。
- **Transition：** 明确允许的状态转换。
- **Guard：** 转换前置条件，例如生产是否成功、结算是否完成。
- **OnEnter / OnExit：** 进入和离开状态时触发的生命周期处理。
- **State Data：** 与当前状态关联的运行时数据，例如 Day
  ID、Attempt、结算标志。
- **Transition Validation：** 拒绝非法转换、重复转换和过期请求。

实现约束：

1.  状态变更必须经过统一的转换入口/系统验证，不允许任意系统直接写当前状态。
2.  转换时记录 `PreviousState`、`CurrentState`、`DayId`、`Attempt`
    和转换原因，具体字段按项目 ECS 风格实现。
3.  状态进入/退出效果通过 ECS
    系统、类型化事件或项目已有消息机制触发，不要在多个地方重复执行初始化。
4.  `OnEnter` / `OnExit`
    必须具备幂等保护，避免重复收到命令时重复创建实体、重复结算或重复播放流程。
5.  状态机必须有可测试的合法转换表；非法转换要被拒绝并记录原因。
6.  状态机逻辑应尽可能与 Unity 场景、UI 和 MonoBehaviour 解耦，便于
    EditMode 测试。
7.  不要同时维护 enum 状态、多个互相矛盾的状态 Tag 和 MonoBehaviour
    状态变量。选择一种权威状态表达，并让其他表现状态从它派生。

建议的 Day 外层合法转换：

| 当前状态        | 触发条件/命令            | 目标状态                                        |
|:----------------|:-------------------------|:------------------------------------------------|
| Campaign 未开始 | 开始 Campaign            | 当前 Day `WakeUp`                               |
| `WakeUp`        | 当天初始化完成           | `Production`                                    |
| `Production`    | 生产目标完成             | `DaySettlement`                                 |
| `Production`    | 生产失败                 | 执行当前 Day Retry，恢复后回到同一 Day `WakeUp` |
| `DaySettlement` | 当天结算完成             | `Rest`                                          |
| `Rest`          | 休息阶段完成且存在下一天 | 下一 Day `WakeUp`                               |
| `Rest`          | 休息阶段完成且没有下一天 | `CampaignCompleted`                             |

如果当前代码的阶段推进方式与表格不同，先在架构审计中说明，再以最小改动实现相同语义。

### 3.3 依赖方向

建议遵循：

``` text
Day Content / Campaign Configuration
                 |
                 v
       Day Flow Orchestration
                 |
        +--------+--------+
        |                 |
        v                 v
   Day Event Flow     Day Rule Flow
        |                 |
        +--------+--------+
                 |
                 v
       Existing ECS Gameplay
                 |
        +--------+--------+
        |                 |
        v                 v
   PhysX Bridge       Presentation/UI
```

约束：

- 核心玩法系统不允许判断 `currentDay == 3` 之类的日期条件。
- Blood、Marble、Device、Production、Shop 等既有系统不应该依赖 Day UI
  或剧情系统。
- Day 流程通过已有公开命令、ECS 组件、系统事件或明确接口驱动玩法。
- 不要为了”事件驱动”而把所有直接调用都改成全局 EventBus。
- 如果项目已有 ECS Event / Buffer / Command 机制，优先复用。
- 只在确实需要广播、解耦订阅者时使用事件通知。

------------------------------------------------------------------------

## 4. Day 内容配置层

### 4.1 配置格式选择

当前项目已经有 JSON
配置加载、校验和深拷贝流程。本次优先沿用现有配置体系。

建议新增独立的日期内容配置，例如：

`Assets/Resources/GameDays.json`

但请先检查当前资源加载规范。如果已有更合适的 Content
配置入口，可以沿用现有入口。不要同时用 JSON 和 ScriptableObject
维护同一份 Day 数据，也不要为了本功能引入新的序列化依赖。

`GameBalance.json`
继续负责适合表格维护的基础数值，不要把剧情事件、日期流程硬塞进平衡表。

### 4.2 数据结构建议

建立纯数据定义，不在配置对象中放运行时状态和业务执行逻辑。命名可按现有项目风格调整。

概念结构：

``` text
DaySequenceDefinition
  - schemaVersion
  - sequenceId
  - days[]

DayDefinition
  - dayId
  - order
  - enabled
  - startProfileId? 
  - productionPlanId?
  - shopProfileId?
  - phasePlanId?
  - ruleIds[]
  - eventIds[]
  - storySequenceIds[]?

ProductionPlanDefinition
  - planId
  - objectives / stages

DayEventDefinition
  - eventId
  - trigger
  - priority
  - executionPolicy
  - conditions
  - actions[]

RuleDefinition
  - ruleId
  - ruleType
  - parameters
  - priority
  - stackingPolicy
  - lifetime
```

以上是概念字段，不要求全部一次实现。只实现本阶段真正需要的最小字段和扩展点。

### 4.3 配置约束

- `dayId` 必须稳定且唯一，不要只依赖数组下标识别日期。
- `order` 决定固定顺序，加载后应校验顺序冲突。
- 事件和规则通过稳定 ID 引用。
- 配置加载后应进行完整校验，并按现有项目规范深拷贝或转成只读运行时定义。
- 错误信息应指出具体 Day ID、Event ID、Rule ID 和字段路径。
- 不允许配置直接指定任意 C# 类型全名、任意方法名或反射调用。
- 配置中的 `type` 必须映射到代码中显式注册的、经过校验的类型。
- 未知事件类型、未知规则类型、重复
  ID、缺失引用应在启动/内容验证阶段报出清晰错误，而不是运行中静默失败。

### 4.4 当前不填具体日期

本次只建立配置 Schema、加载和校验能力。

- 不创建实际的 Day 01/Day 02 内容。
- 不写入 100、500 等生产指标。
- 不添加停电、黑夜、药物变质等事件。
- 如果需要测试数据，只能放在测试代码的临时 Fixture
  中，不得混入正式游戏内容。
- 如果空 `days`
  序列不允许开始游戏，应给出清晰的配置错误，并保留现有测试场景/旧模式的可运行性。

------------------------------------------------------------------------

## 5. Day 运行时与流程控制

### 5.1 运行时状态

Day Runtime 必须与 Day Definition 分离。

结合当前 ECS 组件组织方式，增加必要的 Singleton Component /
DynamicBuffer / Runtime Data。具体结构以现有代码规范为准。

至少需要表达：

- 当前 Campaign / Sequence ID。
- 当前 Day ID 和顺序位置。
- 当前 Day 生命周期阶段。
- 当前 Day 尝试次数。
- 当前 Day 的结果（未完成、进行中、成功、失败）。
- 当前 Day 使用的随机种子或随机流标识。
- 当前事件执行记录（哪些事件已触发、执行到哪一步）。
- 当前启用的 Day 级规则来源。
- 当前 Day 的生产计划进度。

不要把所有字段机械地塞进一个巨大
Component。根据数据访问和生命周期拆分组件与 Buffer。

### 5.2 Day 生命周期与两层 FSM

本节的外层 FSM 定义以第 3.2 节为准。外层 Day FSM 只有四个状态：

``` text
WakeUp -> Production -> DaySettlement -> Rest
  ^            |
  |            |
  +-- Retry ---+   （生产失败时，恢复同一天并回到 WakeUp）

Rest -> 下一 Day 的 WakeUp
Rest -> CampaignCompleted（最后一天）
```

状态含义：

- `WakeUp`：初始化当前 Day
  的运行时状态、加载当天配置、创建当天检查点（若采用该时机），并触发起床阶段的进入通知。
- `Production`：启动并托管现有弹珠机生产内层
  FSM。采购、准备等不作为本阶段的外层状态。
- `DaySettlement`：生产成功后执行当天级结算。它与内层 `RoundSettlement`
  不同。
- `Rest`：当天结算结束后的休息阶段。当前只建立状态和生命周期扩展点，不设计具体休息玩法或剧情。

#### 5.2.1 外层状态转换表

| 当前状态        | 条件 / 命令          | 下一状态                        | 说明                          |
|:----------------|:---------------------|:--------------------------------|:------------------------------|
| Campaign 未开始 | StartCampaign        | 当前 Day `WakeUp`               | 初始化线性序列                |
| `WakeUp`        | 当天初始化完成       | `Production`                    | 进入内层生产流程              |
| `Production`    | 内层报告生产目标完成 | `DaySettlement`                 | 当天生产成功                  |
| `Production`    | 内层报告生产失败     | 恢复检查点后回到同一天 `WakeUp` | 不推进日期                    |
| `DaySettlement` | 当天结算完成         | `Rest`                          | 只结算一次                    |
| `Rest`          | 休息完成且有下一天   | 下一 Day `WakeUp`               | 日期只在此处推进              |
| `Rest`          | 休息完成且没有下一天 | `CampaignCompleted`             | Campaign 终态，不属于 Day FSM |

生产失败的 Retry 是一次有序的状态恢复操作，不必增加 `DayFailed`
作为外层常驻状态。可以使用独立结果字段或短暂的命令/请求表示失败原因。

#### 5.2.2 内层生产 FSM

Production 状态内部继续使用现有生产玩法 FSM。概念核心流程：

``` text
Build -> Playing -> Draining -> RoundSettlement
  ^                                  |
  +----------- 下一轮 ---------------+
```

内层 FSM 在完成当天生产目标或发生失败时，向外层 FSM 报告明确结果：

- `ProductionSucceeded`：请求外层从 `Production` 转到 `DaySettlement`。
- `ProductionFailed`：请求外层执行当前 Day Retry，恢复后回到同一天
  `WakeUp`。

必须审计并复用现有 `Build`、`Playing`、`Draining`、`Shop`、`Lost`、`Won`
等状态。上图是概念流程，不要求为了匹配图示而创建重复状态。若现有结算系统没有独立
`RoundSettlement` 状态，可用现有结算系统/事件表示该步骤。

#### 5.2.3 状态机实现约束

由于项目以 ECS 为玩法状态权威，优先使用 ECS Runtime State、Transition
Request 和系统处理，不要默认采用 MonoBehaviour 状态对象。

状态机至少要具备：

- **State：** 当前唯一有效状态。
- **Transition：** 明确允许的状态转换。
- **Guard：** 转换前置条件，例如当天初始化完成、生产目标完成、结算完成。
- **OnEnter / OnExit：** 进入和离开状态时的生命周期处理。
- **State Data：** Day ID、Attempt、当前结算标记等运行时数据。
- **Transition Validation：** 拒绝非法转换、重复转换和过期请求。

实现要求：

1.  所有状态变更经过统一的转换入口/系统验证。
2.  转换记录 `PreviousState`、`CurrentState`、`DayId`、`Attempt`
    和转换原因。
3.  OnEnter / OnExit 通过 ECS
    系统、类型化事件或现有消息机制触发，并保证幂等。
4.  不允许 UI 或表现层直接修改 ECS 状态。
5.  不要同时维护互相矛盾的 enum、Tag 和 MonoBehaviour
    状态变量；选择一种权威状态表达。
6.  状态转换逻辑应可独立进行 EditMode 测试。
7.  外层 `DaySettlement` 与内层 `RoundSettlement`
    必须使用不同名称或明确类型/作用域。

### 5.3 对外命令入口

沿用 `MarbleGameController` / `MarbleSimulation`
现有的公开命令风格，增加必要的 Day FSM 命令。概念上至少包括：

- `StartCampaign` / `StartSequence`：初始化 Campaign，并进入当前 Day 的
  `WakeUp`。
- `CompleteWakeUp` / `BeginProduction`：起床阶段完成后，请求进入
  `Production`。
- `ReportProductionSucceeded`：由内层生产
  FSM/适配层报告当天生产目标已完成，请求进入 `DaySettlement`。
- `ReportProductionFailed` /
  `RetryCurrentDay`：报告失败，执行当天快照恢复，并回到同一 Day 的
  `WakeUp`。
- `CompleteDaySettlement`：当天结算完成后进入 `Rest`。
- `CompleteRest` / `AdvanceToNextDay`：休息完成后进入下一 Day 的
  `WakeUp`，或在最后一天进入 `CampaignCompleted`。

不要照抄这些方法名，如果现有命令接口已有统一模式则沿用。命令名称可以调整，但四个外层状态和转换语义必须保持清晰。

要求：

- UI 只调用公开命令，不直接写 ECS Component。
- 命令必须验证当前阶段是否合法。
- 重复提交同一个命令不能导致重复结算、重复发奖或跳过日期。
- 重要状态转换应可记录日志，便于定位流程问题。

### 5.4 与现有 Campaign / Round 的适配

这是本次改造最重要的集成点。

- 保留现有弹珠生产、PhysX、装置、药物、商店和结算系统。
- 不要把现有 `CampaignSettlementSystem`、`CampaignShopSystem`
  等直接替换成新写的一套系统。
- Day FSM 作为外层流程，现有 Build / Playing / Draining
  等生产玩法状态作为内层 FSM；二者通过明确的命令或结果事件通信。
- 先建立 Day 与现有 Stage / Round 的映射，再决定最小修改点。不要假定一个
  Day 必然等于一个 Round。
- 如果现有 `Lost` 代表整局失败，应将”当前 Day
  生产失败”作为独立结果处理，并阻止它直接触发旧的整局重开逻辑。
- 如果现有 `Won` 代表整个旧 Campaign
  完成，不要直接把它当作”当天成功”。应区分 Round 结算成功、Day
  生产目标完成、Day 结算完成和整个 Campaign 完成。
- 内层 FSM 不允许直接修改当前 Day Index，也不负责切换到下一天。
- 外层 FSM 不负责实现 Build、弹珠运行、Draining、物理碰撞或 Round
  内部结算。
- `RoundSettlement` 与 `DaySettlement` 必须保持独立语义。
- 不要在多个系统中重复判定生产目标。目标判定仍应有单一权威实现，内层生产流程负责产生结果，Day
  FSM 负责接收并推进外层状态。
- 当前配置中的目标究竟代表分数、弹珠数量还是其他指标，必须阅读实际结算代码后确认；不要擅自将现有分数目标解释为弹珠数量。

------------------------------------------------------------------------

## 6. 事件系统：Trigger 与 Action 分离

事件框架应能表达”何时触发”和”触发后执行什么”，但本阶段不需要填入具体事件。

### 6.1 Trigger

事件触发点应与两层 FSM
的真实状态转换对齐，优先定义稳定、有限、可测试的生命周期触发点。

外层 Day FSM 触发点概念上包括：

- `OnWakeUpEntered`
- `OnProductionEntered`
- `OnDaySettlementEntered`
- `OnRestEntered`
- `OnDayRetry`
- `OnDayCompleted`

内层生产 FSM 可以复用或补充现有生产阶段触发点，例如：

- `OnBuildEntered`
- `OnPlayingEntered`
- `OnDrainingEntered`
- `OnRoundSettlementEntered`
- `OnProductionSucceeded`
- `OnProductionFailed`

以上是扩展点，不要求本阶段为每个触发点都创建实际事件内容。不要再把
`OnProcurementOpened`、`OnNightStarted` 等作为外层 FSM
的独立阶段；如果未来确实需要相关行为，应将其作为某个现有状态中的事件/Action/Feature，而不是擅自增加外层状态。

具体触发点以实际流程为准。不要一次性设计几十个当前没有调用方的 Trigger。

触发点应由状态转换系统或既有玩法系统在明确的状态边界发布。事件调度器不应每帧扫描全部事件。同一状态转换对应的生命周期事件只能发布一次。

### 6.2 Event Definition

每个事件至少应有：

- 稳定 ID。
- Trigger 类型。
- 执行顺序 / Priority。
- 是否仅执行一次或允许重复执行的策略。
- 可选条件。
- 有序 Action 列表。

事件执行状态必须放在 Runtime State，而不是写回共享的配置资产。

### 6.3 Action

简单、可复用的行为可以拆成 Action。Action
通过显式类型注册表或项目现有的类型化机制执行。

要求：

- 不使用任意反射调用。
- 不允许配置字符串直接执行 C# 方法。
- Action 不应持有跨局的可变状态。
- Action 的执行上下文应提供受控的系统访问能力，不要把整个 World
  或所有全局单例无边界地暴露出去。
- 同一事件的 Action 应按确定顺序执行。
- 对异步剧情/演出，应支持完成回调或显式执行状态，不能在演出未完成时错误地推进流程。
- Action 失败时要有明确策略：中止当前事件并记录错误，还是允许后续 Action
  继续；不要静默吞掉异常。

### 6.4 Feature：处理复杂特殊玩法

不要把所有特殊玩法都拆成大量微小 Action。

对于具有完整生命周期、专属状态或复杂交互的特殊机制，使用独立
Feature/System 模块：

``` text
Initialize(context)
Activate()
Deactivate()
Cleanup()
```

接口形式可以按当前 ECS 架构调整。Feature 的玩法状态仍应归 ECS
管理；MonoBehaviour 只负责必要的表现适配。

本阶段只建立可扩展边界，不实现任何具体 Feature。

### 6.5 不要重复造 EventBus

先检查项目现有 ECS
Event、DynamicBuffer、EntityCommandBuffer、消息或命令机制：

- 能复用就复用。
- 不要再引入第二套全局消息中心。
- 不要让高频物理碰撞事件经过通用 Day Event 调度器。
- Day Event 只处理低频的日期/阶段/剧情/规则生命周期事件。

------------------------------------------------------------------------

## 7. 规则系统：规则来源、作用域与清理

事件表示”发生了什么”，规则表示”当前玩法遵循什么约束”。

规则可能来自：

- 当前 Day。
- 当前 Day 的某个 Phase。
- 装置、药物或道具（如果现有项目已有类似修正机制）。

本阶段只建立必要的通用接口，不实现具体规则内容。

### 7.1 规则生命周期

至少区分：

- Campaign 级：跨 Day 保留。
- Day 级：当前 Day 有效，结束或重试时按策略清理/重建。
- Phase 级：只在当前阶段有效。
- Temporary 级：有明确结束条件或持续时间。

每条运行时规则都应有稳定的 `ruleId` 和
`sourceId`，使其可以准确移除，不能只靠全局 `ClearAll()` 粗暴清理。

### 7.2 规则冲突

不要假设所有规则都能用倍率相乘。

不同规则需要不同的合并语义，例如：

- 数值倍率：可能相乘。
- 数值加成：可能相加。
- 开关状态：可能按优先级覆盖。
- 禁用/允许：可能采用明确的优先级或否决规则。
- 集合替换：需要定义覆盖来源。

请实现少量明确、可测试的合并策略，不要在本阶段制作通用表达式语言。

### 7.3 复用已有属性与效果系统

先检查 `AttributeRuntime`、装置效果、药物效果和现有属性修正流程。

- 如果已有 Attribute / Modifier 系统，优先扩展和复用。
- 不要创建第二套与现有属性系统平行的数值修正机制。
- 只有现有系统无法表达的 Day 规则，才新增独立规则接口。
- 规则系统不应让 BloodSystem、DeviceSystem 等反向依赖 Day 配置类型。

------------------------------------------------------------------------

## 8. 当天失败重试与 Snapshot

这是本次功能的关键正确性要求。

### 8.1 重试语义

默认目标：

- 玩家失败后只重试当前 Day。
- 已成功完成的历史 Day 不回退。
- 当前 Day 开始时的状态是重试基准。
- 当前 Day
  内产生的临时资源变化、购买、布局变化、生产状态和事件状态，应按定义恢复。
- 重试后不能残留上一次尝试的弹珠、物理对象、装置临时状态、规则、事件订阅或表现对象。

具体哪些资源跨 Day 继承、哪些资源在 Day
失败时回滚，目前尚未做最终策划决定。请不要擅自添加永久成长、惩罚、奖励或资源继承规则。先把
Snapshot 边界做成明确、可扩展的机制，并在文档中列出尚待策划决定的策略。

### 8.2 Snapshot 不能只复制几个数值

必须先审查现有 Snapshot 实现，尽可能扩展现有能力。

检查范围至少包括：

- ECS Components。
- DynamicBuffer。
- Entity 之间的引用关系。
- Session / Campaign 状态。
- 血量、货币、库存和商店状态。
- 装置实例、位置、朝向、布局和耐久（若存在）。
- 当前生产阶段和生产统计。
- 随机数状态或随机流状态。
- Day 事件触发记录。
- Day 规则运行状态。
- 其他会影响下一次模拟结果的状态。

PhysX 是独立的运动权威，因此还必须处理：

- Rigidbody / Collider 对象的销毁和重建。
- 物理对象池状态。
- 物理 Transform、Velocity、AngularVelocity（若需要恢复）。
- ECS Entity 与 PhysX GameObject 的映射。
- 物理场景中的残留碰撞体。
- Bridge 的缓存、接触队列和延迟回读数据。

如果现有 Snapshot
不能覆盖某类状态，必须明确记录缺口，并实现对应的恢复/重建步骤。不能只恢复
ECS 后就认为重试完成。

### 8.3 Snapshot 接口

如果现有系统已经有快照协议，优先扩展该协议。

如果没有，可以按系统边界建立强类型 Snapshot Provider。避免一个巨大的
`object` 字典或依赖运行时类型转换的通用容器。

概念接口：

``` csharp
public interface IDaySnapshotParticipant<TSnapshot>
{
    TSnapshot Capture();
    void Restore(TSnapshot snapshot);
}
```

实际接口应服从当前代码风格。不要为了接口抽象而让所有小组件都实现不必要的
Snapshot。

### 8.4 重试流程顺序

重试的目标是回到同一个 Day 的 `WakeUp`，而不是进入下一天或回到 Campaign
第一日。重试时应有明确且可测试的顺序，概念上包括：

1.  停止当前生产模拟和输入，阻止内层 FSM 继续推进。
2.  结束/清理当前 Day 的临时 Feature。
3.  清理当前尝试产生的 ECS 临时实体。
4.  清理或重建对应 PhysX 对象与桥接状态。
5.  恢复 Day 开始时的 ECS / Session / Inventory / Layout 等快照。
6.  恢复随机数策略。
7.  清除旧尝试的事件执行记录和 Day/Phase 级规则。
8.  根据当前 Day 配置重新初始化规则和事件。
9.  重置内层生产 FSM 至当天初始生产状态（通常对应
    Build，但必须以现有内层 FSM 的真实初始化流程为准）。
10. 重建表现层需要的对象/UI 状态。
11. 增加当前 Day 的 Attempt 记录，并将外层 FSM 设置回同一 Day 的
    `WakeUp`。

恢复顺序必须结合项目生命周期调整，尤其注意 ECS Entity 和 PhysX
对象的引用关系。`WakeUp` 的进入处理不得再次推进 Day
Index；重试必须保留当前 Day ID。

### 8.5 随机数策略

日期顺序和事件触发顺序固定，但商店、道具或玩法内部仍可能有随机数。

请检查现有 RNG 的所有权和 Seed 管理。不要另建一个互不协调的随机数系统。

为 Day Retry 留出明确的随机策略：

- 同一 Day 的重试是否复用相同 Seed。
- 每次尝试是否生成新的 Attempt Seed。
- 商店刷新是否使用独立随机流。
- 物理随机性是否与内容随机性隔离。

本阶段不要擅自决定最终玩法倾向；先让随机数来源可追踪、可注入、可测试，并将策略做成明确配置/接口。若现有
RNG 无法分流，先记录风险，再以最小改动扩展。

------------------------------------------------------------------------

## 9. Day 结算、休息与下一天推进

外层 FSM 必须严格遵循：

`WakeUp -> Production -> DaySettlement -> Rest -> Next Day WakeUp`

当内层生产 FSM 报告当天生产目标完成时：

1.  验证当前外层状态为 `Production`，且当前 Day 尚未成功提交。
2.  停止/收束内层生产流程，向外层 Day FSM 报告 `ProductionSucceeded`。
3.  外层 FSM 转换到 `DaySettlement`。
4.  执行当天级结算，并确保结算只执行一次。
5.  清理仅属于生产阶段或当天结算前的临时状态；具体哪些规则保留到 Rest
    或下一天，需按作用域处理。
6.  当天结算完成后，外层 FSM 转换到 `Rest`。
7.  `Rest` 阶段完成后，才允许推进 Campaign Progress 中的当前 Day。
8.  如果存在下一 Day，加载下一 Day 配置并进入下一 Day 的 `WakeUp`。
9.  如果已经到达序列末尾，设置 CampaignCompleted
    终态；不要再创建一个虚构的 Day 状态。
10. 不允许 UI 连点或重复事件造成跳过多个 Day、重复结算或重复推进。

生产失败则不进入 `DaySettlement` 和 `Rest`：执行当前 Day
Retry，恢复当天检查点后回到同一个 Day 的 `WakeUp`。

日期推进应由单一权威流程负责，不允许多个系统各自递增 Day
Index。`DaySettlement` 只处理 Day 级结算，内层 `RoundSettlement`
只处理生产回合级结算。

------------------------------------------------------------------------

## 10. 配置加载、校验和运行时安全

沿用现有配置加载规范，尤其注意项目已有的：

- `GameBalance.json`
- `GameContent.json`
- 深拷贝与完整校验
- Editor Validation
- 配置源与运行时 JSON 的 Source of Truth

要求：

- Day 内容配置加载失败时提供具体错误。
- 加载和校验失败不得覆盖有效运行配置。
- 配置数据进入运行时后不允许被 Action 或 System 原地修改。
- 配置与 Runtime State 分离。
- Day / Event / Rule 引用必须校验。
- 不要让某个 Day 的运行时状态污染其他 Day 的配置资产。
- 保持现有 Balance Excel -\> JSON 的工作流不变。
- 不要为了 Day 框架修改现有 GameBalance 数值或 GameContent 条目。

如果新增 `GameDays.json`，请增加对应的解析、校验和 Editor
验证入口；没有真实日期内容时允许空序列作为”尚未配置”的状态，但不能悄悄把它解释成一个有效可玩的
Day。

------------------------------------------------------------------------

## 11. 测试与验收

本任务不能只以”能编译”为验收标准。

### 11.1 EditMode / 单元测试

至少覆盖：

1.  Day Sequence 可以按固定顺序加载。
2.  重复 Day ID 会被拒绝。
3.  重复 Event ID / Rule ID 会被拒绝。
4.  不存在的事件或规则引用会给出明确错误。
5.  空 Day Sequence 被正确识别为未配置状态。
6.  外层 FSM 只允许 `WakeUp -> Production -> DaySettlement -> Rest`
    的正常成功路径。
7.  外层 FSM 拒绝跳过状态、逆向跳转和重复提交的非法转换。
8.  Production 失败时执行 Retry，并回到同一个 Day 的 WakeUp，不进入
    DaySettlement、不推进 Day Index。
9.  Retry 只恢复当前 Day，不回退已完成的历史 Day。
10. DaySettlement 完成后进入 Rest；Rest 完成后才推进下一 Day。
11. 最后一天 Rest 完成后进入 CampaignCompleted。
12. 内层 FSM 的 Build、Playing、Draining、RoundSettlement
    核心流程能够按既有规则运行。
13. 内层 RoundSettlement 不会误触发外层 DaySettlement。
14. 同一状态进入/退出通知不会因为重复命令而执行多次。
15. 同一事件不会因为重复收到同一生命周期通知而意外执行多次。
16. 事件 Action 按配置顺序执行。
17. Day / Phase 级规则可以按 sourceId 正确移除。
18. 不同规则合并策略有确定、可重复的结果。
19. RNG 状态按所选 Retry 策略正确恢复或重置。

### 11.2 PlayMode / 集成测试

至少覆盖：

- 现有 TestScene 能正常启动。
- 现有弹珠发射、PhysX 模拟、接触处理、结算和商店流程不被破坏。
- Day 外层 `WakeUp` 能初始化当天并进入 `Production`。
- `Production` 外层状态能驱动/托管现有 Build、Playing、Draining
  等内层生产流程。
- 内层生产成功后，外层按顺序进入 `DaySettlement`、`Rest`，再推进下一天。
- 内层生产失败后，ECS 和 PhysX
  状态均恢复，不残留旧弹珠或碰撞体，并回到同一天的 `WakeUp`。
- 重试后能再次完成同一天。
- 内层 RoundSettlement 与外层 DaySettlement 不会相互误触发。
- Day 结束/重试后不存在重复事件订阅或残留规则。
- 场景销毁和重新创建 Simulation 不会残留旧 World、System 或物理对象。

### 11.3 回归要求

- 不改变现有 GameBalance 数值。
- 不改变 GameContent 中既有装置、药物和属性定义。
- 不破坏现有 Balance Simulator / Balance Dashboard 工作流。
- 不改变现有 PhysX 权威边界。
- 不引入第二套弹珠运动模拟。
- 不让 UI 直接修改 ECS 玩法数据。
- 不新增未经需要的第三方包。

如果 Unity Editor
或测试环境不可用，应明确报告哪些测试未执行、原因是什么，不得声称已通过。

------------------------------------------------------------------------

## 12. 推荐实施顺序

请按小步提交式改造思路实施，每一步都保持项目可编译。

### Phase A：审计和映射

- 阅读目录 `AGENTS.md`。
- 阅读项目结构分析文档和相关源码。
- 画出现有 Campaign / Stage / Round / PhysX / Snapshot 关系。
- 记录 Day 外层流程的最小接入点。
- 输出 `Docs/DayFramework_Architecture_Audit.md`。

### Phase B：数据模型和加载

- 增加 Day Sequence / Day Definition 等纯数据模型。
- 增加配置解析、校验和只读运行时数据。
- 不创建具体 Day 内容。
- 增加空配置测试与非法配置测试。

### Phase C：两层 FSM、ECS 运行时状态和流程命令

- 在现有 Simulation / Session 结构中增加 Day Runtime State。
- 实现外层四状态 Day
  FSM：`WakeUp`、`Production`、`DaySettlement`、`Rest`。
- 明确合法 Transition、Guard、OnEnter / OnExit 和非法转换处理。
- 保持现有生产内层 FSM，核心概念流程为
  `Build -> Playing -> Draining -> RoundSettlement`；实际实现以审计后的现有状态机为准。
- 建立 Day 外层 FSM 与生产内层 FSM
  之间的结果适配：生产成功、生产失败、回合结算等结果必须有明确边界。
- 增加开始、进入生产、报告生产成功/失败、重试、完成当天结算、完成休息和推进下一天等命令。
- 不在 MonoBehaviour 中创建第二份玩法状态。

### Phase D：事件和规则扩展点

- 接入生命周期 Trigger。
- 建立类型化 Event Definition 和 Action 执行机制。
- 建立 Rule 注册、作用域、合并和清理机制。
- 复用现有 ECS 事件、属性修正和效果系统。
- 只实现框架，不实现具体事件和规则内容。

### Phase E：Snapshot / Retry

- 扩展现有 Snapshot。
- 补齐 ECS 与 PhysX 恢复。
- 处理临时实体、事件状态、规则状态、随机数状态。
- 增加失败重试集成测试。

### Phase F：验证与文档

- 执行现有可用的 EditMode / PlayMode 测试。
- 执行现有配置验证。
- 检查现有 Balance 工具回归。
- 更新 `Docs/DayFramework_Architecture.md`。
- 输出改造总结、文件清单、测试结果和待策划确认项。

------------------------------------------------------------------------

## 13. 明确禁止的实现方式

以下实现视为架构不合格：

1.  为每一天创建 `Day01Manager`、`Day02Manager` 等独立管理器。
2.  在核心系统中大量编写 `if (currentDay == N)`。
3.  新建一个巨大的
    `DayManager`，把日期、商店、血量、剧情、事件、生产和存档全部塞进去。
4.  新建一套 MonoBehaviour 玩法状态，与 ECS 中的状态重复保存。
5.  为 Day 额外创建一个独立 ECS World，却无法解释它与现有
    `MarbleSimulation` 的关系。
6.  未检查现有 Event / Snapshot / Attribute 系统就重新实现一套。
7.  通过字符串反射执行任意方法。
8.  把运行时事件执行状态写进共享 ScriptableObject 或配置资产。
9.  重试时只重置血量和货币，却留下旧 ECS Entity 或 PhysX Rigidbody。
10. 直接删除旧 Campaign / Round 逻辑，导致现有 TestScene 无法运行。
11. 擅自创建具体每日内容、目标数值或剧情。
12. 为了未来可能出现的需求实现复杂的通用脚本语言、表达式解析器或可视化编辑器。

------------------------------------------------------------------------

## 14. 完成后的交付物

完成代码后，请提供：

### 14.1 代码

- Day 内容定义与加载/校验。
- Day ECS Runtime State。
- Day 外层 FSM、合法转换表、Guard、OnEnter / OnExit 和转换验证。
- 生产内层 FSM 的复用与 Day 结果适配。
- Day 流程与命令入口。
- Day 与现有 Campaign / Round 的适配。
- Event Trigger / Action 扩展机制。
- Rule 生命周期与清理机制。
- Day Snapshot / Retry。
- 对应测试。

### 14.2 文档

新增：

- `Docs/DayFramework_Architecture_Audit.md`：现有代码审计与接入映射。
- `Docs/DayFramework_Architecture.md`：最终实际架构、数据流、系统职责、状态转换。
- `Docs/DayFramework_Content_Guide.md`：未来如何新增
  Day、Event、Rule、Action、Feature。

文档必须描述实际代码，不要只复制本任务说明。

### 14.3 最终汇报格式

完成后按以下结构汇报：

1.  **现有架构理解：** Day 层如何接入现有 ECS。
2.  **新增模块：** 新增了哪些组件、系统、配置和接口。
3.  **修改模块：** 修改了哪些既有系统，为什么必须修改。
4.  **状态流：** Day 开始、生产、失败重试、成功推进的完整链路。
5.  **Snapshot 覆盖：** 哪些 ECS / PhysX / RNG
    状态可以恢复，哪些仍有限制。
6.  **配置方式：** 未来如何添加一天、添加事件和规则。
7.  **测试结果：** 实际执行了哪些测试，结果如何。
8.  **未解决问题：** 哪些策略需要策划后续决定。
9.  **风险与回滚：** 若新流程有问题，如何关闭新入口并恢复原有测试流程。

------------------------------------------------------------------------

## 15. 设计决策记录：当前明确与待定

### 已明确

- 游戏采用固定顺序的线性 Day 序列。
- 每一天的事件和规则由策划预先设计，不随机抽取。
- Day 外层 FSM
  本阶段固定为四个状态：`WakeUp`（起床）、`Production`（生产）、`DaySettlement`（当天结算）、`Rest`（休息）。
- 正常成功路径固定为
  `WakeUp -> Production -> DaySettlement -> Rest -> 下一天 WakeUp`。
- 生产失败时只重试当前 Day：恢复当天检查点并回到同一天的 `WakeUp`。
- Production 内部保留现有生产内层 FSM；概念核心流程为
  `Build -> Playing -> Draining -> RoundSettlement`，实际复用现有状态与系统。
- 外层 `DaySettlement` 与内层 `RoundSettlement` 是不同层级，不能混淆。
- 不把采购、准备、夜晚等设为本阶段的外层 FSM 状态；现有 Shop
  等状态按审计结果保留在原有流程层级。
- 弹珠机现有 ECS / PhysX 核心玩法尽量复用。
- 本阶段只完成框架，不创建具体每日内容。
- 需要支持后续策划大改，避免日期逻辑侵入核心玩法系统。

### 尚未确定，不得擅自决定

- 一个 Day 的 Production 阶段对应一个还是多个现有生产 Round / Stage。
- 现有 Shop 状态在当前生产流程中的确切位置，以及如何映射到 Day 外层
  Production 阶段。
- 每日开始时血量、货币、背包、设备和布局如何继承。
- 当天失败时商店刷新、购买结果和随机数是否完全回滚。
- 重试是否使用相同随机 Seed。
- 成功通关后哪些资源跨 Day 保留。
- `Rest`
  阶段未来是否包含可交互内容；本阶段只实现状态与生命周期扩展点，不填充具体内容。
- 事件是否允许玩家选择，以及选择结果如何持久化。
- 是否需要完整的跨应用 Campaign Save / Load。

请在代码中为这些决策保留清晰边界，并在文档中列为待策划确认项。不要用未经确认的默认值把未来设计锁死。

------------------------------------------------------------------------

## 16. 仓库工作约束

- 必须遵守 `AGENTS.md`。
- 项目根目录是包含 `Assets`、`Packages` 和 `ProjectSettings` 的目录。
- 临时脚本、备份、日志、中间产物和预览文件放在项目根目录 `.utmp/`。
- 执行 Python 等工具时，按 `AGENTS.md` 设置 `TEMP`、`TMP` 和
  `PYTHONDONTWRITEBYTECODE`。
- Unity 批处理日志写入 `.utmp/`。
- 不要把临时文件散落到 `Assets/` 或提交进 Git。
- 不修改 Unity / Entities 包版本，除非审计后证明实现必需且获得明确理由。
- 不覆盖用户未提交的工作，不清理与本任务无关的文件。
- 修改前检查 `git status`；完成后检查 diff，确保没有无关变更。

**最终原则：先理解现有 ECS，再以最小改动增加 Day
外层流程。让日期、事件和规则成为可配置内容，让玩法能力继续留在现有 ECS
系统中。**
