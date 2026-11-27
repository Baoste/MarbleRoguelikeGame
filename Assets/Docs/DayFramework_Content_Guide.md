# 每日内容制作指南

`Assets/Resources/GameDays.json` 配置每日目标分、通关奖励和每日内容。基础发射和战役参数直接在 [GameBalance.json](../Resources/GameBalance.json) 中编辑。测试用日期、分数和行为保存在 `Assets/Tests/Day` 的夹具中。

## 增加 Day

| 位置 | 字段与含义 |
| --- | --- |
| 根对象 | `schemaVersion` 为 3，`sequenceId` 是固定序列 ID，`days` 按 `order` 排序。`startingCompanyScrip` 控制新 Campaign 的工票初始值，默认 0。可配置 `defaultProductionFeatures`、`events`、`rules` 和 `campaignRuleIds`。 |
| `days[]` | `dayId` 唯一，`order` 为唯一正整数，`targetScore` 范围为 1 到 `Marble.MaxRoundScore`。`boardLayout` 指定盘面 JSON。可用 `eventIds`、`ruleIds` 引用当天行为和规则，`morningPrep` 定义房间前置目标。 |

根对象的 `defaultProductionSettings` 定义生产规则，每天可用 `productionSettings` 覆盖个别字段。规则包括 `deviceSupply`（库存实例 `OwnedInventory` 或已选种类 `SelectedTypes`）、`installDuringProduction`、`destroyExhaustedDevices`、`allowDeviceReturn`、`allowDeviceSale` 和 `clearDevicesOnDaySettlement`。生产货币固定为血肉残料，可省略 `currency` 或填写 `BiomassScrap`。当天的 `initialBiomassScrap` 设置起始残料，`unlockedPegIds` 和 `defaultPegIds` 设置类型范围与初始权限。`skipMorningPrep` 默认关闭。开启时，由状态机确认已预选的背包并进入生产，预选类型须满足最低数量要求。

IDs 区分大小写，允许 1 至 120 个 UTF-8 字节并要求首尾无空白。菜单 `Marbles ECS → Validate Day Configuration` 检查配置字段、重复 ID、冲突顺序、事件、规则与行为类型。可用 `GameDaysLoader.Parse` 在测试中从字符串加载同一 Schema。

每次尝试结算一个生产回合。弹珠排空且达标后，进入 DaySettlement 时按 `clearCompanyScripReward` 发放工票，同一个 Day ID 只到账一次。结算会清空血肉残料和盘面装置，工票及永久权限留在当前 Campaign。失败重试恢复 `initialBiomassScrap` 和盘面起始状态，保留当天货架、抽取次数、已抽权限、工票支出和已确认的背包。

场景逻辑在 Controller 就绪后调用 `TryStartConfiguredDayCampaign`。调用者也能用 `TryStartDayCampaign` 安装已校验的定义。显示当前状态时读取 `Controller.Day`，以展示时读取的 `Version` 提交起床、结算和休息完成命令。清空日期后，配置仍可校验，显式启动会报告未配置。

`defaultProductionFeatures` 和各日的 `productionFeatures` 取并集，可配置 `Rush` 和 `GamblingWheel`。版本 3 自动接入装置安装，局外抽取由 `shopEnabled` 控制。得分区用 `rushEnabled` 控制进球是否为 RUSH 充能。使用版本 2 的库存关卡可设置 `clearCompanyScripReward` 和 `initialBiomassScrap`，并使用其原有布置流程。

### 血量与血珠升值

在 `GameDays.json` 的 `defaultBloodProgression` 设置共用曲线。某天的 `bloodProgression` 可以覆盖 `initialBlood`、`stages` 或两者。当天的 `initialBlood` 同时是开局血量和回血上限。省略曲线时，基础价值沿用 `GameContent.json` 的属性配置。用 `"stages": []` 可以为单日关闭升值曲线。

```json
"bloodProgression": {
  "initialBlood": 100,
  "stages": [
    { "spentBlood": 0, "baseValue": 0.5 },
    { "spentBlood": 30, "baseValue": 1 },
    { "spentBlood": 60, "baseValue": 2 }
  ]
}
```

`spentBlood` 是发射前已实际扣掉的累计血量。第一档从 0 开始，之后的阈值严格递增。每档的 `baseValue` 可以是小数，按档位保持或上升。最小档每颗消耗 1 时，第 1–30 颗为 0.5，第 31–60 颗为 1，第 61–100 颗为 2。高发射档位按当前发射公式提升价值，并按实际扣血推进阈值。同时发射的一批弹珠使用发射前的档位。回血、RUSH 免扣血和子珠生成均不会推进累计消耗。当日重试和换日会重置消耗进度。

血条标记各个阈值并预览下一颗的基础价值。越过阈值时会高亮并提示「升值」。双生和三重分裂钉的子珠复制母珠当前价值。经济钉将命中球的当前价值减半并产出一份残料。

### 背包与权限商店

| 每日字段 | 配置方式 |
| --- | --- |
| `unlockedPegIds` | 当天候选池。从 GameContent 选用具备放置体积的类型，成套装置按套安装计价。 |
| `defaultPegIds` | 当日免费权限，填写候选池中的 ID。Day 1 填全部五种供试用。 |
| `requiredPegIds` | 从免费权限选出开局预填且必带的类型。 |
| `minLoadoutTypeCount` / `maxLoadoutTypeCount` | 确认时最少携带的种类数和可携带上限，最少默认 1。允许配置最少为 0。 |
| `loadoutSelectionMode` | `Manual` 由玩家选择。`AutoSelectAvailable` 在必带类型之后，按候选池顺序预填可用类型至容量；玩家仍可编辑并确认。 |
| `initialBiomassScrap` / `clearCompanyScripReward` | 当天开局的血肉残料和达标工票奖励，均为非负整数。 |
| `shopEnabled` / `shopCurrencyType` | 开关及抽取货币。抽取货币固定为 `CompanyScrip`，关闭抽取的日期仍可打开装置选择。 |
| `shopOfferCount` / `shopDrawCost` / `shopSelectionLimit` | 每架最多货位、每次抽取工票价、当天最多抽取次数。 |
| `shopGuaranteedPegIds` / `shopRandomPool` | 先放保证出现的未授权类型，再从随机池补满。省略随机池时使用候选池中除免费类型外的装置。 |
| `shopRarityWeights` / `shopRandomSeed` | 稀有度权重按普通、罕见、稀有、传奇顺序填写，默认 60/30/7/3。种子为可选的抽样偏移，使用同一 Campaign 随机流，每个货位推进一次。 |
| `pegPermissionScope` | `CampaignPermanent` 跨日有效，`DayOnly` 仅当日，`RunOnly` 在成功结束本次生产时到期。同日重试沿用已付费的抽取记录和所获权限。 |
| `shopRefreshCost` | 可选付费刷新工票价。仍有货架外的未授权类型且可继续抽取时，允许刷新。默认 Day 2 已展示全部四个付费候选，采用固定当日货架。 |

装置位于 `Resources/Devices/<id>/`。终端和生产物品板显示 `metadata.json` 的 `biomassScrapPrice` 和 `durability`。旧包缺省安装价时回退到同稀有度的默认残料安装价。`device.json` 管理弹性和 Lua 参数，`manifest.json` 可引用同目录的 PNG 图标。新 ID 需加入 `GameContent.json.Devices`，再加入对应日期的候选池。

### Board Layout

在 Unity 打开 **Marbles ECS → Board Layout Editor**。选 `Pin`、`Collision Pin`、`Score Zone` 或 `Random Zone` 后在 2D 盘面上点击放置。`Collision Pin` 以浅蓝色显示，只参与物理碰撞和装置占位检查。用 `Select` 点击或拖动对象，在右侧面板修改属性或删除。`Board Surface` 可引用待使用的 `BoardPlacementSurface`。未选择时预览采用 TestScene 的默认尺寸。

`Save` 或 `Save As` 将 `.json` 存到 `Assets/Resources/BoardLayouts/`。在 `GameDays.json` 的日期条目填写例如 `"boardLayout": "Day0.json"`。子目录可用相对路径。重新进入 Play 时会加载新配置。省略 `boardLayout` 的日期使用场景自带的盘面。

`(0, 0)` 是左上角，`(1, 1)` 是右下角。`y` 沿斜面向下，`x` 是该行从左至右的宽度比例。`radius`、`width`、`depth` 和 `triggerHeight` 用 Unity 单位，触发区从盘面沿法线向上延伸。保存和每日启动时会检查对象的整个轮廓是否位于盘面内。

| JSON 条目 | 属性 |
| --- | --- |
| `pins[]` | `x`、`y`、`radius`。效果钉可配置 `scoreMultiplier` 和 `rushChanceAdd`。设 `collisionOnly: true` 创建纯碰撞钉，其效果字段会被忽略。RUSH 概率加值用 `0` 到 `1`，例如 `0.1` 对应 10 个百分点。 |
| `zones[]` | `type` 为 `Score` 或 `RandomScore`，另有 `x`、`y`、`width`、`depth`、`triggerHeight`、`multiplier`、`flatScore`、`priority`、`rushEnabled`、`scoreStackEnabled`。开启 Gambling Wheel 时，`RandomScore` 把分数存入转盘。关闭时直接结算。 |

每份布局的 `schemaVersion` 为 `1`，`pins` 和 `zones` 可为空数组。Day0 示例包含三枚钉子和底部一个普通得分区。配置布局会替换场景原有钉子与得分区，使用原有斜面、边墙和排球区。

### Morning Prep

每项 `morningPrep` 需要唯一 `objectiveId`、`type` 和 `anchorId`。`ReadNote` 额外需要显示的 `text`。在 TestScene 的 Morning Prep 对象上，`Surface` 指定物件摆放位置，`Interaction` 可指定玩家靠近的独立交互点。Day0 的 `desk` 绑定到弹珠机右侧平台。玩家在机台旁的终端按 E 选装置并确认。完成纸条后去机台正面按 E 进入 Build，拨动开关进入 Playing。两个生产阶段都可从物品板点击类型后落位，或直接拖到盘面。

## 增加 Event 和 Action

在 `events[]` 声明 `eventId`、`trigger` 和非空的有序 `actions`。可用 `priority` 提高执行顺序。事件策略为 `OncePerAttempt` 或 `EveryOccurrence`，省略时采用 `OncePerAttempt`。可选的 `condition` 对象用 `type` 和 `parameters` 引用已注册条件。Action 条目也需要 `type`，`parameters` 是由该类型定义和校验的可选字符串。

可用的 Trigger 是四个外层阶段各自的 `Entered` 和 `Exited`，以及 `BuildEntered`、`PlayingEntered`、`DrainingEntered`、`ShopEntered`、`RoundSettled`、`ProductionSucceeded`、`ProductionFailed`、`DayRetried`、`DayCompleted`。`ShopEntered` 在玩家于 Morning Prep 打开商店时触发。成功从生产进入当天结算时用 `DaySettlementEntered`。需要处理发射到排空这一回合的结果时，用 `RoundSettled`。完成休息的通知在当天作用域结束前派发。

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
