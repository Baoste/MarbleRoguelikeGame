# 装置 Lua API 设计规范

- 新增装置效果时，优先组合现有 Lua API；缺少能力时，在游戏引擎提供职责单一、可独立复用的基础 API。
- 装置提供的得分倍率用 `MultiplyScore(factor)` 记录。各次完整倍率直接相乘，除以 N 传入 `1 / N`。两次 1.5 得到 2.25，两次除以 2 得到 0.25。装置通用倍率、固定倍率钉和临时全盘倍率沿用乘除规则。碰撞时记录，到得分区统一计算。`TryMultiplyScore` 返回此次变化结果，`AddScoreMultiplier` 保留为兼容名。
- 多阶段效果由 Lua 组合：例如 `TryHold(seconds)` 只负责滞留，完成后通过 `OnHoldEnd(api)` 回调，再由 Lua 调用 `TryLaunch`、传送、属性修改等能力。
- 不把装置名称、ID、特定出射方向或下一阶段效果写进通用能力。失败后的玩法选择由 Lua 决定；引擎负责物理一致性、计时和生命周期清理。
- 延时回调必须创建新的有效上下文；不要跨回调保存并复用旧的 `api`、Motion 或出口对象。无回调、回调异常、装置移除、弹珠消失与回合结束时必须正确恢复或清理状态。
- 所有装置效果 API 统一维护在 `Docs/Device_Lua_API.md`。修改接口时同步更新文档、装置 Lua 和行为验证。
- 每个装置在 `Resources/Devices/<id>/` 存放 `manifest.json`、`metadata.json`、`device.json`、原生 Unity Prefab 和 Lua。Prefab 管理模型、材质和碰撞体的引用。
- `GameContent.json.Devices` 是游戏启用的装置 ID 列表。新增或移除装置时编辑此列表，保留属性和其他内容规则。
- 新装置 Metadata 包含 `name`、`description`、`value`（单件出售价格）、`purchasePrice`（一次购买价格，按普通/罕见/稀有/传奇分别为 9/14/23/38，默认 9）、`rarity`（`common` / `uncommon` / `rare` / `legendary`，默认 `common`）、`durability`（默认 20，`-1` 表示无限耐久）和 `type`。
- 耐久按每回合有效作用计数。普通即时效果通过 `Trigger` 或 `TryUse` 登记；持续力场每球每次进入扣 1，不逐帧扣。储存/充能类在释放效果时扣：黑洞点击启动一次扣 1，期间吞球不另扣；吸吸一批释放扣 1；大炮一批成功发射扣 1；黏黏每颗完成释放扣 1。储存、充能、取消、空点击不扣。延后扣除可组合 `TryPrepareTrigger` 与按实例/回合去重的 `TryUseOnce(key)`；最后一点耐久必须完成整批或完整持续时间，重试不重复扣。已经提前登记耐久的旧效果完成时不重复扣。同步维护 API 文档、装置 Lua、资源和行为验证和运行时买卖/耐久行为。

- 商店按稀有度档位权重 60/30/7/3 抽取，再在该档剩余装置中等概率选择；同次刷新不重复，空档归一化剩余权重。继续使用整局共享随机序列，每货位一次抽样。当前分档见 `Docs/Device_Overview.md`，新增装置未指定稀有度时为普通。
