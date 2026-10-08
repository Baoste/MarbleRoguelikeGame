# Balance Source Conflicts

## 判定原则

本报告以“当前可运行实现”为 Source of Truth。运行时优先级为：已启用的内容属性与阶段配置 > 场景中仍被读取的 Inspector 字段 > 兼容旧 API 的 `[NonSerialized]` 默认值 > 历史策划说明。`ASSUMPTION` 与 `UNKNOWN` 不会被模拟器偷偷替换为假数据。

| ID / 状态 | 文档或配置值 | 实际代码路径 | 运行时最终值 | 建议采用值 / 处理 |
| --- | --- | --- | --- | --- |
| C-01 / DOCUMENTED | `TestScene` 的控制器仍序列化 `FireIntervalSeconds=0.15`、`TargetScore=1000`；`GameBalance.Marble.TargetScore=350` | 场景 `EnableCampaign=1`；启动时基础表覆盖发射间隔，`MarbleSimulation.Campaign` 再用当前 Stage 覆盖目标 | 发射间隔 `0.30s`；首阶段目标 `120`；初始血量均为 `100` | 采用 `GameBalance.xlsx` 的 Marble/Stages。Inspector 与 `Marble.TargetScore` 仅作为非战役兼容值，暂不删除，以免破坏旧场景/API |
| C-02 / DOCUMENTED | `GameBalanceDefaults` 有 `BaseScore=10`、`TargetScore=350`；`CampaignTuning` 有提现 `0.5`、赌博 `50% ×3` | 内容模式从 `GameContent.json` 读取属性；四档赌博由 `CampaignSettlementSystem` 按权重抽取 | 球基础价值 `1`；提现 `blood × 0.65 × stageRate × 0.60`；赌博 `30%/30%/25%/15%` 对应 `×0/×1/×2/×4` | 采用内容模式值。兼容字段仅用于无内容目录的纯 C# 旧测试，不作为正式 Balance Data |
| C-03 / OPEN—DESIGN CONFIRMATION | 规范工作簿流程 F09 写 `floor(k × sqrt(剩余血量) × CASHOUT_K_MOD)` | `MarbleSimulation.Campaign` 使用线性 `BLOOD_DIVIDEND_RATE × CashoutRate × COIN_GAIN_RATE`；`Assets/Docs/Balance.md` 也描述线性公式 | Recommended 为 `floor(剩余血量 × 0.65 × 阶段倍率 × 0.60)` | 本版采用已实现、已模拟的线性公式。若要改平方根，必须作为 Gameplay Rule 变更重新建模，不能只改表格文字 |
| C-04 / DOCUMENTED | `Assets/Docs` 同时有“规范修订版”和名称较新的“四档赌博更新版”两个工作簿 | `GameContentImporter.Workbook` 硬编码读取“规范修订版”；保存工作簿不会自动导入，只有显式菜单导入 | 当前 JSON 与“规范修订版”34 个属性定义一致；另一份不会参与运行 | 继续以“规范修订版”为唯一导入源；后续应给旧工作簿加 `ARCHIVED_` 前缀或移入历史目录，但本次不移动用户资产 |
| C-05 / RESOLVED | 规范工作簿曾保留原型值，例如 `BALL_BASE_VALUE=10`、`RUSH_DURATION=10`，且缺少显式四倍概率行 | 运行时 JSON 原为基础价值 `1`、Rush 时长 `5`，并从 JSON 读取四倍概率 | 工作簿已同步全部 34 个属性，并新增 `GAMBLE_QUADRUPLE_CHANCE`；审计差异为 `0` | 采用已同步后的工作簿/JSON；任何显式导入前继续运行一致性审计 |
| C-06 / RESOLVED | `Assets/Docs/README.md` 曾写赌博 `50% ×3`；旧 Rush 专项报告使用 ×3 和旧阶段表 | 当前结算已是四档赌博；Recommended 使用 Rush ×2.5 与新阶段表 | README、上手指引已更新；旧专项报告已标为历史材料 | 当前值以 `Docs/Balance/` 和运行数据为准；历史报告只用于追溯，不可直接抄值 |
| C-07 / UNKNOWN | 规范工作簿流程出现“生成特殊球”，任务目标也要求 Special Ball Economy | 项目没有 `SpecialBall` 类型、目录、库存或持久化购买路径；药物是发射快照修饰，不是特殊球库存 | Special Ball 的数量、Pick Rate、EV、组合均为 `N/A` | 不创建虚构目录或概率。未来实现 Special Ball 后，先补数据定义与物理变体采样，再启用这些 KPI |
| C-08 / OPEN—PRODUCT RULE | 规范工作簿 F06 写“银行收益是否失败局保留？需确认” | 银行金币直接写入 Campaign Coins；失败没有单独回滚银行收入的规则 | 模拟按当前实现：银行收入即时进入局内金币；Run 失败后本局终止，未建跨 Run 元经济 | 需要产品确认失败局是否存在局外保留。当前没有 Meta Economy，因此不把它假设为永久收益 |
| C-09 / RESOLVED—DOCUMENTED | 旧规范工作簿没有 Device/Drug 的运行时 `Price` 列 | `CampaignShopSystem` 从 `GameContent.json` 的 `Devices[].Price` / `Drugs[].Price` 读取商品价；刷新价来自 `GameBalance.xlsx` | 规范工作簿已新增只读策划页“运行时平衡表”，列出全部 27 项价格、效果、时长、叠加、证据等级和改动；Importer 所有权仍不变 | 程序配置继续以 `GameContent.json` 为 Source of Truth；策划可在“运行时平衡表”审阅，不把该页误认为自动导入源 |
| C-10 / MODEL GAP | 真人反馈指出 V4 第二回合明显难过 | V4/V5 Monte Carlo 的三种策略 Stage 2 Clear Rate 均接近或等于 100%，模型没有完整表示瞄准误差、操作熟练度与真人路线选择 | V5 仍按真人反馈将 Stage 2 从 `430` 降至 `380`；该反馈优先于未校准的自动策略 | 后续采集真实 Stage 2 失败率、发射方向与每球得分分布，并增加 Player Skill / Aim Noise 校准层；当前结果标记为模型限制，不把 100% 模拟通过率解释为真人难度 |
| C-11 / RESOLVED | 四叶草概率同时存在 `DeviceDefinition.Value=0.75` 与结算代码常量 `.75`；修改数据不会真正改变运行时 | `DeviceEffectSystem` 只写入布尔标记，`ContentSettlementSystem` 用硬编码 `.75` | 已改为球上保存 `CloverChance`，结算读取装置 `Value`；Recommended 正式值为 `0.65` | 以后只改 `GameContent.json` / 规范工作簿的 Value，不再复制概率常量到 Gameplay Code |
| C-12 / RESOLVED—SAFETY RULE | “无限增值”描述允许复制体继续复制，没有代数上限；价格和时长都无法消除 `2^n` 增长 | `Replicate` 原样复制 `BallFeatures.Replicates`，唯一刹车是 `MaxActiveMarbles=256` | 复制使用独立 `ReplicationGeneration`，复用 `MaxSplitGeneration=2` 上限；单颗原球最多扩展到 `2^2=4` 球 | 采用 2 代上限。若未来需要独立代数参数，再新增显式配置字段并重做物理性能采样，不能恢复无界递归 |

## 已确认但不是“文档冲突”的问题

- 真实 PhysX 采样中 Right 三档均为 `100% Void`，而 Left 低档旧终点倍率 EV 为 `5.27`。这是盘面几何/发射带的可玩性问题，不是配置来源冲突。
- 装置可自由摆放，因此不存在唯一“装置触发概率”。模拟器使用每种尺寸在真实轨迹上的标准化最佳观察网格；所有由此得出的装置 EV 都带有该假设。
- Unity PhysX 不保证跨平台位级确定；保存 Seed、Unity 版本与物理依赖 Hash 是当前可达到的复现边界。
