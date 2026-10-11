# 装置 Lua API 设计规范

- 新增装置效果时，优先组合现有 Lua API；缺少能力时，在游戏引擎提供职责单一、可独立复用的基础 API。
- 装置提供的得分倍率统一用 `AddScoreMultiplier(amount)`，传入完整倍率，但引擎只累加 `amount - 1`；最终装置倍率为 `1 + Σ(amount - 1)`，两次 1.5 得到 2，基础 1 只算一次。撞击时只记录，到得分区统一计算。旧 `MultiplyScore` 名称仅兼容，语义相同；装置通用倍率修正与旧倍率钉共用规则。
- 多阶段效果由 Lua 组合：例如 `TryHold(seconds)` 只负责滞留，完成后通过 `OnHoldEnd(api)` 回调，再由 Lua 调用 `TryLaunch`、传送、属性修改等能力。
- 不把装置名称、ID、特定出射方向或下一阶段效果写进通用能力。失败后的玩法选择由 Lua 决定；引擎负责物理一致性、计时和生命周期清理。
- 延时回调必须创建新的有效上下文；不要跨回调保存并复用旧的 `api`、Motion 或出口对象。无回调、回调异常、装置移除、弹珠消失与回合结束时必须正确恢复或清理状态。
- 所有装置效果 API 统一维护在 `Docs/Device_Lua_API.md`。修改接口时同步更新文档、制作端 Lua、受影响的 `.mres` 和行为验证。
- 制作源资源放在 `MResPacker`；游戏运行时成品放在 `Resources/Devices`。
- `GameContent.json.Devices` 必须由当前 `Resources/Devices` 下全部 MRes 完整同步；不得合并旧 JSON/Excel 装置列表。同步时保留属性、药物和其他内容规则；新增或删除装置跟随实际包文件。
- 新装置 Metadata 包含 `name`、`description`、`value`（单件出售价格）、`purchasePrice`（一次购买价格，默认 5）、`rarity`（`common` / `rare` / `epic` / `legendary`，默认 `common`）、`durability`（默认 20，`-1` 表示无限耐久）和 `type`。
- 耐久按每回合有效作用计数。普通即时效果通过 `Trigger` 或 `TryUse` 登记；持续力场每球每次进入扣 1，不逐帧扣。储存/充能类在释放效果时扣：黑洞点击启动一次扣 1，期间吞球不另扣；吸吸一批释放扣 1；大炮一批成功发射扣 1；黏黏每颗完成释放扣 1。储存、充能、取消、空点击不扣。延后扣除可组合 `TryPrepareTrigger` 与按实例/回合去重的 `TryUseOnce(key)`；最后一点耐久必须完成整批或完整持续时间，重试不重复扣。已经提前登记耐久的旧效果完成时不重复扣。同步维护 API 文档、制作 Lua、MRes、行为验证和运行时买卖/耐久行为。
