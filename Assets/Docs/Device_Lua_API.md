# 装置 Lua API

装置效果写在 `Resources/Devices/*.mres` 包内的 Lua 中。引擎向 `OnContact(a)`、`OnHoldEnd(a)`、`OnBallUpdate(a)` 传入各自独立的 `DeviceContactContext`：C# 提供弹珠运动、属性、触发和计分等通用能力；Lua 选择调用哪些能力、调用顺序及数值。新增效果不需要在 C# 按装置 ID 或 `Kind` 增加分支。

没有当前弹珠的装置事件 `OnTick(a)`、`OnActivate(a)` 以及移动视觉对象的完成回调使用 `DeviceEventContext`；连续力 `OnField(a)` 使用 `DeviceFieldContext`。不同上下文提供各自阶段的能力，不能互换或跨回调保存。

布置阶段向已放置装置提交背包物品时，`OnOffer(a)` 使用独立的 `DeviceOfferContext`，提供库存物品信息、暂存的回合/实例数值、耐久设置和接受操作，不提供弹珠运动或计分接口。

## 赌博区结算说明

赌博区已改为全盘共享转盘奖池：入区时按当前装置累计倍率、区域倍率和全局得分叠加计算入池值；每球刷新 `max(0.2, 2 / sqrt(本轮球数))` 秒，超时或回合结束开奖，倍率为 `−2 / −1 / 0 / 1 / 5 / 10 / 20 / 50`（等概率）。负结果仅扣本回合分，最低 0。RUSH 贡献仍在入区时结算。旧 `GAMBLE_*` 和 `BALL_GAMBLE_VALUE_MOD` 属性仅保留加载兼容，不再参与玩法；通用 `AddScoreMultiplier` 语义不变，现有装置不需要修改 Lua 或重打包。

## 药物迁移状态

2026-10-22：旧药物系统已移除，后续将用 Lua 重新实现。当前没有药物商品、初始库存、服药入口或药效计时；本次未接入新的 Lua 药物系统。

旧药物的 C# 配方、库存、服药接口及 JSON 列表均已删除。装置 API 继续保留，后续药物也应组合职责单一的基础能力，不恢复旧的按药物种类分支。

## RUSH 累积机制（2026-10-14 重构）

全盘共用一条 0～100 的 RUSH 条，普通球的积累贡献从 **2** 开始。每次成功触发装置加 **5**，同一装置再次有效触发也计数；同一步重复通知和同一次回调调用多个效果 API 只登记一次。触发概率不再决定是否进入 RUSH，未激活时条值不衰减。

- 流失到底部：条值增加该球贡献；普通/赌博得分区：增加 `贡献 ×1.5`。同球终结只结算一次，关闭 RUSH 的得分区不增加贡献。超时、丢失物理体、回合清理、被装置吞掉以及 `TrySettleAmount` 不冒充底部/得分区。
- 达到 100 后清空条值并进入 **2 秒 RUSH**，溢出不保留。RUSH 期间不向条值入账、不刷新时长；结束后从 0 重新积累。贡献仍保存在存活球上，之后正常结算。
- 期间发射不扣血，0 血也能发射，回血照常。免费发射不会制造家族返血。因缺血进入排空的回合，若最后的球累满条，可恢复发射；主动提现不恢复发射。
- 发射器射速增加 **每秒 5 发**：`新间隔 = 1 / (1 / 原间隔 + 5)`，不改变球的飞行速度。固定物理步仍是实际发射频率的上限。
- 所有现有球和新球临时价值 **+5**，结束后撤销。基础价值、分球守恒和永久属性保持独立，避免复制或分裂把临时加成保存为永久价值。得分区、黑洞直接计分和赚赚兑换使用当前有效价值。
- 本次**不实现装置增益 ×2**。装置附加倍率、连击、金币、回血、耐久、冷却等现有数值保持原规则；有锅气按下文专门调整。旧 RUSH 全局得分倍率不再自动乘入。
- 降降火的额外 5 秒保留：只有带效果的球将条值累满时，该次时长为 7 秒。持续时间按模拟秒倒计时，暂停不流逝，不再读取旧 `RUSH_DECAY`。

引擎入口为 `Scripts/System/RushSystem.cs`。普通球接触、分球成功、持续力场首次进入分别登记；同一次滞留完成不重复登记，力场每次进入加一次，不逐帧累加。回合结束和重开会话清空 RUSH。

| Lua API | 契约 |
| --- | --- |
| `RushContribution` | 当前球的普通贡献，只读，默认 2。 |
| `AddRushContribution(amount)` | 增加当前球贡献，有限范围 `[0,1000000]`，总值上限 1000000；只记录，不立即增加全盘条值，不扣耐久。非零修改登记一次有效装置作用。 |
| `SetScoreRushContribution(amount)` | 为当前球设置得分区的固定入账值，有限范围 `[-1000000,1000000]`；替代普通 `贡献×1.5`，不会再乘 1.5。仅得分区使用，底部流失仍使用普通贡献。 |
| `ClearScoreRushContribution()` | 清除固定入账覆盖，恢复普通结算。 |
| `RushDurationBonus` | 球把条值累满时额外增加的秒数，读写，默认 0，有限范围 `[0,3600]`。不启动、不刷新正在进行的 RUSH。 |
| `RushActive` / `Board.RushActive` | 当前是否在 RUSH 中，只读。 |
| `Board.RushMeter` / `Board.RushMaximum` | 全盘当前积累值和上限 100，只读。 |
| `Value` / `Get("BALL_BASE_VALUE")` | 当前基础价值，包含其属性修正，不包含临时 RUSH +5；供永久增减和分球使用。 |
| `EffectiveValue` | 基础价值加当前 RUSH 临时增量，供即时结算/兑换使用。 |
| `SetAppearance(label, r, g, b)` | 设置当前球的文字与颜色。文字 UTF-8 最多 60 字节，RGB 为有限 `[0,1]`；不改变价值或贡献，不扣耐久。物理桥即时同步，物体池再用时清理。 |

上述球状态随复制、等分和按价值分球完整继承；临时 +5 由当前全盘状态实时计算，**不被复制或均分进永久价值**。API 只在所属回调有效；非法参数不部分写入。`MarbleStateSnapshot` 提供 `RushContribution`、`HasScoreRushContribution`、`ScoreRushContribution`、`EffectiveValue`；`MarbleSnapshot` 提供条值、上限、剩余秒数和当前每秒发射数。

旧 `AddRushChance`、`RushOnSettlement`、`Board.SetRushChanceBonus` 保留兼容读取，但不参与新的 RUSH 触发。区域 `BaseRushChance` 及对应运行时数据已移除。旧 `RUSH_DURATION/RUSH_MULT/RUSH_DECAY/BALL_RUSH_GAIN` 属性也不再控制条值触发和核心计时；后续装置请使用本节 API，不把概率换算成贡献。

得分区另有全盘共享的限时叠加：开启 `MarbleZone.ScoreStackEnabled` 的区域每进一球，先增加 0.05 区域倍率，再结算并刷新倒计时；首球 1 秒，后续为 `max(0.1, 1 / (1 + 进球前全局加成))` 秒。它直接加在区域原倍率上，不写入 `AddScoreMultiplier` 的装置累计值，不随复制/分裂传递。Lua 的直接金额结算不会自动获得或刷新该区域加成；API 签名及包内脚本无需修改。关闭区域开关时不累加、不刷新，也不享受加成。完整公式与调参见 `Docs/Balance.md`。

## 超大杯全冰美式（已实现）

业务 ID `9032`，源脚本 `MResPacker/Authoring/Lua/IcedAmericano.lua`，运行时 `Resources/Devices/9032_IcedAmericano.mres`。模型为大杯咖啡、六块冰和吸管，使用实体碰撞体，弹珠正常反弹。

每次有效碰撞将当前基础价值乘 **0.5**，保留小数，同时增加 **20 点额外 RUSH 贡献**。通用装置触发仍加 5：新球贡献从 2 变为 27，第二次碰撞后变为 52。此时不直接增加 RUSH 条；到底部按贡献入账，到得分区按贡献 ×1.5 入账。价值为 0 也可触发；同一步重复碰撞通知只处理一次，之后再次撞击可继续生效。

Lua 组合已有 `Visit(0, 0)`、`TryUse()`、`SetCurrent("BALL_BASE_VALUE", api.Value * api.Parameter)` 和 `AddRushContribution(api.Strength)`，无需新增引擎接口。`SetCurrent` 转换包含属性修正后的基础价值，不重复应用旧价值修正；累计得分倍率、物理运动、其他属性保持原状。RUSH 期间临时 +5 独立计算，例如基础价值 10 在碰杯后变为 5，当时有效价值为 10，RUSH 结束后为 5。

`game/device.json` 的 `Value=0.5` 为保留价值比例，`Strength=20` 为额外贡献。每次成功处理扣 1 耐久，最后一次完整生效，耗尽后本回合停用。默认普通、耐久 20（支持 -1）、购买价 5、出售价 120、初始库存 0。

2026-10-14 验证：冰美式专项 **357** 条断言、完整玩法回归 **10341** 条通过，元数据/耐久 **291** 条，全部 **35** 个 MRes 加载通过。覆盖小数与零价值、重复/同时碰撞、最终和无限耐久、回合恢复、已有属性修正、RUSH 临时价值、底部/得分区入账，以及两种固定步下的真实倾斜棋盘碰撞。

## 台球（已实现）

业务 ID `9031`，源脚本 `MResPacker/Authoring/Lua/Billiards.lua`，运行时 `Resources/Devices/9031_Billiards.mres`。制作源包含绿色台面、框架、洞口与装饰球的模型、Prefab、材质、配置和 `BilliardsAuthoring.unity`。

一次吞入一球，无碰撞隐藏 0.2 秒，然后向棋盘下方吐出一颗编号球，速度至少 6（保留更高的入口速度），基础价值、尺寸、质量和原有效果保留。8 号概率 20%，1～7 号均分剩余 80%，编号和颜色在球上显示。进入得分区时，黑八使全盘条值减 100、最低到 0，其他编号使条值增加 `编号×10`；这替代普通贡献入账，不另外乘 1.5。黑八不结束已激活的 RUSH。

吐出成功才扣 1 耐久并开始 15 秒冷却；吞入中和冷却时其他球直接经过，受阻则继续短暂滞留并重试，重试不重复抽号或扣耐久。每回合重置冷却。`game/device.json`：`Value=0.2` 为滞留秒数，`Strength=6` 为最低吐球速度，`Range=15` 为冷却。普通、购买 5、出售 120、耐久 20（支持 -1）、初始库存 0。

2026-10-14 重构验证：完整玩法回归 **9978** 条断言通过，RUSH 专项 **63** 条、台球专项 **1649** 条、有锅气 **497** 条、降降火 **337** 条；元数据/耐久 **286** 条，全部 **34** 个 MRes 加载通过。覆盖真实台球吞吐、文字/颜色与物体池清理、出口受阻重试、无限/最后耐久，以及两种固定步下的物理行为。

## API 设计标准（后续装置统一遵循）

- 基础 API 职责单一、可以独立使用。例如滞留 `TryHold`、出射 `TryLaunch`、仅修改位置 `TrySetPosition`、属性修改 `Set` 分别提供；具体效果由 Lua 组合。
- 多阶段效果通过事件衔接。滞留结束后 Lua 可以选择出射、传送、分裂、修改属性、继续滞留或不做额外操作；通用滞留系统不保存出射方向或目的地。
- 引擎负责物理/ECS 同步、计时、上下文有效期和生命周期清理；触发条件、方向、时长、数值、受阻后的替代效果由 Lua 决定。
- 延时事件总是提供新的有效 `api`。跨阶段只保存普通数据，不复用旧回调的 API、Motion 或出口对象。
- 没有结束回调时基础能力仍能独立完成；回调异常需释放上下文，并保证其他弹珠继续处理。新增能力同步维护本文件和对应行为验证。

## Metadata、价格与每回合耐久

制作配置和所有现有 `.mres` 的 `metadata/metadata.json` 使用七个字段。此节补充原始 Packer / Loader 工作流中的四字段 Schema；新增业务效果参数仍写在 `game/device.json`。

```json
{
  "name": "黏黏",
  "description": "弹珠滞留 1 秒后向下释放。",
  "value": 120,
  "purchasePrice": 5,
  "rarity": "common",
  "durability": 20,
  "type": "device"
}
```

| 字段 | 规则 |
| --- | --- |
| `value` | 玩家出售**单个库存实例**获得的金币；沿用各包原有数值。装置必须为非负整数，不再按购买价和旧 `SellRatio` 折算。 |
| `purchasePrice` | 商店一次购买的基础价格，所有现有装置及新配置默认 `5`，必须为正整数；已有商店价格修正继续生效。配对装置一次获得两件也只扣一次购买价。 |
| `rarity` | `common` 普通、`rare` 稀有、`epic` 史诗、`legendary` 传奇；固定小写字符串。现有装置和新配置默认 `common`。 |
| `durability` | 每回合有效作用次数，默认 `20`；**`-1` 表示无限耐久**。其他合法值为整数 `1～10000`，`0` 和其他负数无效。 |

旧四字段包缺少新字段时，Loader 分别补 `5`、`common`、`20`。Packer 新包显式写出七字段并独立校验。Lua 可读取 `metadata.purchasePrice`、`metadata.rarity`、`metadata.durability`，也可用 `self:GetPurchasePrice()`、`self:GetRarity()`、`self:GetDurability()`；这些是配置值，当前剩余耐久读取回调的 `RemainingDurability`。Metadata 的小写 `value` 是出售价格，`api.Parameter` 对应附加业务文件的大写 `Value`，`api.Value` 是当前弹珠价值。

耐久按库存实例、回合独立计算。储存/充能类在**释放效果时**扣 1：黑洞启动一次引力、吸吸释放一批、大炮成功发射一批、黏黏释放一颗，各算一次；接纳/储存/充能不扣。最后一次仍完成整批或完整持续时间，然后本回合停用。普通即时效果维持原触发计数，持续力场每球每次进入扣 1。新回合恢复满耐久，`-1` 表示无限，不扣减。

耗尽后停止新的效果。定时滞留的完成回调仍可执行；手动存球保留点击与计时事件以完成已接受的批次。持续效果可显式申请有期限的完成窗口，在窗口内继续接触、施力和更新；到期自动关闭玩法碰撞体与更新。窗口不增加耐久，新批次仍不能登记。来源收回/删除、回合或会话结束均不延续效果；暂停不推进模拟计时。

界面在耐久归零但仍有完成窗口或手动存球时显示“当前效果待完成”；结束后显示“本回合已失效”。

| 使用方式 | 计数契约 |
| --- | --- |
| `Trigger(key)` | 通过触发率、冷却和访问检查后扣 1；同一回调后续 `TryUse`、`Consume`、`TrySettle` 共用这次作用，不重复扣除。失败的触发检查不扣。 |
| 接触/逐球/装置事件 `TryUse()` | 登记一次作用，成功返回 `true`。同一回调重复调用不重复扣，耗尽返回 `false`；不增加访问次数、冷却或机器倍率。黑洞在接受激活的 `OnActivate` 调用。 |
| `TryPrepareTrigger(key)` | 接触/逐球回调：执行与 `Trigger` 相同的触发率、冷却、访问及机器倍率/复制修正，但不扣耐久。储存类在后续释放时显式登记耐久。耗尽或 `OnHoldEnd` 中返回 `false`。 |
| `CanUseOnce(key)` | 接触/逐球/结束/装置事件：查询此批次是否已登记，或是否还有新作用的耐久；不扣除。 |
| `TryUseOnce(key)` | 上述上下文：同一装置、同一回合、同一字符串只扣一次；后续回调即使剩余为 0 也返回 `true`，让同批继续完成。不同 key 是新的作用。`key` 非空且 UTF-8 不超过 120 字节，非法返回 `false`。 |
| `TryKeepActive(seconds)` | 上述上下文：保留来源装置的接触、持续力与更新至 `Time + seconds`，不扣耐久、不增加可用次数。只接受有限数值 `(0,60]`；再次调用替换截止时间。用来完成刚扣掉最后一点耐久的持续效果。 |
| `TryConsume(refund)` / `TrySettleAmount(score)` | 弹珠回调：分别只负责吞噬、按明确金额结算，不隐式扣耐久。重复处理、非法分数返回 `false`。黑洞充能和有效期吞球使用这两个基础能力。 |
| 兼容 `Consume(refund)` / `TrySettle(score)` | 保留旧包的自动登记耐久行为；新脚本优先组合上面两个基础能力和显式耐久登记。 |
| 力场回调 `TryUse()` | Lua 先检查区域，引擎按弹珠与装置记录连续进入状态，停留期间不重复扣，离开后重入扣 1。黑洞已按激活登记，全局引力不再调用此接口。 |
| `OnHoldEnd` 中 `TryUse()` | 兼容已提前扣除的旧效果，直接返回 `true` 而不扣除。**延后计数的释放必须用 `TryUseOnce(key)`。** |

运动、向量、属性 API 不隐式扣耐久；`Visit` 只记录访问。批量释放先 `CanUseOnce(key)`，成功执行运动后再 `TryUseOnce(key)`；Lua 为同一批的所有成员保存相同普通字符串，为新批分配新字符串。不要再混用 `Trigger` 或 `TryUse` 额外扣同一次效果。引擎只负责去重、计数和完成窗口，批次边界、方向与失败后的选择由 Lua 决定。已成功的操作与触发修正不回滚。

## 传送门：成对购买，保持世界运动方向和速度

核心操作是 `api.Motion:TrySetPosition(position)`。Lua 查找配对门、计算出口外侧位置，再调用只修改位置的通用接口。弹珠保留传送前的**完整世界速度向量**，不随出口旋转；零速和很低的速度不会被抬高。世界旋转、角速度、质量、重力倍率等自身物理属性保持，仍遵守当前 `BALL_MAX_SPEED` 限速。

制作源为 `MResPacker/Authoring/Lua/Portal.lua`，输出 `MResPacker/Output/Portal.mres`，游戏使用更新后的 `Resources/Devices/103_Portal.mres`，沿用现有 ID 103。包内效果如下：

```lua
function OnContact(api)
    if api.PairId == 0 or api.TransportLockedUntil > api.Time then return end
    local motion = api.Motion
    local exit = motion:FindPartner()
    if exit == nil then return end

    local offsetDirection = api.Vectors:Normalize(api.Vectors:ProjectOnPlane(motion.Velocity, exit.Up))
    if offsetDirection.sqrMagnitude < 0.000000000001 then offsetDirection = exit.Forward end
    local position = exit.Position + exit.Up * (api.Radius + 0.005)
        + offsetDirection * (exit.Radius + api.Radius + 0.02)
    if not api:Trigger(-api.PairId) then return end
    if motion:TrySetPosition(position) then
        api.TransportLockedUntil = api.Time + 0.5
    end
end
```

**购买一次获得两个已配对的独立装置**，配置 `PurchaseQuantity = 2`，基础购买价 5 为整对价格，只扣一次；`metadata.value = 15` 为出售单扇门的价格。通用库存赋予两扇门相同的非零 `PairId`；另一次购买生成新的配对 ID。两扇门可以分别放置，双方均可作为入口或出口，各自拥有独立耐久，作为入口触发时扣 1。容量只剩一个位置时整次购买失败，不扣钱、不产生半对；卖掉、收回或耗尽其中一扇后，另一扇找不到有效配对出口就不传送。

为选取出口位置，Lua 将当前速度投影到出口棋盘平面，沿投影方向放到门外，并留出弹珠半径和棋盘表面间隙。这只用于位置计算，不改变速度；投影长度小于 `1e-6` 时，使用出口朝向选择位置。出口被实体阻挡时 `TrySetPosition` 返回 `false`，位置、速度保持原状，也不设置传送锁。未放置、耐久耗尽或没有物理视图的配对门不会成为出口。`Trigger` 已成功登记的访问次数、公共冷却和通用修正不会回滚。

“加速度保持”的规则是：传送本身不施加额外力/冲量，不改变重力、质量等弹珠自身属性；下一物理子步的区域力按**出口环境重新计算**。从加速力场内传到场外后不会携带入口场力，传到另一力场内会受到出口场力。引擎不存储、冻结或永久复制入口的总加速度，正常重力、阻力及后续碰撞继续计算。

沿用现有配对访问规则：成功传送设置 0.5 秒锁，防止立即回传；装置 `Cooldown = 0.5`，每球对该配对的 `MaxTriggersPerBall = 2`。本版不使用或消费旧 `ExitDown` 标记，因此不会把保存方向的传送改成下坡出射。旧通用 `TryTeleport(exit)` 仍保持其“沿出口朝向出射”的 API 契约，其他包如需该行为可继续调用；当前传送门脚本使用 `TrySetPosition`。

## 运动接口 `a.Motion`

所有位置和向量均为 Unity **世界坐标**，速度单位为世界单位/秒，角度单位为度。通过实例调用方法使用冒号 `:`；读写属性使用点 `.`。

| API | 含义 |
| --- | --- |
| `Position` / `Velocity` / `Speed` | 当前弹珠的位置、线速度向量、线速度大小。成功操作后立刻能读到新值。 |
| `Source` | 当前触发装置的只读出口对象 `DeviceEndpoint`。 |
| `FindDevice(instanceId)` | 在当前游戏会话查找已放置且有物理视图的装置；不存在返回 `nil`。ID 是库存实例 ID。 |
| `FindPartner()` | 查找当前装置同 `PairId` 的另一个已放置出口；无配对返回 `nil`。超过两件时按实例 ID 取最小的有效对象。 |
| `TryTeleport(exit)` | 兼容既有 API：保持当前速度大小，沿 `exit.Forward` 从出口外侧出射，会改变方向。当前传送门使用下面的 `TrySetPosition`。 |
| `TryLaunch(exit, direction, speed)` | 指定世界方向和非负速度，从出口外侧出射。方向由引擎归一化。 |
| `TryMove(position, velocity)` | 指定世界位置和完整速度向量，执行带障碍检查的位移。 |
| `TrySetPosition(position)` | 指定世界位置并检查终点障碍，保留当前完整速度向量、旋转和角速度；内部复用 `TryMove(position, Velocity)`，同样限速、同步 ECS/PhysX。只负责位置，不查配对、不计算出口、不加冷却。 |
| `TrySetVelocity(velocity)` | 原地设置速度，不执行传送障碍检查，也不改变位置。 |
| `TryScaleSpeed(scale)` | 保持方向，将当前速度乘以非负倍率；`0` 使弹珠停止。 |
| `TryHold(seconds)` | 原地固定当前弹珠，经过指定模拟秒数后恢复进入时的运动，再调用 `OnHoldEnd(api)`。不自动出射。`seconds` 必须大于 0 且不超过 60；当前已滞留时返回 `false`。 |
| `TryHoldUntilReleased()` | 原地固定当前弹珠，等待来源装置调用 `ReleaseHeld()`；不设自动结束时间，不包含容量、移动到中心或出射逻辑。已滞留时返回 `false`。弹珠寿命、来源移除与回合清理仍生效。 |
| `TrySetGravityScale(scale, seconds, resetOnOtherDevice)` | 设置独立的临时重力倍率：`-1` 反转、`0` 无重力、`0.5` 半重力；不改变位置或速度。倍率范围 `[-10,10]`，时长 `(0,60]` 模拟秒。最后一个参数决定接触其他装置实例时是否提前移除。重复调用替换倍率、来源及截止时间，不乘叠。 |
| `TryClearGravityScale()` | 提前移除临时重力倍率，立即恢复使用弹珠当前的重力属性；没有临时效果时返回 `false`。不修改基础属性及其他修正。 |

所有 `Try*` 返回布尔值。非法数值（NaN/无穷）、负速率、零出射方向、无效出口、找不到刚体，会返回 `false`。即时位移的终点被阻挡也返回 `false`；位移只检查终点，不要求沿途无障碍。速度操作统一执行全局限速，成功后同步 PhysX 和 ECS，保留角速度；成功操作后同一逻辑帧的后续弹珠交互会跳过，避免使用旧接触位置重复触发。延时操作的返回值及异常处理见下一节。

### 大炮：手动滞留、旋转与批量发射

制作源为 `MResPacker/Authoring/Lua/Cannon.lua`，成品为 `MResPacker/Output/Cannon.mres`，游戏包为 `Resources/Devices/9007_Cannon.mres`。圆柱底座配朝向本地 -Z 的炮管，炮口方向与物理方向一致。

新增的通用能力彼此独立：

| API | 上下文与含义 |
| --- | --- |
| `HeldCount` | `DeviceContactContext` / `DeviceEventContext`：来源为当前装置且仍存活的滞留弹珠数量，由 ECS 实时查询。消失、到寿命的弹珠不会占用名额。 |
| `Motion:TryHoldUntilReleased()` | 弹珠回调：只负责原地暂停运动和碰撞。Lua 可先独立调用 `TryMove` 选择存放位置。 |
| `ReleaseHeld()` | 装置事件：按生成顺序释放调用时属于自己的滞留集合，返回处理数量。支持提前结束定时滞留；每球先恢复原运动，再同步调用独立、全新的 `OnHoldEnd`。在该回调中重新登记的滞留不会被这次集合再次释放。 |
| `Forward` / `Up` | 装置事件：当前实际视图的世界朝向（本地 -Z）和棋盘法向，不需要有当前弹珠。 |
| `TrySetYaw(degrees)` | 装置事件：相对摆放姿态，绕棋盘法向设置绝对旋转偏移；只改变当前物理/表现视图，不累积旋转、不修改背包布局。拒绝 NaN/无穷或不存在的视图；八个角度、间隔与自动旋转策略均由 Lua 决定。 |
| `Hold.IsManual` | `OnHoldEnd`：此次完成的是否为手动滞留；手动滞留 `Hold.Duration = 0`，`Elapsed` 为已等待的模拟秒数。 |

大炮 Lua 在 `OnContact` 中检查 `HeldCount < Parameter` 与剩余耐久，仅用 `Visit(0, 1)` 记录新球，随后 `TryMove` 到炮内，再 `TryHoldUntilReleased()`，存球不扣耐久。满仓时直接放行；同一模拟步可依次存入多颗，不受公共冷却限制。同一球发射后不再被同一炮收回。

`OnTick` 按 `floor(Time / Range) % 8 × 45°` 设置旋转，默认每 0.5 秒转 45°，暂停不转。点击 `OnActivate` 时保存当前可见的 `Forward` 向量，然后调用 `ReleaseHeld()`。逐球 `OnHoldEnd` 组合 `TryMove(position, direction * Strength)`，将弹珠沿锁定方向排在炮口外，间距包含双方半径及 0.02 余量；默认速度 6，仍受每球当前速度上限限制，保留角速度。发射期间不按新的旋转角度改方向。

出口被实体阻挡时，Lua 重新调用手动滞留保留该球，等待下次点击，不重新扣耐久。引擎不包含大炮名称、容量、八向角度或批量位置计算。容量 `Value = 5`、旋转间隔 `Range = 0.5`、出射速度 `Strength = 6` 位于 `game/device.json`。

手动滞留的生命周期与定时滞留一致：来源收回、删除或视图消失时恢复进入速度，不调用完成玩法；弹珠消失、寿命到期、排空超时及新回合会清理存球。正常弹珠寿命继续计时，容量读取引擎现状，不依赖 Lua 保存的过期 ID。缺少结束回调或回调异常时，已经恢复的弹珠不会卡住，后续球继续处理。API 及 `Hold` 信息不能跨回调复用，只保存普通值。

每批第一次成功发射时 `TryUseOnce("volley:" .. batchNumber)` 扣 1，其余成员及受阻重试沿用这个 key，不再扣。空点击或全部被挡住不扣；等待重试时不接纳新球。最后一点耐久能发射完整五球；部分受阻时虽然碰撞体关闭，仍保留已有手动滞留的完成事件、状态及点击范围，直到完成或清理。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

### 反重力：上方出射与限时重力分别组合

制作源为 `MResPacker/Authoring/Lua/AntiGravity.lua`，成品为 `MResPacker/Output/AntiGravity.mres`，游戏包为 `Resources/Devices/9006_AntiGravity.mres`。金色圆柱触发器；`game/device.json.Value = 2` 控制持续秒数。

```lua
function OnContact(api)
    if not api:Trigger(0) then return end
    local motion = api.Motion
    local source = motion.Source
    if motion:TryLaunch(source, -source.Down, motion.Speed) then
        motion:TrySetGravityScale(-1, api.Parameter, true)
    end
end
```

`-source.Down` 是棋盘上坡方向，出射点在该方向的装置外侧；`source.Up` 是棋盘法向，不用于朝棋盘上方发射。保留进入时的速度大小（包括零速，受正常限速约束）和角速度。出口受阻时不位移、不授予新的反重力效果；已经接受的触发仍扣 1 耐久。

临时重力是弹珠状态，计算为 `Physics.gravity × 当前 BALL_GRAVITY_MOD × scale`，不修改该属性本身，不反转力场、磁力或其他外力。期间基础重力或其他属性修正改变时继续参与计算；恢复时移除临时倍率，不用进入时的旧属性覆盖现值。

- 按模拟时间独立计时，在累计满时长的固定步末尾恢复，误差小于一个固定步；最后一步仍施加临时重力，暂停不计时。滞留期间不施力，但该计时继续推进。
- `resetOnOtherDevice = true` 时，接触其他有效装置实例，在执行对方 Lua 前恢复；不依赖对方触发率、冷却是否通过，也不要求对方有 `OnContact`。进入其他装置的触发区域同样算接触。普通固定钉、墙、弹珠和来源装置自身不触发此规则；连接线穿越不是实体接触。
- 另一件反重力装置可以在取消旧效果后授予新的 2 秒效果。默认每球每个实例最多触发一次，避免重复进入同一出口刷新计时。
- 来源装置收回、移除或视图消失时，下个模拟步恢复；仅耐久耗尽不取消已接受的效果，第 20 次仍完整生效。弹珠消失、回合/会话重置随 ECS 实体销毁清理，对象池复用不继承状态。
- API 不扣耐久、不出射、不保存 Lua 上下文；Lua 用 `Trigger` 登记新作用。无效参数不会覆盖旧效果；回调结束后继续使用 API 会抛出上下文失效异常。

默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。验证入口包含在 `Marbles ECS/Validate Attribute and Content Runtime`。

### 黏黏：滞留后向下释放

制作源文件为 `MResPacker/Authoring/Lua/Sticky.lua`，成品为 `MResPacker/Output/Sticky.mres`，游戏资源为 `Resources/Devices/9001_Sticky.mres`。

```lua
function OnContact(api)
    if api.RemainingDurability >= 0 and api.HeldCount >= api.RemainingDurability then return end
    if not api:TryPrepareTrigger(0) then return end
    api.Motion:TryHold(api.Parameter)
end

function OnHoldEnd(api)
    local key = "release:" .. api.BallId
    if not api:CanUseOnce(key) then return end
    local motion = api.Motion
    local source = motion.Source
    local speed = api.Hold.EntrySpeed
    local released = motion:TryLaunch(source, source.Down, speed)
    if not released then released = motion:TrySetVelocity(source.Down * speed) end
    if released then api:TryUseOnce(key) end
end
```

`api.Parameter` 来自包内 `game/device.json` 的 `Value`，当前为 `1` 秒。`api.Hold.EntrySpeed` 是本次滞留开始时的速度大小，黏黏在结束回调中主动选择保留它（包括零速，仍受释放时的限速约束）。`source.Down` 是棋盘下坡方向，与装置自身朝向无关；水平棋盘投影为零时退回装置 `Forward`。

旧 `TryHoldThenLaunch` 已移除，黏黏的制作源脚本和成品已迁移到上面的独立能力组合；旧版本黏黏包需要重新打包。`TryHold` 没有按装置 ID 或名称分支：

- 返回 `true` 表示成功固定并开始计时，不登记出射计划。无需提供方向、速度或出口。
- 按模拟时间计时，在满时长后的第一个固定步末尾释放；暂停游戏不会推进等待。每颗弹珠独立计时，等待期间线速度和角速度为零，保持位置和旋转，暂不参与重力、磁力、碰撞或计分区域触发。
- 到时先恢复物理、进入时的线速度（受当前限速约束）及角速度，然后调用 `OnHoldEnd(api)`；没有该函数时，弹珠直接继续原来的运动，不自动位移。
- `OnHoldEnd` 获得全新的上下文，`Motion.Source` 使用结束时装置的实际位置和朝向。出射受阻时 `TryLaunch` 返回 `false`；黏黏的 Lua 选择用 `TrySetVelocity` 原地向下释放。其他装置可选择不同处理方式。
- 来源装置移除、收回或物理视图消失时，下个模拟步取消滞留，在原位置恢复进入时的运动，不调用结束回调。
- 弹珠寿命及回合排空计时正常推进；弹珠消失、回合重置、会话销毁会清除滞留状态，不补发回调。结束与寿命耗尽/排空超时同一步发生时，不调用回调。对象池复用不会继承滞留状态。
- 结束回调每次正常完成调用一次。回调抛错会记录 Lua 异常并关闭上下文，其他弹珠仍继续恢复和回调；回调前已经恢复运动，回调内已经成功执行的操作不回滚。
- 等待期间其他即时运动命令返回 `false`；重复登记不会覆盖原计划。普通 `TrySetVelocity(0)` / `TryScaleSpeed(0)` 仅瞬时置零，不能代替持续固定。
- 结束回调中可以重新调用 `TryHold`，新一轮以当前恢复后的速度为入口数据，从下一固定步开始等待。也可以调用 `Children`、`Consume` 或属性接口组合其他效果；消耗弹珠后应结束回调。

### 滞留时的显示 `api.Motion:TrySetHeldVisible(visible)`

只改变当前装置已经滞留的弹珠是否显示，不开始/结束滞留、不改位置、速度、时长或耐久。先成功调用 `TryHold` / `TryHoldUntilReleased`，再调用 `TrySetHeldVisible(false)`；`true` 恢复本次隐藏之前的显示状态，弹珠继续滞留。自由运动的球、其他装置滞留的球或不支持该能力的物理适配器返回 `false`。上下文过期后调用会抛出失效异常。

PhysX 适配器保存弹珠及子级各 Renderer 原有的 `forceRenderingOff`，重复隐藏不会覆盖快照。结束或取消滞留时，在 `OnHoldEnd` 之前恢复；弹珠消失、回收、回合/会话重置也恢复。原本已经隐藏的 Renderer 仍保持隐藏，不擅自启用禁用的组件。无结束函数、回调提前返回或抛错也不会因这次隐藏让弹珠永久不可见。该接口可独立用于运输、储存等效果；引擎不包含淘金热的概率或结果。

### 结束信息 `api.Hold`

`OnContact` / `OnBallUpdate` 中为 `nil`；`OnHoldEnd` 中是只读的 `DeviceHoldInfo`。以下数据仅在本次结束回调内有效，跨回调使用请保存普通数字或向量副本。

| 属性 | 含义 |
| --- | --- |
| `EntryVelocity` / `EntrySpeed` | 滞留开始时的世界线速度向量及大小，不受等待期间修改限速的影响；实际运动命令仍执行当前限速。 |
| `EntryAngularVelocity` | 滞留开始时的世界角速度向量。 |
| `Duration` | 调用 `TryHold` 时请求的秒数；手动滞留为 `0`。 |
| `IsManual` | 是否由 `TryHoldUntilReleased()` 登记。 |
| `Elapsed` | 实际经过的模拟秒数，受固定步量化影响，可能略大于请求时长。 |

结束回调沿用来源装置及原接触的 `LinkKey`。`Trigger` 在结束回调返回 `false`，避免把完成事件当成第二次碰撞消耗冷却/次数或重复应用触发修正。

黏黏每颗成功向下释放扣 1，滞留、取消和等待中消失不扣。有限耐久时不接纳超过剩余次数的待释放球，`-1` 不限制此数量。默认 `MaxTriggersPerBall = 1`，同球对同实例只滞留一次；`Cooldown = 0` 仍遵守 `TryPrepareTrigger` 的通用同逻辑帧触发限制。购买价 5、出售价格 120、初始库存 0。

出射位置由引擎计算，不需要 Lua 操作 Transform：

```text
exit.Position
  + exit.Up * (ballRadius + 0.005)
  + normalizedDirection * (exit.Radius + ballRadius + 0.02)
```

### 出口对象 `DeviceEndpoint`

| API | 含义 |
| --- | --- |
| `InstanceId` / `Position` / `Radius` | 装置实例 ID、世界中心、配置半径。 |
| `Forward` | 世界出射方向，来自装置旋转后的**本地 -Z**，与现有装置外观/摆放朝向一致。不是 Unity 默认的本地 +Z。 |
| `Up` | 旋转后的本地 +Y，即装置所在平面的法向。 |
| `Down` | 世界重力向下方向在装置平面上的投影；投影为零时使用 `Forward`。 |
| `Rotate(direction, degrees)` | 绕 `Up` 旋转一个世界方向，例如偏转出射角度。 |

`a`、`a.Motion`、`a.Hold` 和出口对象仅在创建它们的本次回调内有效，包括 `OnBallUpdate`。不要保存后在 `Update`、下一次碰撞或结束回调中复用；跨回调访问会报错，向另一个回调传入出口会被拒绝。可以保存普通数字作为 Lua 状态。运动接口面向当前弹珠，不暴露 ECS 实体或 Rigidbody；不要通过 `self` 绕过运动接口修改弹珠刚体。

### 其他已接入的效果

离心机在下坡方向增加速度：

```lua
function OnContact(a)
    if not a:Trigger(0) then return end
    local m = a.Motion
    m:TryLaunch(m.Source, m.Source.Down, m.Speed + a.Parameter)
end
```

弹板沿自身朝向弹出，保证最低速度：

```lua
function OnContact(a)
    if not a:Trigger(0) then return end
    local m = a.Motion
    m:TryLaunch(m.Source, m.Source.Forward, math.max(m.Speed, a.Strength))
end
```

减速带由 Lua 决定倍率：

```lua
function OnContact(a)
    if not a:Trigger(0) then return end
    a.Motion:TryScaleSpeed(a.Parameter)
end
```

分裂器的低价值弹珠分支使用 `source:Rotate(source.Down, ±45)` 配合 `TryLaunch`。以上效果均已写入相应 `.mres`。文档示例不是运行时脚本来源；发布时需要更新包内 Lua，然后刷新装置目录。

## 其他装置回调 API

| API | 含义 |
| --- | --- |
| `Trigger(key)` | 在 `OnContact` 或 `OnBallUpdate` 中检查触发率、装置冷却和每球次数，成功后应用通用机器倍率/复制修正。`0` 使用当前装置 ID，连线用 `LinkKey`，配对装置可用 `-PairId`。在 `OnHoldEnd` 中返回 `false`。 |
| `TryUse()` / `RemainingDurability` | 接受一次有效作用、读取当前剩余耐久；`-1` 表示无限耐久。与 `Trigger` / 吞噬 / 结算共享本回调的一次扣除，详细契约见 Metadata 与耐久章节。 |
| `Visit(key, maximum)` | 只记录每球访问次数，无装置公共冷却；`maximum = 0` 表示不限制总次数，同一帧不重复。 |
| `Visit(key, maximum, inheritOnCopy)` | 同上；传 `true` 时，成功登记的这条记录随 `CreateCopy` 副本继承，适合已继承效果的防重复标记。普通访问记录仍从空开始；等分和按价值分球始终继承全部访问记录。只登记访问，不扣耐久、不施加效果。 |
| `HasTriggeredOtherDevice` | 只读：当前弹珠此前是否已受到其他装置实例的有效作用，排除当前实例；相同装置类型的另一实例仍算其他装置。与访问次数、耐久独立，具体记录规则见“有效触发历史”。 |
| `MarkTriggered()` | 为当前球记录当前装置的一次有效作用，重复调用幂等。仅供 Lua 自行完成的纯状态效果（如成功蓄力）显式登记；不扣耐久、不增加访问或倍率、不设置冷却。 |
| `DeviceScoreMultiplier` | 当前球持有的累计装置倍率，只读：`max(0, min(1000, 1 + Σ(amount - 1)))`。无贡献时为 1；不包含临时全盘装置效果、发射器、独立永久修正、全局属性、Rush 或得分区倍率。复制、分裂或平均球的持有倍率时用此值，避免永久继承临时加成。与 `ScoreMultiplier`（当前装置的配置倍率）不同。 |
| `EffectiveDeviceScoreMultiplier` | 当前有效装置倍率：在球持有的增量上，加上仍生效的全盘装置增量，再以 1 为基础钳制到 0–1000。用于需要当前完整装置倍率的兑换等效果；不包含发射器及其他独立倍率。 |
| `Get(attribute)` / `Set(attribute, value)` / `Global(attribute)` | 读取弹珠属性、设置基础属性、读取全局属性。名称使用 `GameAttribute` 中的大写字符串。 |
| `AddBase(attribute, amount)` | 给当前弹珠保存的基础属性加上一个有限增量，再重新计算当前属性修正；支持负增量，遵守属性上下限。无返回值，NaN/无穷或相加溢出抛出参数异常且不修改值。只负责属性运算，不扣耐久、不结算、不设置计时。 |
| `SetCurrent(attribute, value)` | 将指定属性的当前值和基础值替换为目标值，并清除这颗球在该属性上已有的 ADD/MUL/SET 修正，避免当前值再次参与旧修正。遵守属性上下限；NaN/无穷抛出异常且不修改。其他属性修正、累计装置倍率、固定得分加成保持原样。只负责属性替换，不扣耐久。 |
| `Value` / `Parameter` / `Strength` | 弹珠基础价值、装置 `Value` 参数、装置力度参数。 |
| `Coins` | 当前玩家持有的金币，只读；弹珠回调和 `OnTick` / `OnActivate` 的装置上下文均可读取。返回 Lua number，钱包上限 9,000,000,000,000,000 范围内整数精确；不预留或消耗金币，不缓存历史余额，跨回调读取旧上下文会报失效。 |
| `Blood` / `BloodCapacity` | 当前可用于发射的血量、血量上限，只读；弹珠和装置事件上下文均可读取。 |
| `RestoreBlood(amount)` | 给玩家回复血量，返回实际回复量；弹珠和装置事件上下文均可调用。支持非负有限小数，超过容量的部分舍弃，满血或传 0 返回 0；负数/NaN/无穷抛出异常。只操作血量，不修改弹珠价值、不发金币、不扣耐久。详见造血章节。 |
| `ScoreMultiplier` / `RushChanceAdd` | 装置的计分配置与旧概率兼容字段；概率字段不触发累积 RUSH。 |
| `Time` / `PairId` / `LinkKey` | 当前回合秒数、库存配对 ID、当前连线标识。 |
| `RoundId` / `BallId` | 当前回合 ID；弹珠的稳定字符串 ID，格式为 `回合:出生序号`，避免 Lua 数字丢失 64 位序号精度。 |
| `InstanceId` / `SpawnSourceId` | 当前装置的库存实例 ID；当前弹珠的生成来源装置 ID，玩家发射为 `0`。`CreateCopy` / `CreateSplitBatch` 记录本次来源；旧 `Children` 继承原来源。祖先链分别用 `HasCopiedFrom` / `HasSplitFrom` 查询。引擎只提供信息，是否允许反哺来源装置由 Lua 判断。 |
| `CreateSpawnBatch()` | 创建当前回调内使用的 `DeviceSpawnBatch`，逐颗指定位置、速度、价值并整批提交；不消费当前弹珠，详见批量生成章节。 |
| `CreateSplitBatch(count)` | 创建当前弹珠的等分批次 `DeviceSplitBatch`，由 Lua 指定出口位置和速度，全部生成成功才消耗原球；见等分章节。 |
| `CreateCopy()` | 创建当前回调内使用的一次性复制请求 `DeviceCopy`；保留原球，由 Lua 设置副本价值、位置和速度，见复制章节。 |
| `CreatePortion(value)` | 创建当前回调的一次性价值分出请求 `DevicePortion`。成功生成一颗子球才从原球扣除指定价值，失败不扣；可组合滞留实现连续发射，见按价值分出章节。 |
| `HasCopiedFrom(instanceId)` | 当前球是否包含此装置实例的复制祖先记录。再次复制、等分及旧 `Children` 均保留记录；新发射和独立生成从空记录开始。只查询，由 Lua 决定是否跳过。 |
| `HasSplitFrom(instanceId)` | 当前球是否由该实例的等分批次产生，包含继承的祖先记录。复制及再次分流保留记录，新发射和独立生成从空记录开始；是否跳过由 Lua 决定。 |
| `DeltaTime` | 当前固定模拟步的秒数，不是渲染帧间隔。 |
| `Radius` / `Range` | 当前弹珠世界半径；来源装置配置的作用范围。引擎不自动用 `Range` 过滤更新事件。 |
| `StoredValue` | 可读写的装置储存数值。 |
| `TransportLockedUntil` / `ExitDown` | 可读写的弹珠传送锁截止时间、旧一次下坡出射标志。当前只改位置的传送门不使用/消费 `ExitDown`；需要方向覆盖的其他脚本可自行读取和清除。 |
| `RushOnSettlement` / `RefundOnDespawn` | 可读写的旧 Rush 概率兼容字段、消失返还标志。 |
| `TryConsume(refund)` / `AddCoins(value)` | 吞噬当前弹珠（参数控制是否保留返还资格）、增加金币，均不隐式扣耐久。重复吞噬返回 `false`；吞噬后应结束本次回调。旧 `Consume(refund)` 保留自动登记耐久的兼容语义。 |
| `TrySettleAmount(score)` | 按指定最终分数立即结算并消耗当前弹珠，不隐式扣耐久，成功返回 `true`。Lua 决定公式；不额外套用机器/全局/Rush 倍率，不触发 Rush 抽签或增长。非法、负数、已经结算/待消失的弹珠返回 `false`；有限大值遵守单球/回合上限。保留小数累计、计分事件与消失返还资格；成功后不要继续操作弹珠。旧 `TrySettle(score)` 另有自动登记耐久的兼容行为。 |
| `SetStatus(text)` | 发布当前装置的显示文字，最多 256 字符，`nil` 清空；用于进度/提示/倒计时。只改变表现信息，不激活效果或推进时间。结算后仍可更新装置的文字。 |
| `Children(count, replace)` | 通过通用生成系统产生子球，可替换父球；继承、代数与数量上限由引擎处理。 |
| `AddScoreMultiplier(amount)` | 传入本次完整倍率，引擎累加 `amount - 1`，最终装置倍率为 `1 + Σ(amount - 1)`；两次 1.5 为 2。只记录，得分区统一计算，不改基础价值、不立即加分、不隐式扣耐久。有限非负数有效，1 为中性、0 贡献 -1；负数、NaN、无穷抛错且不修改状态。增量最高 999，最终装置倍率最低 0、最高 1000；负增量保留用于后续相加。正常结算另遵守 `MACHINE_MULT` 等内容上限。 |
| `MultiplyScore(factor)` | 旧包兼容名称，内部转发 `AddScoreMultiplier`，同样只累加 `factor - 1`；新脚本使用新名称。 |
| `SetDeviceScoreMultiplier(amount)` | 将当前弹珠的完整累计装置倍率替换为 `amount`，内部增量改为 `amount - 1`；接受 0–1000 的有限数，非法数值抛错且不改变状态。保留基础价值、固定加分和独立基础倍率，不立即计分、不隐式扣耐久；实际改变时登记触发历史。设为 0 后，后续 `AddScoreMultiplier(3)` 会得到 2，不锁定最终得分。仅当前弹珠回调有效。 |
| `AddRushChance(amount)` | 旧概率兼容写入，不参与新的累积触发；新装置使用 `AddRushContribution`。 |
| `RushDurationBonus` | 当前球使 RUSH 条累满时的额外秒数，默认 0，有限范围 `[0,3600]`；不刷新已经激活的 RUSH，详见重构章节。 |
| `Roll(probability)` | 使用整局统一随机序列进行概率判定；与初始装置、商店及开奖共用。 |
| `RandomRange(minimum, maximum)` | 使用整局统一随机序列在指定区间均匀取数；有限数值且下界不大于上界。上下界相等时直接返回该值，不消耗随机状态。 |
| `BreakPinsOnCross()` / `RemoveDevice()` | 清除当前装置十字范围内的普通钉子、消耗当前装置。 |

旧 `a.Speed`、`ScaleVelocity`、`Exit`、`ExitPartner` 作为兼容别名保留，内部也走新的通用运动 API。新脚本优先使用 `a.Motion`。

`Get` / `Value` 读取的是含当前修正的值，`Set` 写入的是未修正的基础值；做增量效果请用 `AddBase`，避免把已经乘过倍率的数再次写回基础值。例如基础价值 10、当前倍率 ×2 时，`AddBase("BALL_BASE_VALUE", 1)` 后当前值为 22，移除倍率后为 11。它与其他弹珠 API 一样仅在当前回调有效，跨回调使用会抛出失效异常。

## 装置倍率统一累加，到得分区计算

所有提供撞击得分倍率的装置共用一个增量累计值，最终倍率为 `1 + Σ(每次倍率 - 1)`。没有贡献时默认 ×1；两次 ×1.5 得到 `1 + 2 × (1.5 - 1) = ×2`。首次提供 1.2 得到 ×1.2，再提供 1.2 得到 ×1.4，之后 Multi Ding 提供 1.85 得到 ×2.25。基础的 ×1 始终只算一次。球价值 10 时，上述三次撞击不直接增加回合分数，进入普通得分区才得到 22.5（整数分与小数余量按原系统累计）。Lua 传入原始完整倍率，不要提前减 1。

Lua 的 `AddScoreMultiplier`、旧包的 `MultiplyScore`、`Trigger` / `TryPrepareTrigger` 接受的装置 `MACHINE_MULT` 修正，以及旧倍率钉接触都走同一规则；中性的通用修正 ×1、普通钉、Rush 钉不额外提供倍率。装置各自的访问次数、冷却和耐久规则不变，例如 Multi Ding 仍每球每实例一次。复制/分裂继承已累计的装置倍率增量及独立基础倍率，发射器新发射的球从空累计开始。

发射器、非装置永久修正、全局、得分区倍率继续在正常结算公式中应用；RUSH 核心改为临时价值 +5；它们不混入装置贡献和。基础价值加减（如沙包、矿老板）仍修改基础属性。黑洞的专用 `TrySettleAmount(EffectiveValue × 10)` 是原有吞球即时结算效果，保留显式金额语义，不经过撞击倍率累计。

需要重置累计值的效果使用 `SetDeviceScoreMultiplier(amount)`。例如当前 ×13，调用 `SetDeviceScoreMultiplier(0)` 后为 ×0，再调用 `AddScoreMultiplier(1)` 仍为 ×0，随后 `AddScoreMultiplier(3)` 为 ×2。复制、分裂和逐颗分出继承这个普通累计值，之后也可以继续增加；新发射的球仍从 ×1 开始。现有 `AddScoreMultiplier(0)` 的贡献 -1 语义保持不变。

## 已放置装置数量查询

弹珠回调 `DeviceContactContext` 与装置事件 `DeviceEventContext` 均提供 `CountPlacedDevices(excludedInstanceId = 0)`，实时读取当前游戏会话的已放置库存实例数量：

- 不传参数或传 0：统计全部已放置实例，包括调用者；传入某个实例 ID：只排除该实例。未知 ID 或未放置实例的 ID 不影响结果；负数抛出参数异常。
- 以库存实例的 `Placed` 状态为准，已耗尽耐久但仍在场也计数；未放置库存、商店商品、固定钉子和制作/预览对象不计数。每扇已放置传送门独立计 1，按实例而非种类或购买份数计数。
- 放置、收回、出售、移除立即影响之后的查询，不缓存到下一回合，也不依赖模型或碰撞体是否启用。此 API 不修改倍率、不扣耐久、不登记触发历史；回调结束后不可继续调用。
- “是否排除自身”和数量转效果的公式由 Lua 决定。例如 `api:CountPlacedDevices(api.InstanceId)` 查询其他已放置装置数。

`CountPlacedDevicesOfDefinition(definitionId)` 在 `DeviceContactContext` 与 `DeviceEventContext` 中按定义 ID 统计全部已放置同类实例，包含自己及耗尽耐久的实例，排除未放置库存；未知 ID 返回 0，0 为非法参数。查询不扣耐久、不登记触发，回调结束后失效。

### 向已放置装置发送事件

两种上下文提供 `ForEachPlacedDevice(definitionId, callback, value)`：先取得指定定义的已放置实例快照，按实例 ID 升序，在各自的 Lua 环境调用 `callback(api, value)`。每次创建独立的 `DeviceEventContext`，接收者包含调用者；只传递有限数值 `value`，不可借此共享旧上下文。

- 仅在运行回合、来源装置可执行时发送。接收者必须仍已放置、有物理展示且能执行或完成已有持续效果；耗尽且无待完成效果、缺少回调的实例跳过。数量查询仍会计入耗尽的已放置实例，因此查询数量与成功回调数可能不同。
- 返回正常完成的回调数。某一接收者抛出 Lua 异常时记录异常并继续其他接收者；已经执行的修改不回滚。定义 ID 不得为 0，回调名非空且最多 64 字符；嵌套事件最多 8 层。
- 发送不自动扣耐久、改状态、设置计时或改变倍率。接收者自行组合已有能力；本次回调结束后，其上下文和 `Board` 等派生 API 均失效。

### 临时全盘装置倍率

接触和装置事件上下文都提供 `api.Board`（`DeviceBoardApi`）：

| API | 含义 |
| --- | --- |
| `Board:AddScoreMultiplier(amount, seconds)` | 注册一份全盘临时装置倍率，传完整倍率，实际贡献 `amount - 1`；返回正整数效果 ID。不能执行或达到同时 256 份效果的上限时返回 0。 |
| `Board:GetScoreMultiplierEndTime(effectId)` | 返回有效效果的回合时间截止点；未知、过期、所属回合结束、来源装置移除或收回时返回 0。可用于让多件装置同步到同一效果的截止时间。 |

`amount` 必须为 0–1000 的有限数，持续时间必须大于 0 且不超过 60 秒。不同效果分别计时、增量相加；注册本身不扣耐久，也不自动合并同一来源的多次调用，Lua 应控制一轮只注册一次。计时使用模拟回合时间，暂停时不推进。来源耗尽耐久不提前撤销已开始的效果；来源移除/收回、回合结束会取消，超时清理不依赖 Lua 成功回调。

普通和赌博得分区、弹珠快照在读取时实时合成 `1 + 球持有增量 + 当前全盘增量`，再应用既有基础及其他倍率和上限。效果适用于当前和之后产生的所有弹珠，包含被滞留的球；不会写入 `MarbleScore.DeviceMultiplierBonus`，复制/分裂只继承持有增量，不会把临时效果变成永久倍率或重复继承。到期只移除该效果，其他装置贡献保持。有效的非中性效果会登记作用历史，赚赚兑换使用 `EffectiveDeviceScoreMultiplier`；显式传入最终分数的 `TrySettleAmount(score)` 仍不暗中追加任何倍率。

例如球原本累计 ×1.5，`Board:AddScoreMultiplier(4, 5)` 在 5 秒内提供 +3，全盘当前有效倍率为 ×4.5；期间新球为 ×4，到期分别恢复 ×1.5 和 ×1。

## 库存提交与装置回合数值

### 按回合数值查询同类装置

接触回调 `DeviceContactContext` 和装置事件 `DeviceEventContext` 提供：

| API | 含义 |
| --- | --- |
| `DefinitionId` | 当前装置的定义 ID（同类装置相同）；`InstanceId` 是这件库存物品的独立实例 ID。 |
| `CountPlacedDevicesWithRoundValue(definitionId, key, value)` | 统计指定定义下，当前回合 `GetRoundValue(key)` 与 `value` **精确相等**的已放置实例数，包含自身。 |

查询实时读取实例状态，不依赖模型、碰撞体、Lua 局部变量或剩余耐久；已耗尽但仍放置的实例计入，背包、商店与预览不计。缺失 key 或其他回合的值视为 0；因此查询 0 也包含尚未写入这个 key 的实例。布置阶段查询即将开始的回合，与 `GetRoundValue` / `SetRoundValue` 一致。放置、收回、删除和数值写入立即影响查询。

`definitionId` 必须非 0，未知定义返回 0；key 与回合数值接口限制一致，value 必须有限。此 API 只查询，不扣耐久、不登记触发、不修改倍率；回调结束后不可继续使用。需要按同类状态计数时可写 `api:CountPlacedDevicesWithRoundValue(api.DefinitionId, "lit", 1)`，引擎中不含装置名称、亮灯条件或奖励公式。

### 模型节点的可见性

`self:TrySetRenderersVisible(nodePath, visible)` 仅切换当前 MRes 实例指定节点及其后代的 `Renderer.enabled`。`nodePath` 是相对包根节点的路径，空字符串表示整个实例；不存在、没有 Renderer、路径无效或对象已销毁时返回 `false`。路径最多 256 字符，不接受绝对路径、空路径段、`.`、`..` 或反斜杠。

这个接口不更改 GameObject 激活状态、碰撞体、刚体或其他实例，不修改共享材质，也不自动生成灯光；即使节点未激活也可设置 Renderer 状态，但不会将节点激活。亮灭模型和发光材质由制作端提供，Lua 选择显示哪个面。没有展示对象时 `self == nil`，Lua 应跳过显示操作。重新绑定展示对象时可在 MRes 的 `Start()` 中恢复外观，不跨回调保存游戏上下文。

### 库存提交事件

`OnOffer(api)` 是通用库存提交事件。游戏界面从背包拖动一件装置，松开在已放置且含 `OnOffer` 的装置上时，调用控制器 / 模拟器 `TryOfferDevice(offeredId, targetId)`。`CanOfferDevice` 只查询提交资格和是否有回调，不运行 Lua、不消耗物品，不代表脚本一定接受。提交限定为 Build：来源必须是本会话未放置的库存实例，目标必须已放置，不能提交自己；暂停、战斗阶段或拖到界面控件上均不允许。布置阶段可给上回合已耗尽耐久的装置准备下一回合。

| `DeviceOfferContext` API | 含义 |
| --- | --- |
| `InstanceId` / `RoundId` | 接收装置实例和**即将开始的回合 ID**。布置阶段的 `RoundId` 为当前模拟回合 ID + 1。 |
| `DefinitionId` | 接收装置的定义 ID；可与 `OfferedDefinitionId` 比较，由 Lua 决定是否只接受同种物品。 |
| `OfferedInstanceId` / `OfferedDefinitionId` | 本次提交的库存实例和定义 ID。一次只提交一件，不自动处理同批购买或配对的其他实例。 |
| `OfferedName` / `OfferedRarity` | 来源名称和 `common` / `rare` / `epic` / `legendary` 稀有度，从当前 MRes 定义读取。 |
| `Parameter` / `Strength` | 接收装置的业务 `Value` / `Strength`，由 Lua 用于自己的公式。 |
| `GetRoundValue(key)` | 读取该实例在目标回合的命名数值，不存在返回 0；能读到同一次回调暂存的新值。 |
| `SetRoundValue(key, value)` | 暂存数值；key 非空且 UTF-8 最多 120 字节，value 必须有限，每实例每回合最多 32 个不同 key。 |
| `GetInstanceValue(key)` / `SetInstanceValue(key, value)` | 读取/暂存跨回合保留的实例数值，详见下节；与本次物品消耗一起提交。 |
| `DurabilityLimit` / `RemainingDurability` | 读取接收实例的当前耐久上限/剩余耐久，包含本次暂存的修改。 |
| `SetDurabilityLimit(maximum)` / `RestoreDurability()` | 分别暂存上限调整、补满耐久；契约见下节。 |
| `Accept()` | 标记接受本次提交；重复调用仍只处理这一件物品。 |
| `SetStatus(text)` | 暂存接受后的状态提示，最多显示 256 字符。 |

回调正常结束且调用 `Accept()` 后，引擎提交回合/实例数值、耐久设置与状态提示，删除来源库存实例并递增 `LayoutRevision`，刷新碰撞可用性，最后分发独立的新 `OnOfferAccepted(api)`。没有调用 `Accept()` 或 `OnOffer` 回调异常时，上述修改全部不提交，来源物品保留。接受不奖励出售金币、不继承来源自身的成长，也不扣接收装置耐久。原有点击选择和点击放置保持原行为，消耗物品使用明确的拖放动作；拖动开始时捕获实例 ID，翻页或库存重排不改变所提交的物品。

接触回调 `DeviceContactContext` 和装置事件 `DeviceEventContext` 同样提供 `GetRoundValue(key)`、`SetRoundValue(key, value)`，但它们操作当前回合，写入立即生效。布置写入的数据属于下一回合，开始这一回合时保留；再次进入 Build 时查询新回合，旧值视为 0，不喂食则下一场仍为 0。收回再放置不丢失同一轮准备的数据，出售或删除实例移除其数值。它与每次开局直接清空的旧 `StoredValue` 不同。

上下文仅在本次回调有效，结束后调用属性、数值方法或 `Accept` 都会报错。事务保证覆盖这些 API 暂存的数值、耐久与状态，不回滚 Lua 自己的局部变量；需要与物品消耗同时提交的成长应通过 `SetRoundValue` 或 `SetInstanceValue` 保存。表现缓存可在 `OnOfferAccepted` 中从已提交的实例状态更新；该通知出错只记录异常，不撤销已经完成的接受。

```lua
function OnOffer(api)
    local gains = { common = 2, rare = 4, epic = 6, legendary = 8 }
    local gain = gains[api.OfferedRarity]
    if gain == nil then return end
    api:SetRoundValue("bonus", api:GetRoundValue("bonus") + gain)
    api:Accept()
end

-- 在 OnContact 中另行判定有效碰撞，再扣耐久并施加效果：
-- api:AddScoreMultiplier(1 + api:GetRoundValue("bonus"))
```

## 降降火（已实现）

制作源 `MResPacker/Authoring/Lua/CoolingTea.lua`，业务 ID `9030`，游戏包 `Resources/Devices/9030_CoolingTea.mres`。保留青瓷凉茶壶、壶嘴、弧形把手和壶盖的模型、独立材质、Prefab 与制作场景。

首次有效实体碰撞将当前 `BALL_FRICTION ×1.2`、`BALL_BOUNCE ×0.9`，记录 `RushDurationBonus=5`，扣 1 耐久。只有该球在底部/得分区累满 RUSH 条时，才使本次 RUSH 由 2 秒变为 7 秒；未累满或已在 RUSH 中均不加时。物理参数换算、同球一次、跨实例去重保持原规则。

Lua 用共享定义键 `-(4294967296 + DefinitionId)` 和 `Visit(key, 1, true)`；复制/等分/按价值分球继承效果和防重复记录。`SetCurrent` 以实际当前物理属性换算，清除对应旧修正，其他属性不变；下个物理步同步 PhysX 静/动摩擦与弹性，不改已经求解的反弹速度。新球默认额外时长 0，球消失时清理。`Strength=.2`、`Range=.1`、`Value=5` 的数值不变。

## 旧限时 RUSH 概率 API（兼容）

`Board.SetRushChanceBonus(key, amount, seconds)`、`RemoveRushChanceBonus(key)`、`GetRushChanceBonusEndTime(key)` 和 `RushChanceBonus` 保留原有存储、生命周期和参数校验，供旧包加载。**新的累积结算不读取这些概率。** 有锅气已迁移到球贡献，不再调用这组接口。

## 有锅气（已实现）

业务 ID `9029`，源 `MResPacker/Authoring/Lua/Wok.lua`，运行时 `Resources/Devices/9029_Wok.mres`。敞口锅、双把手、传感器模型保持。

1. 成功入锅调用 `Motion:TryHold(1)`：可见、无碰撞，暂停平移与旋转。
2. `AddRushContribution(10)` 给当前球记录额外贡献，加上这次成功装置作用的 5，共增加 15。普通新球从 2 变为 **17**；未落底之前不改变全盘条值，其他球不受影响。不同锅可继续给同一颗球增加贡献。
3. 完整 1 秒后使用新的 `OnHoldEnd`，向棋盘上方 ±60° 随机抛出，保留入口速度。出射受阻时原位恢复该方向速度。滞留结束不会再次计入同一装置作用。
4. 每颗成功释放扣 1 耐久，入锅、取消不扣。最后一颗离锅后保持原有 **5 秒冷却**，冷却时穿过；结束后可再次入锅并再次增加贡献。同锅容量按剩余耐久预留，最后一点完成释放，-1 无限。

`game/device.json` 的 `Strength=10` 为额外贡献，`Range=60` 为抛射角，`Value=1` 为翻炒时长。每回合清理滞留与冷却；球上已获得的贡献随球存活，不随离锅撤销。普通、购买 5、出售 120、耐久 20。

## 跨回合实例数值、动态耐久和生命周期

接触上下文 `DeviceContactContext`、装置事件 `DeviceEventContext`、库存提交 `DeviceOfferContext` 提供以下独立基础能力：

| API | 契约 |
| --- | --- |
| `GetInstanceValue(key)` | 读取该库存实例的命名数值，未写入时为 0。 |
| `SetInstanceValue(key, value)` | 保存有限数值，key 非空且 UTF-8 最多 120 字节，每实例最多 32 个不同 key。跨回合、收回再放置、模型重建均保留；出售/消耗/删除实例或重开会话时移除。不是磁盘存档接口。 |
| `DurabilityLimit` | 当前实例的有效上限；未覆盖时使用 Metadata，覆盖后不修改包的 Metadata 或其他实例。 |
| `SetDurabilityLimit(maximum)` | 只设置当前实例上限，**保留已消耗次数**。合法整数 `-1～10000`：`-1` 无限，`0` 表示休眠，正数为有效次数。上限覆盖跨回合保留；库存快照和碰撞可用性随之更新。 |
| `RestoreDurability()` | 清除当前实例已消耗次数，恢复到当前上限。不改变上限、实例状态、访问次数、冷却或批次去重记录。新回合引擎也会按实例有效上限自动恢复。 |

接触/装置事件调用立即生效；`OnOffer` 调用先暂存，只有接受成功才提交。API 不自动选择阶段、倍率或升级条件，不隐式登记触发/扣耐久。Metadata 的 `durability` 仍仅接受 `-1` 或 `1～10000`；运行时上限允许 `0`，用于由 Lua 控制的休眠阶段。

以下生命周期事件使用每次新建的 `DeviceEventContext`，**不要求已放置、物理视图或剩余耐久**：

| 事件 | 时机 |
| --- | --- |
| `OnCreated(api)` | 每个库存实例创建后一次，可初始化阶段、耐久上限；商店购买、配对购买的每件物品和初始库存均适用。 |
| `OnOfferAccepted(api)` | 向当前实例提交物品的事务成功后一次，读取已提交状态、刷新外观/文字。 |
| `OnRoundStart(api)` | `BeginRound` 完成回合重置和耐久恢复后，按实例 ID 通知所有已拥有装置，包括背包与休眠实例。 |

以上事件不扣耐久；没有物理视图时不要读取 `Position` / `Forward` 等姿态属性。回调异常会记录并关闭上下文，已经生效的操作不回滚。状态文字在 Build 对应下一回合，在战斗中对应当前回合。模型 `Start()` 无参数，只恢复缓存的普通外观状态；不得缓存旧 `api`。

## 种植牙（已实现）

制作源 `MResPacker/Authoring/Lua/DentalImplant.lua`，业务 ID `9028`，游戏包 `Resources/Devices/9028_DentalImplant.mres`。制作目录包含可编辑模型、材质、Prefab、配置和 `DentalImplantAuthoring.unity` 场景。

“五个阶段”按未放置、挖洞、不锈钢牙、陶瓷牙、金刚钻牙理解。第一次购买并放置留下无效果、无碰撞的洞；布置时将背包中的另一件种植牙拖到洞/牙上，消耗一件，逐级升级并补满耐久。其他装置、已放置的来源、自身或最高阶段的继续升级都会拒绝，物品保留。

| 已放置阶段 | 每次撞击附加增量 | `AddScoreMultiplier` 参数 | 阶段耐久 |
| --- | --- | --- | --- |
| 挖洞 | 无 | 不调用 | 0，弹珠直接穿过 |
| 不锈钢牙 | +2 | 3 | 5 |
| 陶瓷牙 | +5 | 6 | 8 |
| 金刚钻牙 | +20 | 21 | 20 |

每次有效实体撞击扣 1，原球正常反弹；同一步重复报告只算一次。最后一次仍完整增加倍率，然后立即退回洞、关闭碰撞并切换外观，之后必须重新种植。未耗尽的阶段跨回合保留，新回合补满该阶段耐久；洞不会自动长回牙。倍率沿用累计增量，到得分区统一计算。

Lua 以 `GetInstanceValue/SetInstanceValue("stage", ...)` 保存阶段，`OnOffer` 组合同种检查、上限设置、补满和 `Accept`。`OnOfferAccepted` / `OnRoundStart` 刷新外观与状态。阶段表中的倍率和耐久可在 Lua 顶部调整并重新打包；C# 不含种植牙名称、ID、档位或倍率公式。

Metadata 默认普通、购买 5、出售 120、耐久 20，初始库存 0。Metadata 是商店基础配置，当前阶段的有效耐久由实例 API 覆盖并显示在背包和场上。

## 星星灯（已实现）

制作源 `MResPacker/Authoring/Lua/StarLamp.lua`，配置 `Authoring/Profiles/StarLamp.asset`，成品 `Output/StarLamp.mres`，运行时 `Resources/Devices/9027_StarLamp.mres`。模型为沿棋盘平面铺开的立体五角星，深色边框、暗金关闭面和黄色发光开启面，只有 Trigger 区域，弹珠直接穿过。

1. 未激活时，每颗弹珠每次进入切换当前灯一次；停留不重复触发，离开重入可再切换，同球同一步报告去重。至少有 2 盏已放置星星灯，且**全部点亮**才开始一轮效果；3 盏只亮 2 盏没有倍率加成。其他类型、背包库存和预览不计。
2. 最后一盏点亮时，Lua 只注册一份 `Board:AddScoreMultiplier(1 + 灯总数, 5)`，为**全盘所有弹珠**临时增加灯总数点装置倍率。3 盏灯为 +3，普通新球当前 ×4；球已持有 ×1.5 时为 ×4.5。包括没有经过灯的球、新发射和复制/分裂的球，作用范围不限于灯区。按激活时数量确定本轮增量，到得分区按当时是否仍处于效果期计算。
3. **持续 5 秒**，参与灯保持亮着，弹珠经过不切换、不叠加、不刷新截止时间。结束时移除临时全盘加成、所有参与灯熄灭，并且**每盏参与灯各扣 1 耐久**；不按弹珠数量或经过次数扣。未凑齐全亮时免费，最后一点耐久完整支持整轮 5 秒；`-1` 无限。
4. 熄灭后，可重新逐盏点亮开始下一轮。耗尽的灯关闭且无法切换，仍放在场上时计入“全部灯”条件，需要下回合恢复或在布置阶段收回；不会保留旧规则的“耗尽亮灯”。每回合开始所有灯关闭、恢复耐久，未完成的上一回合效果取消，不额外扣费。
5. 通过通用装置事件把效果 ID 和截止时间分发给参与灯，各自保存普通回合数值，不保存旧上下文。模型重建通过 `Start()` 恢复外观。倒计时遵循游戏模拟时间，暂停不会推进；来源装置被移除/收回会取消全盘效果，其余灯检测到取消后熄灭、不扣完成耐久。
6. 临时加成不会永久写入球的累计倍率；5 秒结束后，即使球还没结算也会失去这份加成，保留其他已获得的装置增量。正常计分、赌博计分、弹珠信息和赚赚兑换都读取当前有效倍率；只有一次全盘效果，不会因为有多盏灯而再乘或叠加多份。

`Authoring/Data/StarLamp.device.json`：ID 9027，`Value = 5` 为持续秒数（Lua 限制最多 60 秒），`Strength = 1` 为每盏灯的附加增量，`Range` 未使用。这里业务 `Value` 是时长，metadata `value` 仍是售价。默认普通、20 耐久、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Star Lamp Device` 定位配置。

验证源 `Editor/Validation/GameContentValidation.StarLamp.cs`、`GameContentValidation.StarLampApi.cs`、`GameContentValidation.StarLampPhysics.cs` 覆盖全亮条件、同步窗口、未经过灯和新产生的球、永久/临时倍率合成、复制分裂不永久继承、正常/赌博结算与兑换、有限/无限耐久、回合与来源清理、接口有效期，以及两种步长下真实穿过、停留、高速穿越、展示重建和统一熄灯。

## 大胃袋（已实现）

制作源 `MResPacker/Authoring/Lua/GluttonBag.lua`，配置 `Authoring/Profiles/GluttonBag.asset`，成品 `Output/GluttonBag.mres`，运行时 `Resources/Devices/9026_GluttonBag.mres`。模型为橄榄绿圆柱袋身、浅褐色袋口与牙齿、深色口部，袋身有实体凸碰撞。

1. 先放置大胃袋，布置阶段从背包拖入其他装置；普通、稀有、史诗、传奇分别增加 **2、4、6、8 点附加增量**，消耗提交的单件。不同大胃袋独立成长，喂入另一台大胃袋也只按其稀有度计算，不转移它的成长。
2. 初始增量为 0，不产生效果。喂入普通装置后增量为 2，弹珠碰撞调用 `AddScoreMultiplier(3)`，实际增加 2；同球两次为 `1 + 2 + 2 = 5`。四档各喂一件共增加 20，碰撞提供完整 ×21。重复碰撞继续累加，到得分区统一结算，基础价值不直接修改；仍遵守通用倍率上限 1000。
3. 喂食不扣耐久；有正增量的真实碰撞每次扣 1，同球同一步重复报告去重，不同球各自生效。无成长、Trigger 和连线不生效；最后一点耐久完整作用，`-1` 无限。弹珠正常反弹。
4. 成长用于即将开始的这一回合，进入该回合保留，下一轮布置清空。界面在拖放前提示会消耗库存，接受后显示成长；失败、取消、越界、松开在控件上或离开布置阶段时保留物品。实体物品板与旧 uGUI 背包均支持拖放，拖到合法空白区域仍可正常放置。

`Authoring/Data/GluttonBag.device.json`：ID 9026，`Value = 0` 为初始附加增量，`Strength = 2` 为每档稀有度增量，Lua 使用档位 1/2/3/4；`Range` 未使用。`Strength <= 0` 时拒绝喂食。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Glutton Bag Device` 定位配置。

验证源 `Editor/Validation/GameContentValidation.GluttonBag.cs`、`GameContentValidation.GluttonBagUI.cs` 覆盖四档稀有度、原子提交和回调异常、库存实例校验、配对物品、回合准备与清空、独立实例、耐久、真实拖放控件和两种步长下的实际物理碰撞与得分结算。

## 雪糕机（已实现）

制作源 `MResPacker/Authoring/Lua/IceCreamMachine.lua`，配置 `Authoring/Profiles/IceCreamMachine.asset`，成品 `Output/IceCreamMachine.mres`，运行时 `Resources/Devices/9025_IceCreamMachine.mres`。模型为粉色圆柱底座、三层奶油色圆柱雪糕和薄荷色顶饰，底座有实体凸碰撞。

1. 每台独立记录本回合成功触发次数 `n`，本次完整倍率为 `max(1, 10 - n × 1)`。首次 `n = 0`，依次提供 ×10、×9、×8、×7、×6、×5、×4、×3、×2，之后最低 ×1。同台由不同弹珠共享次数，不同实例互不影响。
2. Lua 调用 `AddScoreMultiplier(本次完整倍率)`，引擎只增加“倍率 − 1”。同一颗球前两次累计倍率为 `1 + 9 + 8 = 18`，全部九次后为 `1 + 9 + 8 + … + 1 = 46`；不是把这些倍率连乘。碰撞时不结算，到得分区统一计算。
3. 倍率大于 1 才调用 `Visit(0, 0)`、`TryUse()` 登记，施加倍率后次数加 1；默认前九次各扣 1 耐久，降至 ×1 后不生效、不登记成功触发、不扣耐久，不改变球已经获得的倍率。最后一点耐久完整生效；无限耐久也不会绕过本回合的倍率递减。
4. 同球同一步重复碰撞报告去重，不同球同一步按处理顺序取得连续档位；持续贴住、Trigger 和连线不生效。原球正常反弹，基础价值、钱包、血量和运动均不修改。此前归零的累计倍率也可获得新的增量。
5. Lua 只跨回调保存 `roundId` 和 `uses` 普通数值，`RoundId` 变化时重置次数为 0；同回合收回再放置不补充次数。状态显示本回合已触发次数、下次完整倍率和用尽提示。默认耐久 20，因此正常九次后剩余 11 耐久，但倍率已降至 ×1；新回合次数和耐久均重置。

`Authoring/Data/IceCreamMachine.device.json`：ID 9025，`Value = 10` 为初始完整倍率，`Strength = 1` 为每次有效触发后的减量，`Range` 未使用。支持小数；初始倍率不大于 1 时无效果，减量为 0 时一直提供初始倍率，减量跨过 1 时钳制到 1。这里的业务 `Value` 与 metadata 的出售价 `value` 不同。默认普通、20 耐久、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Ice Cream Machine Device` 定位配置。

无需新增引擎 API，计数和递减公式由 Lua 组合已有能力。`Editor/Validation/GameContentValidation.IceCreamMachine.cs` 验证全部倍率档位、重复与同时碰撞、实例和回合隔离、收回再放置、无效事件、有限/无限耐久、参数、归零后恢复和最终结算，以及两种步长下真实倾斜棋盘的 11 次重新碰撞与反弹。

## less is more（已实现）

制作源 `MResPacker/Authoring/Lua/LessIsMore.lua`，配置 `Authoring/Profiles/LessIsMore.asset`，成品 `Output/LessIsMore.mres`，运行时 `Resources/Devices/9024_LessIsMore.mres`。模型为紫色圆柱底座与三根逐渐降低的橙银色立柱，底座有实体凸碰撞。

1. 每次真实碰撞用 `CountPlacedDevices(api.InstanceId)` 查询其他已放置装置数 `n`，本次附加倍率为 `max(0, 20 - n × 2)`。不计自身，耗尽但仍在场的装置计入，库存不计；配对门按实际已放置的实例分别计数。
2. 附加倍率是实际增加量，Lua 调用 `AddScoreMultiplier(1 + 附加倍率)`。没有其他装置时增加 20；3 个时增加 14，球初始倍率为 1，撞击一次为 15、两次为 29；9 个时增加 2，10 个及以上不增加。20 是单次增量上限，重复碰撞仍继续累加，到得分区统一计算。
3. 正增量的有效碰撞扣 1 耐久，零增量不登记触发、不扣耐久，也不减少弹珠之前已有的倍率。同球同一步重复报告去重，不同球独立生效；持续贴住、Trigger 和连线不生效。最后一点耐久完整作用，`-1` 无限。原球正常反弹，基础价值、钱包、血量和运动不修改。
4. 状态实时显示其他装置数和本次增量；放置、收回或移除只影响之后的碰撞，不追溯修改已获得的倍率。每个实例只排除自己，新回合恢复耐久并按当前布局查询；此前被归零的累计倍率也能重新增加。

`Authoring/Data/LessIsMore.device.json`：ID 9024，`Value = 20` 为没有其他装置时的附加倍率和单次增量上限，`Strength = 2` 为每个其他装置的减量，`Range` 未使用。支持小数；`Value <= 0` 无效果，`Strength = 0` 表示不随数量衰减。这里的业务 `Value` 与 metadata 的出售价 `value` 不同。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Less Is More Device` 定位制作配置。

无需新增引擎 API；数量查询、去重、耐久登记和倍率累加使用已有通用能力，衰减公式由 Lua 组合。`Editor/Validation/GameContentValidation.LessIsMore.cs` 覆盖数量边界、动态布局、库存与耗尽、重复及跨实例累加、回合与有限/无限耐久、归零后恢复、参数，以及两种步长下真实倾斜棋盘的反弹、零增量边界和结算。

## more is more（已实现）

制作源 `MResPacker/Authoring/Lua/MoreIsMore.lua`，配置 `Authoring/Profiles/MoreIsMore.asset`，成品 `Output/MoreIsMore.mres`，运行时 `Resources/Devices/9023_MoreIsMore.mres`。模型为蓝色圆柱底座与三根逐渐增高的青金色立柱，底座有实体凸碰撞。

1. 每次真实碰撞查询其他已放置装置数 `n`，本次附加倍率为 `min(20, n × 2)`；不计自身，耗尽但仍在场的装置照常计数，库存里的装置不计。
2. “附加倍率”是直接增加量。Lua 调用 `AddScoreMultiplier(1 + 附加倍率)`，让引擎恰好增加该数值。3 个其他装置每次增加 6；球初始倍率为 1，碰撞一次为 7、两次为 13，到得分区统一计算。
3. +20 是单次作用上限，达到 10 个其他装置后饱和，继续放置不提高单次增量。重复撞击仍按通用规则累加，不将弹珠的最终累计倍率限制为 20。没有其他装置时增量为 0，不生效、不扣耐久。
4. 有正增量的有效碰撞扣 1 耐久；同球同一步重复报告去重，不同球同一步各自生效，持续贴住、Trigger、连线不生效。最后一点耐久仍完整作用，`-1` 无限。原球正常反弹，基础价值、钱包、血量和运动均不修改。
5. 状态显示实时数量和本次增量。盘面增减只影响之后碰撞，不追溯修改球已经获得的倍率；各实例只排除自己，每回合按当前盘面重新查询。归零后的弹珠也可获得正常增量。

`Authoring/Data/MoreIsMore.device.json`：ID 9023，`Value = 20` 为单次附加倍率上限，`Strength = 2` 为每个其他装置的增量；`Range` 未使用。上限或增量非正时无效果；支持小数参数。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate More Is More Device` 定位制作配置。

`Editor/Validation/GameContentValidation.MoreIsMore.cs` 覆盖 0/1/9/10/11/20 个装置、上限与重复累加、库存/耗尽/移除/传送门计数、独立实例、动态布局、回合与耐久、归零后恢复、查询有效期和副作用，以及两种步长下真实倾斜棋盘的碰撞、反弹和结算。

## 故障装置（已实现）

制作源 `MResPacker/Authoring/Lua/Malfunction.lua`，配置 `Authoring/Profiles/Malfunction.asset`，成品 `Output/Malfunction.mres`，运行时 `Resources/Devices/9022_Malfunction.mres`。模型为灰色金属圆柱、黄色警戒环、红色警报灯和倾斜断杆，圆柱有实体凸碰撞。

- 每次真实碰撞从 `{0, 1, 2, 3, 5, 7, 11, 13}` 等概率抽取一个结果，每项概率 1/8。Lua 用 `floor(RandomRange(0, 8)) + 1` 选择索引，使用本局随机序列，不使用独立的 `math.random`。
- 抽到 0：调用 `SetDeviceScoreMultiplier(0)`，清空先前所有装置贡献，将当前累计倍率置为 0；保持基础价值和物理运动，后续仍可增加。抽到 1 不增加累计倍率；其余调用 `AddScoreMultiplier(result)`，每次增加 `result - 1`。
- 例：球价值 10，依次抽到 13 → 0 → 1 → 3，累计倍率依次为 13 → 0 → 0 → 2，进入普通得分区最终得到 20。若归零后未获得新倍率，价值部分按 ×0 结算。没有独立的归零标记，也不改写原有固定加分、黑洞显式金额等结算规则。
- 组合 `Impact`、`Visit(0, 0)`、`TryUse()`、随机接口与倍率接口。八种结果均扣 1 耐久，最后一次仍完成抽取；`-1` 无限。重复碰撞可再次抽取，同球同一步重复报告只算一次；不同球同一步各抽一次。持续接触、Trigger、连线及耗尽耐久不抽签、不推进随机状态。
- 每次有效处理均调用 `MarkTriggered()`，抽到 1 或再次归零也登记效果历史。原球正常反弹，不消耗、不传送、不改价值、不直接计分。装置显示最近一次结果，每回合清空显示；球的累计倍率随复制/分裂按通用规则继承。

`Authoring/Data/Malfunction.device.json`：ID 9022，默认普通、耐久 20、购买价 5、出售价 120、初始库存 0；本装置不使用 `Value` / `Strength` / `Range`。候选倍率在 Lua 顶部 `factors` 修改，等概率随列表长度计算。菜单 `Tools > MRes > Create or Locate Malfunction Device` 定位制作配置。

`Editor/Validation/GameContentValidation.Malfunction.cs` 验证全部八种结果、固定种子序列、归零与恢复、跨装置累加、得分区/赌博区结算、复制分裂继承、有效历史、同帧去重、有限/无限耐久、回合重置、接口范围与有效期，以及两种步长的真实倾斜棋盘碰撞和正常反弹。

## 有效触发历史

`HasTriggeredOtherDevice` 查询当前球生命周期内的成功作用，按装置实例判断，不读取 `Visit` 的尝试记录。成功的移动、出射、速度设置、滞留和临时重力操作自动登记；属性修改实际改变当前值、非中性装置倍率、正整数金币奖励也会登记。实际产生非零力的 `OnField` 自动登记，包括黑洞引力和加速力场；只在力场范围内但未施力不算。

`Visit`、`Trigger` 的中性检查、`TryPrepareTrigger`、耐久登记、状态显示、单纯物理碰撞及失败的运动请求本身不登记。纯 Lua 状态效果在成功后调用 `MarkTriggered()`，例如大石碎胸口每次有效蓄力；它不改变“释放时才扣耐久”的规则。已成功的作用记录不会因后来失败、装置移除或效果结束而撤销。

历史与弹珠 ECS 状态一起保存，复制/分裂球继承，新发射的球和独立批量生成的球从空历史开始。对象池复用及新回合不会继承旧球历史。同一个装置反复作用不算“其他装置”，另一个同类型实例算。多个接触在同一物理步内按现有接触处理顺序判断；查询时已经完成的其他装置效果可见。

## 摇钱树：无碰撞经过计数

制作源 `MResPacker/Authoring/Lua/MoneyTree.lua`，配置 `Authoring/Profiles/MoneyTree.asset`，成品 `Output/MoneyTree.mres`，运行时 `Resources/Devices/9014_MoneyTree.mres`。模型为树干、绿色树冠和金币果实，只有一个 `isTrigger = true` 的 BoxCollider；树干、树冠和金币均无实体碰撞，不阻挡或弹开弹珠。

- 每颗弹珠每次进入区域计 1，停留不持续增加；离开后再次进入可重新计数。物理桥对进入、Stay 与高速扫掠去重，Lua 再组合 `Visit(0, 0)` 排除同一球同一步重复事件。实体撞击和连线事件不计。
- 每累计 10 次，组合 `TryUse()` 和 `AddCoins(5)` 发放金币并扣 1 耐久，计数减去 10 后继续积累。前 9 次不扣耐久；同一步多颗球可完成多批，每批各扣 1。第 20 批仍发放完整奖励，之后本回合停止计数；`-1` 无限耐久。
- 不消费弹珠，不修改位置、速度、价值或装置倍率，不计回合分数、不返还血量。金币直接进钱包并遵守钱包上限。每次有效累积调用 `MarkTriggered()` 记录纯 Lua 效果，因此经过摇钱树的弹珠可被后续“赚赚”兑换。
- 每个装置实例独立保存普通 Lua 数字（回合 ID、当前计数、产出批数），新回合清零，未满 10 次的进度不结转。`OnTick` 显示进度和本回合累计产出。

`Authoring/Data/MoneyTree.device.json`：`Value = 10` 为每批经过次数，`Strength = 5` 为每批金币，均向下取整；次数须在 1–10000，金币至少 1，否则不计数、不扣耐久。`Range` 未使用；触发区域尺寸在制作 Prefab 的 BoxCollider 配置。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

此效果复用现有 API，没有增加专用引擎能力。`GameContentValidation.MoneyTree.cs` 验证 9/10/20 次边界、重入、同一步多球、实例独立、回合清零、有限/无限耐久、参数和钱包上限，以及倾斜棋盘的真实穿过、长时间停留、高速扫掠和无物理阻挡。

## 赚赚：按触发历史兑换金币

制作源 `MResPacker/Authoring/Lua/CashOut.lua`，配置 `Authoring/Profiles/CashOut.asset`，成品 `Output/CashOut.mres`，运行时 `Resources/Devices/9013_CashOut.mres`。模型为金色立柱、绿色横梁和金币标志的兑换口，根节点 BoxCollider 为入口触发器。

- 进入时先查询 `HasTriggeredOtherDevice`。存在其他装置有效作用时，金币为 `floor(Value × EffectiveDeviceScoreMultiplier + 0.5)`，**没有额外 ×2**。例如价值 10、累计装置倍率 ×2，返还 20 金币；价值 10.5、倍率 ×1，返还 11 金币。使用当前 `Value`（含基础价值修正），累计倍率按 `1 + Σ(倍率 - 1)` 计算。
- Lua 组合 `TryConsume(false)` 与 `AddCoins`，吞噬成功后只加一次金币，不计回合得分、不返还发射血量、不触发同一步的得分区结算。零价值也消耗；金额遵守钱包上限。`AddCoins` 本身仍向下取整，四舍五入由本装置 Lua 完成。
- 无其他装置有效作用时，组合 `Motion.Source.Down` 与 `TryLaunch`，从棋盘下方出口射出，保留进入速度大小（包括零速，仍受弹珠速度上限约束）。受阻则留在原处、不给金币、不扣耐久，后续重新进入可重试。不把当前实例以前的放行算作其他装置。
- 两种成功处理均通过 `TryUse()` 扣 1；第 20 次完整执行，之后本回合失效，`-1` 无限。`Visit(0, 0)` 排除同一步重复进入，连线事件不参与。不同球可在同一步分别处理。

Metadata 默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。`game/device.json` 的 `Value`、`Strength`、`Range` 对此效果不使用。`GameContentValidation.CashOut.cs` 覆盖四舍五入、累计倍率、自己与其他实例、失败访问、纯 Lua 蓄力、复制与新球、钱包上限、耐久/跨回合、真实入口与旋转棋盘出射、真实力场历史及对象池清理。

## 猪猪存钱罐：按钱包档位提供倍率

制作源 `MResPacker/Authoring/Lua/PiggyBank.lua`，配置 `Authoring/Profiles/PiggyBank.asset`，成品 `Output/PiggyBank.mres`，运行时包 `Resources/Devices/9012_PiggyBank.mres`。模型为粉色陶瓷小猪，包含投币口、猪鼻、耳朵、四脚和尾巴，使用实体 CapsuleCollider。

- 每次真实撞击读取 `Coins`，计算 `n = floor(Coins / 10)`，提供 `1 + 0.1 × n` 的倍率，引擎累计其中的 `0.1 × n` 增量。例如 25 金币提供 1.2，撞两次为 `1 + 0.2 + 0.2 = 1.4`；之后钱包剩 15 金币，再撞一次加 0.1，最终倍率 1.5。钱包改变不追溯修改以前的贡献，也不消耗金币。
- 组合 `Impact`、`Visit(0, 0)`、`TryUse()` 和 `AddScoreMultiplier`。金币不足 10 时不生效、不扣耐久；有效撞击扣 1，最后一次仍完整提供倍率，`-1` 无限。持续贴住、同球同一步重复事件、Trigger/连线不算新撞击，不设公共冷却，同一步不同球各算一次。
- 不消耗弹珠、不改基础价值或速度，得分区使用累计总和。实例分别计算耐久，球分别保存倍率；新回合清理球状态并恢复耐久。`OnTick` 显示当前钱包对应的本次撞击倍率。

`Authoring/Data/PiggyBank.device.json`：`Value = 10` 为每档金币数（正数），`Strength = 0.1` 为每档提供的倍率增量（正数），`Range` 未使用。Metadata 普通、耐久 20、购买价 5、出售价 120、初始库存 0。效果遵守已有总倍率和得分上限；大额钱包不会使倍率调用溢出或抛错。

`GameContentValidation.PiggyBank.cs` 验证金币边界、动态钱包、倍率减 1 后累加与混合装置、普通得分区、通用触发修正、旧倍率钉、复制继承、上下文有效期、耐久和真实物理碰撞。

## 沙包：连续撞击加值，连击越高窗口越短

制作源为 `MResPacker/Authoring/Lua/Sandbag.lua`，配置为 `Authoring/Profiles/Sandbag.asset`，输出 `MResPacker/Output/Sandbag.mres`，运行时为 `Resources/Devices/9008_Sandbag.mres`。模型是棕黄色圆柱配红褐色束带，使用实体凸 MeshCollider；反弹由正常物理及弹珠弹性决定，Lua 不修改速度。

- 每个沙包实例独立保存 `combo`、`endsAt` 和 `roundId`。真实撞击先检查超时，再把连击数加 1，给当前球增加 `combo × Parameter` 的基础价值。首击 +1，第二击 +2，第三击 +3；同一个球离开后再次撞击可以续连，多球同一步撞击也各算一次，按正常接触处理顺序确定连击数。
- 每次命中重设截止时间为 `Time + max(Strength, Range / sqrt(combo))`，默认是 `max(0.3, 3 / √combo)` 秒：1 连击 3 秒、4 连击 1.5 秒、9 连击 1 秒、100 连击及以上最短 0.3 秒。
- 截止时清零；下一击从 1 开始。`OnTick` 在没有新碰撞时也会清空并刷新状态。已增加的弹珠价值保留，之后正常进入计分区时结算，碰撞本身不直接增加回合分数、不消耗弹珠。暂停不推进计时，新回合清空连击。
- 每次有效撞击 `TryUse()` 扣 1 耐久，不使用会限制同一步其他球的公共 `Trigger` 冷却。第 20 次仍完整加值，之后本回合关闭碰撞体并停用；`-1` 无限。不是储存类效果，计数与加值在同一回调完成。
- 只接受 `Impact` 存在且不是连线的事件。持续贴住不会重复触发，Trigger/连线接触不计数。状态文字显示当前连击数与剩余时间，各实例不共享计数。

核心组合如下，完整脚本包含超时和跨回合复位：

```lua
if api.Impact == nil or api.LinkKey ~= 0 then return end
if not api:TryUse() then return end
combo = combo + 1
endsAt = api.Time + math.max(api.Strength, api.Range / math.sqrt(combo))
api:AddBase("BALL_BASE_VALUE", combo * api.Parameter)
```

`Authoring/Data/Sandbag.device.json`：`Value = 1` 是每连击增加的基础价值，`Range = 3` 是初始窗口秒数，`Strength = 0.3` 是最短窗口秒数；此装置对这两个通用参数采用时间含义，不代表空间范围或物理力度，三者必须为正才触发。元数据默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

`Editor/Validation/GameContentValidation.Sandbag.cs` 随统一运行时验证执行，覆盖首击/多球/重复命中、同一步多球、精确超时及最短窗口、实例/回合隔离、耐久 20/21 与无限、基础加值与倍率/正常结算、真实圆柱碰撞和持续贴住不重复触发。

## 矿老板：减值换金币，每座矿每回合独立限产

制作源 `MResPacker/Authoring/Lua/GoldMine.lua`，配置 `Authoring/Profiles/GoldMine.asset`，输出 `MResPacker/Output/GoldMine.mres`，运行时包 `Resources/Devices/9010_GoldMine.mres`。模型为直径 0.5、高 0.2 的石质圆柱，带金色矿脉和顶部金块装饰；实体凸 MeshCollider 负责正常碰撞。

- 每次真实撞击给当前弹珠基础价值减 2，最低降到 0，同时立即给玩家加 4 金币。价值为 0 或不足 2 也会产出完整 4 金币。球保留，正常反弹，之后以剩余价值结算。
- 每座矿独立累计本回合产出的金币，默认最多 20，即成功触发 5 次。达到上限后的撞击不减值、不加金币、不扣耐久，仍保留实体碰撞。新回合恢复产出额度；卖掉或移除后随实例清理 Lua 状态。
- `OnContact` 先检查 `Impact`、连线和剩余额度，再组合 `Visit(0, 0)`、`TryUse()`、`AddBase`、`AddCoins`。同球同一步去重，不使用装置公共冷却；不同球同一步撞击按接触顺序共享这座矿的剩余额度。持续贴住不反复触发。
- 每次实际产出扣 1 耐久，最后一点仍完整生效；`-1` 无限耐久也遵守每回合 20 金币上限。`OnTick` 与接触回调按 `RoundId` 重置产量，状态显示“本回合 n/20 金币”，采完后提示“已采完”。
- 复用现有属性 API：`AddBase("BALL_BASE_VALUE", -2)` 修改未修正的基础值，再计算现有属性修正，与沙包一致。例如基础 10、价值倍率 ×2 时，采矿后基础 8、当前值 16；移除倍率后仍为 8。不会把已有倍率写回基础值。`AddCoins(4)` 直接增加玩家金币，不是回合得分，也不套用兑现时的金币收益倍率；遵守通用钱包上限。

`Authoring/Data/GoldMine.device.json`：`Value = 2` 为每次基础价值扣除量，`Strength = 4` 为单次金币产量，`Range = 20` 为每回合产出上限；后两项按整数取整，非正产量/额度不生效，负扣除量不生效。自定义上限不是产量整数倍时，最后一次只发剩余额度，仍扣一次价值和耐久。这里的 `Range` 是金币额度，不是空间距离。Metadata 的 `value` 仍为单件出售价，与上述 `Value` 无关。

默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。没有新增专用引擎 API。`Editor/Validation/GameContentValidation.GoldMine.cs` 随统一验证执行，覆盖低值/零值、实例和回合隔离、同时碰撞、额度耗尽、最后一点/无限耐久、价值修正与正常结算，以及真实 PhysX 接触和持续贴住去重。

## 淘金热：单球运输十秒，随机返还或消耗

制作源 `MResPacker/Authoring/Lua/GoldRush.lua`，配置 `Authoring/Profiles/GoldRush.asset`，输出 `MResPacker/Output/GoldRush.mres`，运行时包 `Resources/Devices/9011_GoldRush.mres`。外观为木支架人工矿洞、轨道和洞口停放的矿车，入口为 BoxCollider 触发器，放置半径 0.6。

1. 空闲时接收一颗弹珠，组合 `Visit(0, 0)`、`Motion:TryHold(10)` 和 `Motion:TrySetHeldVisible(false)` 开始运输。等待期间不参与物理碰撞或结算；同一矿洞拒绝其他球，入库不扣耐久、不抽随机数。多球同一步进入也只收第一颗，其他球正常经过。
2. 等待按模拟时间计算，满 10 秒后的首个固定步进入 `OnHoldEnd`。用新的上下文调用一次 `RandomRange`，三种结果各占 1/3：返还原弹珠并加 2 金币；消耗弹珠并加 5 金币；消耗弹珠且没有金币。后两种使用 `TryConsume(false)`，不计分、不返还发射血量。
3. 返还保留弹珠原价值、属性和进入时的速度大小，用 `TryLaunch(source, source.Down, speed)` 沿棋盘下坡方向出射；受阻则用 `TrySetVelocity` 在原来的安全位置向下恢复运动。两种方式均遵守当前限速。引擎在回调前恢复显示与角速度。
4. 三种结果都在完成时以 `TryUseOnce("trip:" .. tripNumber)` 扣 1 耐久，包括一无所获。一次结果只扣一次，最后一点也完整生效；`-1` 无限。结果出来后恢复接收，返回的同一颗球以后再次进入也能开始新的运输。

Lua 只跨回调保存本回合 ID、球 ID、截止时间、批次编号和显示文字。各库存实例独立；状态显示运输倒计时及最近结果。弹珠消失/到期、装置移除/收回/视图消失或新回合取消运输，不抽结果、不补金币、不扣耐久；寿命和回合排空计时仍正常推进，完成与寿命/排空超时同一步时以清理优先。无球时也通过 `OnTick` 清理失效的忙碌标记。暂停不推进运输时间。

参数 `Authoring/Data/GoldRush.device.json`：`Value = 10` 为运输秒数，范围 `(0, 60]`；`Strength = 2` 为返还弹珠时的金币数；`Range = 5` 为仅金币结果的金币数，后两项取非负整数。概率权重在 Lua 顶部 `returnWeight, coinsWeight, lossWeight = 1, 1, 1`，可编辑为非负权重且总和必须为正。这里的 `Range` 为金币参数，不代表空间距离。Metadata 默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

`Editor/Validation/GoldRushDeviceValidation.cs` 随统一验证执行，覆盖固定随机种子的全部结果、十秒边界、容量/实例隔离、重复运输、最后一次/无限耐久、原球价值/速度、受阻回退、显示所有权及恢复、取消/寿命/回合/对象池清理和真实洞口触发器。

## 通用回血与造血工厂（已实现）

`RestoreBlood(amount)` 操作的是发射弹珠消耗的玩家血量，直接进入运行时状态与血量快照；控制器在物理步结束后发布血量变化事件。数量由 Lua 计算，引擎只负责有限值校验、容量限制和运行状态。弹珠回调实际回复大于 0 时会记录这台装置的有效作用；没有当前球的装置事件不记录某颗球的触发历史。

血量耗尽时仍在场的球可以回血；回复量足够支付最低档一次发射（包括发射颗数）时，自动从等待清场恢复到可发射状态，仍需玩家重新发射，不自动补发旧输入。主动提现后不会重新开放发射。回合已结束或结算已完成时回复返回 0。此 API 不占用或重置复活返还的家族记录。

造血工厂制作源 `MResPacker/Authoring/Lua/BloodFactory.lua`，配置 `Authoring/Profiles/BloodFactory.asset`，成品 `Output/BloodFactory.mres`，运行时 `Resources/Devices/9019_BloodFactory.mres`。模型为带浅色箍带、烟囱和阀门的红色圆柱储罐，实体碰撞。

```lua
local remaining = math.floor(api.Value * api.Parameter) -- 默认 0.5
api:SetCurrent("BALL_BASE_VALUE", remaining)
local restored = api:RestoreBlood(api.Value * api.Strength) -- 默认 5
```

- 仅实际碰撞触发。当前基础价值减半向下取整，按**减半后的价值 ×5** 回复血量，不计尚未结算的装置倍率。例如 11 → 5、回复 25；5 → 2、回复 10；1 → 0、回复 0。弹珠保留并正常反弹，同一颗球之后再撞可继续处理。
- 每次对正价值球成功处理扣 1 耐久；同一碰撞的重复通知去重，持续贴住不逐帧扣。已经为 0 的球不触发、不扣耐久；满血仍按碰撞规则减半价值并扣耐久，超出上限的回血舍弃。最后一次完整处理，`-1` 无限耐久。
- 倍率继续按 `1 + Σ(倍率 - 1)` 累计，剩余价值在得分区统一结算。若这颗球已有基础价值属性修正，`SetCurrent` 将修改后的当前价值作为本次输入并替换结果，清除该属性的旧修正，后续过期不会把消耗掉的价值恢复；新效果仍可再次增值。普通 `Set` / `AddBase` 继续保留修正，语义不变。
- `Authoring/Data/BloodFactory.device.json`：`Value = 0.5` 为保留比例（须在 0 与 1 之间），`Strength = 5` 为每点剩余价值回复的血量；减半和取整在 Lua 中。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

验证入口 `Editor/Validation/GameContentValidation.BloodFactory.cs`：奇数/小数/零价值、满血截断、倍率与属性修正、重复碰撞、耐久/跨回合、血量耗尽后恢复发射、提现不恢复发射、通用 API 有效期，以及两种固定步长下的真实倾斜棋盘碰撞。

## 通用按价值分出 `DevicePortion`

`api:CreatePortion(value)` 从当前弹珠向新球转移指定价值。`value` 必须是大于 0 的有限数；构造时不生成球、不修改价值，不会扣耐久。请求及其全部属性只在创建它的回调期间有效，不能保存到下一次计时回调。

| 成员 | 契约 |
| --- | --- |
| `Value` / `Radius` / `Mass` | 请求分出的价值、世界半径和质量。默认尺寸/质量沿用原球当前值。 |
| `TrySetSize(worldRadius, mass)` | 单独配置新球的半径和质量，必须符合属性上下限；无效或已提交返回 `false`。不缩放原球，也不自动按价值比例计算体积/质量。 |
| `CanSplit` | 原球存活、未滞留、价值足够、来源有效，且分裂代数、容量与回合状态允许。空间检查在提交时执行。 |
| `TrySpawn(position, velocity)` | 按明确的世界位置和速度生成一颗球，允许零速；受当前最大速度限制。成功才扣原球价值，失败回滚候选球，保留全部未发射价值。请求仅能提交一次。全部价值转出时消耗原球，并按替换计算球数容量；部分转出时原球继续保留。 |

子球继承累计倍率、访问与有效作用历史、复制/分裂祖先、家族、剩余寿命及临时重力截止时间；分裂代数增加 1，来源加入 `HasSplitFrom` 祖先列表。原球的代数不变，可继续分出其余价值。容量、`MaxSplitGeneration` 和回合排空时限仍生效，不收取发射血量，也不增加家族返还次数。

固定 `ScoreBonus` / `ScoreAdd` 按本次价值占当前剩余价值的比例转移，原球同步减去这部分，避免每颗小球重复获得整份加分。指定新价值、尺寸与质量不会再次套用这些属性的旧修正；原球已分出的价值也不会在下次属性计算时重新出现，其他属性的修正及其到期时间保持。若需要守恒体积/质量，由 Lua 显式计算尺寸或使用 `CreateSplitBatch`。

这里不包含“每颗 1 价值”、射速、扫射角度、舍弃余数或下一步动作。Lua 可以在每次 `OnHoldEnd` 重新创建请求，成功转出后重新滞留；最后一颗完成再用 `TryUseOnce(key)` 计数。取消或异常恢复的原球只保留尚未转出的价值。

## 弹珠机关枪（已实现）

Lua 为 `MResPacker/Authoring/Lua/MarbleMachineGun.lua`，配置 `Authoring/Profiles/MarbleMachineGun.asset`，成品 `Output/MarbleMachineGun.mres`，运行时 `Resources/Devices/9020_MarbleMachineGun.mres`。模型包含弹仓、进球口和扫射炮管；只有入口触发器，没有实体阻挡。

- 按当前基础价值分成每颗价值 1 的球，继承累计得分倍率。例如 **5.5 → 5 颗价值 1**，余数 0.5 在最后一颗成功出射后舍弃；整颗价值不足 1 时直接穿过，不存储、不扣耐久。
- 一次储存一颗原球，默认每 0.1 秒发射一颗、速度 6，朝棋盘下方 **±22.5°，总宽 45°** 扇形来回扫射，完整往返 1 秒。炮管表现与出射方向一致，旋转和计时随模拟暂停。
- 小球世界半径 0.04、质量 0.1，当前制作参数采用固定尺寸，不按份数守恒体积/质量。球价值与固定加分按份额转移，倍率仍在得分区统一计算。
- 存储和中途出射不扣耐久，**整批完成扣 1**；最后一点耐久能完成整批。出口被挡或球数暂满时保留未发射价值，按正常间隔重试，不卡着连发补偿；忙碌时其他球直接穿过。
- 自己产出的球及其复制/分裂后代回到同一台时直接穿过，不再装填。其他实例可处理符合通用分裂代数条件的球。
- 沿用原球剩余寿命，存储不暂停寿命。装置移除或视图消失时，剩余原球恢复可见和进入速度；寿命结束、回合结束或排空超时按现有生命周期清理未发射部分。未完成批次不扣耐久。

`Authoring/Data/MarbleMachineGun.device.json`：`Value = 0.1` 是发射间隔，`Strength = 6` 是速度，`Range = 45` 是完整扇形宽度。Lua 顶部 `radius = 0.04`、`mass = 0.1`、`period = 1` 控制小球尺寸与扫射往返时间。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

装置事件新增只读 `Down`，返回棋盘平面内的世界下坡方向；水平棋盘时回退到当前装置朝向。Lua 组合 `TrySetYaw(0)`、`Down`、`Vectors:Rotate` / `SignedAngle` 和 `TrySetYaw` 计算表现朝向，避免视图重建时累计旋转。分出后重新隐藏原球仍使用既有 `TryHold` 与 `TrySetHeldVisible`，不跨回调保存 API 对象。

验证见 `Editor/Validation/GameContentValidation.MarbleMachineGun.cs`：每颗价值、余数、分数守恒、节奏/扫射方向、容量与受阻重试、完成时耐久、祖先排除、取消/到期恢复，以及真实倾斜棋盘下两种固定步长的连续发射与穿过。

## 通用复制 `DeviceCopy`

`api:CreateCopy()` 创建一次性请求，对象只在当前弹珠回调内有效。C# 不决定复制球的价值、出射方向或装置每球次数；原球始终保留。新接口与旧 `Children(count, false)` 的固定角度出射分开。

| 成员 | 契约 |
| --- | --- |
| `Value` / `Radius` | 待复制的基础价值、世界半径。未覆盖价值时读取原球当前属性。 |
| `CanCopy` | 检查请求未提交、原球存活且未滞留、来源装置有效、回合/容量及复制代数允许。空间是否受阻要在提交时检查。 |
| `TrySetValue(value)` | 设置副本基础价值，并清除副本继承的基础价值属性修正、固定 `ScoreBonus` 和 `ScoreAdd`，保留倍率；这样设为 0 就不会带入旧固定加分。非法数值、超出属性范围或已提交时返回 `false`。不修改原球，也不创建球。 |
| `TrySetDeviceScoreMultiplier(amount)` | 设置副本的完整累计装置倍率，接受 0–1000 的有限数；内部保存 `amount - 1`，覆盖副本继承的装置贡献，保留独立基础倍率及其他属性。不修改原球、不生成球、不扣耐久。非法数值或请求已提交时返回 `false`；未调用时照常继承。平均、混合等公式由 Lua 计算。 |
| `TrySpawn(position, velocity)` | 以明确的世界位置和速度提交一次复制；允许零速，遵守当前最大速度。成功返回 `true`，失败清理候选实体/物理球并保留原球。请求无论成功失败均不可重复提交；后续需要重试时新建请求。不隐式扣耐久。 |

副本继承尺寸、质量、当前属性及其尚未到期的修正、累计倍率、Rush、临时重力的原截止时间、家族和剩余寿命；不会重启计时、额外扣血或多返还家族血量。默认未调用 `TrySetValue` 时也继承价值和固定加分；未调用 `TrySetDeviceScoreMultiplier` 时继承累计装置倍率。普通旋转沿用原球姿态，新物理球不复制自转速度。

副本的普通访问次数从空记录开始，允许触发原球之前碰过的其他装置；通过 `Visit(key, maximum, true)` 明确标记的记录保留次数和最后访问步，防止已继承效果重复施加。有效效果历史及复制/等分祖先记录继续继承。`SpawnSourceId` 为本次来源实例。副本的 `ReplicationGeneration` 增加 1，沿用内容规则 `MaxSplitGeneration` 的复制代数上限（当前 2），不增加普通等分代数 `Generation`；同时遵守在场数量上限。

```lua
local request = api:CreateCopy()
if request.CanCopy and request:TrySetValue(0) then
    if request:TrySpawn(outputPosition, outputVelocity) then
        api:TryUse() -- 复制成功才登记本次耐久
    end
end
```

## 草皮（已实现）

制作源 `MResPacker/Authoring/Lua/Turf.lua`，配置 `Authoring/Profiles/Turf.asset`，成品 `Output/Turf.mres`，运行时 `Resources/Devices/9021_Turf.mres`。模型为棕色圆柱土层、绿色草冠与草叶，土层有实体凸碰撞体。

- 首次真实碰撞只记录球 ID、碰撞时的当前基础价值、累计装置倍率和截止时间；5 秒内另一颗不同弹珠碰撞，生成一颗新球。恰好 5 秒仍可配对，超过截止时间清空记录，下一次有效碰撞重新开始。
- 新球价值为 `(首次价值 + 当前价值) / 2`，保留小数；累计装置倍率为两次快照倍率的平均值。例如价值 4 和 10、倍率 2 和 4，生成价值 7、倍率 3 的球，到得分区结算。原球的价值、倍率和正常反弹均不改变。
- Lua 组合 `CreateCopy()`、`TrySetValue()`、`TrySetDeviceScoreMultiplier()` 和 `TrySpawn()`，新球从棋盘下方以速度 6 发出。尺寸、质量、其他属性和剩余寿命沿用第二颗球；不额外抽血，不复制旧固定加分。遵守通用复制代数及在场数量上限。
- 同球重复碰撞不配对、不延长窗口；同一步不同球可组成一对，第三颗开始下一对。成功生成后立即重置并扣 1 耐久，第二颗不会同时成为下一对的第一颗。存入、超时、无效碰撞、失败不扣；最后一点耐久仍完整生成，`-1` 无限。
- 出口阻挡、球数或代数限制导致生成失败时，保留首次快照，等待原窗口内下一次不同球的有效碰撞，不自动逐帧出球。超时照常清空。首次球消失不影响已经保存的数值；各实例独立，每回合清空状态，不保存过期上下文或球对象。
- `HasCopiedFrom(api.InstanceId)` 排除本实例产生的球及其复制/分裂后代。它们仍会物理反弹，但不占配对位置、不扣耐久，可以触发其他装置。

`Authoring/Data/Turf.device.json`：ID 9021，`Value = 5` 是配对窗口秒数，`Strength = 6` 是出射速度。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Turf Device` 定位制作配置。

`Editor/Validation/GameContentValidation.Turf.cs` 覆盖价值/倍率快照与平均、小数/零价值、5 秒边界、同球与同帧去重、重置、祖先过滤、独立实例、容量/物理失败重试、有限/无限耐久、球消失/跨回合、接口有效期，以及两种步长下真实倾斜棋盘的碰撞、反弹、出口阻挡和向下出射。

## 复制体（已实现）

制作源 `MResPacker/Authoring/Lua/Duplicator.lua`，配置 `Authoring/Profiles/Duplicator.asset`，成品 `Output/Duplicator.mres`，运行时 `Resources/Devices/9018_Duplicator.mres`。模型为浅蓝金属圆柱，使用实体碰撞。

- 原球保留并由物理系统正常反弹。Lua 用碰撞前速度和表面法线，在棋盘平面内计算镜面反射方向；副本按该方向、以碰撞前速度大小射出，尺寸和质量不变。
- 副本初始价值为 0，旧固定加分清空；继承累计倍率，之后可以被其他装置增值、吞噬或触发其效果，到得分区统一结算。
- 每个实例对每颗原球成功复制一次，自己的复制后代（包括再分裂后的后代）不再触发它。这里不触发指不再复制；圆柱仍有实体碰撞，球会正常反弹。
- 成功复制扣 1 耐久，阻挡、数量/代数限制及无效碰撞不扣；失败后原球下次碰撞仍可重试。复制球放在原球反射方向前方，留出两球半径及少量间隔，避免重叠。
- 默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。`Authoring/Data/Duplicator.device.json` 的 ID 为 9018；价值 0 的选择和每球一次规则均在 Lua 中。

`Editor/Validation/GameContentValidation.Duplicator.cs` 验证零价值及固定加分清理、其他装置增值、祖先记录、重试/容量/耐久、回调有效期，以及两种物理步长下的真实倾斜棋盘正撞和斜撞。

## 通用等分 `DeviceSplitBatch`

弹珠回调调用 `api:CreateSplitBatch(count)`；数量为 2–256 的整数。批对象只在当前回调有效，不可跨回调保存。它只负责把原球等分，不包含三出口、向下方向、角度、耐久计数或具体装置规则。

| 接口 | 规则 |
| --- | --- |
| `CanSplit` | 当前是否满足等分前提：原球仍在飞行且未滞留、装置有效、容量和分裂代数足够、均分后属性合法；还不检查物理出口。 |
| `TargetCount` / `Count` | 目标数量 / 已添加的出口数量。 |
| `Radius` / `Mass` / `Value` | 每颗子球的世界半径、质量、当前基础价值。价值与质量除以数量，半径乘数量倒数的立方根，体积总量不变。 |
| `TryAdd(position, velocity)` | 添加一个世界位置和速度，不创建实体。允许零速度，拒绝非有限值、过量出口和子球互相重叠；速度遵守继承的速度上限。 |
| `TryCommit()` | 一次性提交；必须恰好提供目标数量的出口。全部成功才消耗原球且不返血；任一出口受阻或创建失败，撤销全部子球并保留原球。每批只能提交一次。 |

容量按替换后的净数量检查，原球由本物理步末的通用回收清理；同一步中先前已消耗的原球不再占后续等分批次的存活容量。原球当前属性在创建批次时取快照；提交前属性变化则拒绝旧快照。子球当前属性作为新基础值，沿用旧替换分裂的快照语义，不复制属性修正列表；均分后低于质量/半径等属性下限时拒绝，避免钳制导致总量增大。沿用 `MaxSplitGeneration`（默认 2），失败不消耗代数或耐久。

子球继承累计装置倍率（`1 + Σ(amount - 1)`）、独立基础倍率、Rush 状态、通用访问记录、触发历史、原寿命和家族。加分量 `ScoreBonus` 与 `ScoreAdd` 按数量均分，倍率修正保持原值和到期时间；当前仍有效的限时重力保持截止时间。子球具有独立球身份，零初始角速度；不会重新抽血，同家族返血仍最多一次。区域力按出生位置重新计算。

`SpawnSourceId` 改为本次等分的装置实例 ID；独立的祖先列表保留此前全部等分来源，并加入当前实例。用 `HasSplitFrom` 可避免后代绕经其他分流器后回到祖先再次分流。引擎登记本次成功效果的触发历史；Lua 在成功提交后组合 `TryUse()` 登记一次耐久。

## 分流器（已实现）

制作源 `MResPacker/Authoring/Lua/Splitter.lua`，配置 `Authoring/Profiles/Splitter.asset`，成品 `Output/Splitter.mres`，运行时 `Resources/Devices/9015_Splitter.mres`。模型为圆柱集流仓与三个管口，只有一个 BoxCollider Trigger，所有管口均无实体碰撞。

- 原球进入后，Lua 创建 3 球等分批次；每颗价值、体积和质量各为原球 1/3，继承累计装置倍率。
- Lua 沿棋盘向下及左右各 30° 设置出口，保留进入速度大小；原球静止时输出也为零速。出生距离同时满足装置外部及子球间距要求。
- 完整分流成功扣 1 耐久，第 20 次仍生成全部 3 球；失败保留原球，不扣耐久，下次进入可重试。
- 分出的球及其后代再次经过同一台时**直接穿过**，不反弹、不改速度、不再分裂、不扣耐久。其他分流器实例可再次分流，但仍受通用代数、属性下限和数量上限约束。

`Authoring/Data/Splitter.device.json`：`Range = 30` 为侧口相对向下的角度，支持 1–80°；`Value` / `Strength` 未使用。修改侧口角度时同步调整制作 Prefab 的管口朝向。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

行为验证位于 `Editor/Validation/GameContentValidation.Splitter.cs`，覆盖均分守恒、得分结算、同实例后代穿过、其他实例分流、复制继承、失败回滚、容量、耐久、回合恢复、零速，以及真实倾斜棋盘的三出口与物理阻挡。

## 大喷菇（已实现）

制作源 `MResPacker/Authoring/Lua/PuffMushroom.lua`，配置 `Authoring/Profiles/PuffMushroom.asset`，成品 `Output/PuffMushroom.mres`，运行时 `Resources/Devices/9016_PuffMushroom.mres`。紫色蘑菇模型带八个扇形喷口，根 BoxCollider Trigger 检测进入，模型和喷口均无实体碰撞。

1. Lua 使用 `CreateSplitBatch(8)`，把原球价值、体积和质量各均分成八份；每颗半径为原球一半，继承累计装置倍率，到得分区才结算。
2. 以 `source.Down * -1` 得到**棋盘平面内向上**的方向，不是离开棋盘的法向 `source.Up`。八个方向覆盖总计 120°，角度从 −60° 到 +60°，相邻间隔 `120 / 7` 度。
3. `source:Rotate` 计算每个方向，`batch:TryAdd` 设置出生位置与入射速度大小；零速仍输出零速。出生距离为 `max(装置半径 + 子球半径 + 0.02, (子球直径 + 0.02) / (2 × sin(相邻夹角 / 2)))`，避免八球重叠。
4. 全部出口成功后才消耗原球，并用 `TryUse()` 扣 1 耐久；第 20 次仍完成全部八球。任一出口受阻时撤销整批，原球保留、不扣耐久，下次进入可重试。
5. `HasSplitFrom(api.InstanceId)` 排除本实例产生的球及后代，再次经过时直接穿过，不反弹、不改变速度、不再分裂、不扣耐久。其他实例仍可分流，受通用属性下限、代数和数量上限约束。

`Authoring/Data/PuffMushroom.device.json` 的 `Range = 60` 表示半扇形角，即两侧各 60°、总计 120°；支持半角 1–90°，符合通用参数上限。`Value` / `Strength` 未使用，出口数量固定为 Lua 顶部 `outlets = 8`。修改扇形角或数量时同步调整制作 Prefab 的喷口。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

现有质量下限为 0.1，因此八等分要求原球质量至少 0.8；默认质量 1 的弹珠可以触发，刚八等分出的默认小球（质量 0.125）不能继续八等分。沿用 `DeviceSplitBatch` 的拒绝规则，失败保留原球和耐久，不强行钳制子球质量。未新增专用引擎 API。

`Editor/Validation/GameContentValidation.PuffMushroom.cs` 验证八等分与计分守恒、120° 上方扇形、可调夹角、来源继承、失败回滚、容量与耐久，以及真实倾斜棋盘上的阻挡、零速/非零速出射和后代穿过。

## 通用计时视觉对象与范围回调

以下能力由 `DeviceEventContext` 提供，不需要当前弹珠。C# 只负责移动、计时、生命周期和查询，不含炸弹名称、投掷周期、爆炸半径或分裂规则。

| API | 规则 |
| --- | --- |
| `RandomRange(minimum, maximum)` | 与弹珠上下文相同，使用整局统一随机序列，返回 `[minimum, maximum)`；等值不推进随机状态，非法边界抛异常。 |
| `TrySpawnVisual(nodePath, position, velocity, seconds, callback)` | 从当前 MRes 根节点下的指定视觉模板复制 Mesh/Material/Transform，以世界坐标匀速移动，返回正整数 ID；失败返回 0。零速度可作定时闪光；时长须大于 0 且不超过 60 秒。 |
| `Transient` | 仅视觉完成回调非空，含只读 `Id`、`Position`、`Velocity`、`Duration`。Position 是精确寿命终点，完成后此对象也随回调过期。 |
| `ForEachBallInRadius(center, radius, callback)` | 冻结与世界球形区域相交的在场弹珠列表，按出生顺序，为每颗调用指定 Lua 函数并传入新的 `DeviceContactContext`；返回正常完成的回调数。半径 0–100，包含球自身半径；跳过滞留、已结算、待回收和寿命已到的球。 |

视觉模板可以是隐藏节点。复制仅包含网格、共享材质和层级变换，不复制碰撞体、刚体、Lua 组件或其他行为；视觉对象不占弹珠数量、不触发装置、不计分。路径必须存在且包含网格，需物理适配器提供 `IDeviceTransientVisuals`。同一模拟最多 256 个活动视觉对象；重建或丢失源视图时取消，不把它们当物理弹道处理，运动不受重力、力场或障碍影响。

移动时间随模拟步推进，暂停不推进；完成回调在物理读回和接触处理后、逐球更新前执行。先移除到期视觉对象，再调用 Lua，空回调或不存在的函数只清理。每次回调都会创建新的有效上下文，异常不会遗留该对象或阻止其他到期对象处理。装置移除/收回、源节点失效、回合结束/重开、Drain 超时和模拟释放均取消对象且不执行完成效果。创建与取消都不隐式扣耐久。

范围回调只访问查询开始时的快照，新生成的子球不会进入同次查询；一颗的 Lua 异常不阻止其他球。查询本身不扣耐久、不登记效果，也不冒充物理碰撞。区域内各颗球的行为由 Lua 决定，可组合修改属性、运动、等分等 API。多个球共享一次效果时，外层 Lua 先登记一次耐久；最后一点耐久可通过 `TryKeepActive(seconds)` 让整次查询和视觉尾效完成。只能跨回调保存普通数字/字符串/向量，不保存 API、Transient 或 Motion 对象。

## 炸弹（已实现）

制作源 `MResPacker/Authoring/Lua/Bomb.lua`，配置 `Authoring/Profiles/Bomb.asset`，成品 `Output/Bomb.mres`，运行时 `Resources/Devices/9017_Bomb.mres`。模型包含固定投掷底座、隐藏的黑色圆球引信模板 `BombTemplate` 和爆炸闪光模板 `BlastTemplate`。底座有实体碰撞，飞出的炸弹和闪光无碰撞。

- 每回合首次等待 5 秒，此后每 5 秒随机选择棋盘平面 0–360° 方向。以速度 3 投出，1 秒后在准确终点爆炸；暂停冻结投掷及引信时间。
- Lua 的 `OnFuseEnd` 先扣 **1 次耐久，空爆也扣**，然后查询爆点半径 0.8 内的弹珠。投出不扣，取消不扣，最后一次爆炸仍完整处理全部目标。
- `OnBlastBall` 为每颗符合条件的原球创建 `CreateSplitBatch(8)`，价值、体积和质量各均分为八份，继承各自累计装置倍率。围绕原球在棋盘平面每隔 45° 布置一个出口，朝四周以速度 6 散开，遵守子球速度上限；倍率到得分区结算。
- 同一装置产生的后代不再被它分裂；这一爆仍扣耐久。沿用分裂代数、质量/半径下限和容量限制，默认八等分至少需要原球质量 0.8。某颗原球八等分受阻时整颗回滚保留，继续处理其他原球；并非整个范围共同回滚，失败或全不符合条件仍算一次爆炸。
- 范围查询取球形相交和原球快照，不重复选中新碎片；已滞留的球不受影响。爆炸不提前结算，不扣血或生成额外总价值。

`Authoring/Data/Bomb.device.json`：`Value = 5` 为投掷间隔，支持 0.1–60 秒；`Strength = 3` 为投掷速度，`Range = 0.8` 为爆炸半径。Lua 顶部 `flightSeconds = 1`、`scatterSpeed = 6`、`pieces = 8` 控制引信、散射速度和份数。修改爆炸半径时同步调整 `BlastTemplate` 的显示大小。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

`Editor/Validation/GameContentValidation.Bomb.cs` 覆盖周期/引信、空爆扣除、最终一次处理多球、随机方向、价值/倍率守恒、失败与容量回滚、来源过滤、取消/回合清理、新回调有效期及异常隔离，以及真实倾斜棋盘的飞行视觉和物理碎片。

## 通用批量生成 `DeviceSpawnBatch`

弹珠回调和装置事件都可调用 `api:CreateSpawnBatch()`。返回对象只在本次回调有效；它先保存普通生成请求，直到 `TryCommit()` 才创建实体与物理球。引擎不包含环形、数量 16、蓄力 8 次或特定装置名称。

| API | 契约 |
| --- | --- |
| `Radius` | 此批新球的世界半径，取当前内容配置中 `BALL_RADIUS` 的默认值并换算。用于 Lua 计算位置间隔。 |
| `Count` | 已接纳的请求数量。每批最多 256 颗，同时受会话总弹珠上限约束。 |
| `TryAdd(position, velocity, value)` | 添加世界位置、完整世界速度、基础价值；无实际生成。速度要求有限且非零，价值有限且非负。拒绝批内距离小于两球半径加 0.001 的重叠请求；失败不加入队列。 |
| `TryCommit()` | 一次性提交所有请求；空批、重复提交、装置未放置/已移除/无物理视图、非运行阶段、尚无有效固定步长、排空超时或超出总上限返回 `false`。物理生成检查实体阻挡，任一球失败则移除本批此前创建的全部 ECS/物理球并恢复数量；异常也执行清理后传播。只允许提交一次，重试请在新回调创建新批。 |

新球是独立普通球：基础价值采用请求值并遵守属性上下限，半径、质量、弹性、重力、寿命、速度上限等采用内容配置的默认值；不复制接触球或玩家当前倍率/滞留状态。出射速度遵守新球速度上限。每球获得独立身份与寿命、零初始角速度、`SpawnSourceId = 当前实例 ID`，复制/分裂后的后代继续保留该来源信息。

生成不扣玩家血量，不改当前弹珠，不自动扣耐久；新球的投入血量与返还额度为 0。Lua 应检查需要的耐久，在整批成功后用 `TryUse` 登记这次效果。失败不会留下半批、扣血或计分；已分配的身份序号不倒退，避免复用旧 ID。正常回合/会话清理、寿命、计分和对象池规则继续生效。

示例（任意两点布局，无需存在当前弹珠）：

```lua
if api.RemainingDurability == 0 then return end
local batch = api:CreateSpawnBatch()
local position = api.Position + api.Up * (batch.Radius + 0.005)
local distance = api.Radius + batch.Radius + 0.02
if not batch:TryAdd(position + api.Forward * distance, api.Forward * 6, 1) then return end
if not batch:TryAdd(position - api.Forward * distance, -api.Forward * 6, 1) then return end
if batch:TryCommit() then api:TryUse() end
```

## 大石碎胸口：八次蓄力、环形发射、冷却后重启

制作源 `MResPacker/Authoring/Lua/StoneBurst.lua`，配置 `Authoring/Profiles/StoneBurst.asset`，输出 `MResPacker/Output/StoneBurst.mres`，运行时包 `Resources/Devices/9009_StoneBurst.mres`。灰色圆柱配橙色环带，直径 0.5、高 0.2，实体凸 MeshCollider；撞击球始终由正常物理反弹，Lua 不吞噬或改写其运动。

1. 每次真实碰撞进入蓄力 +1；同一步不同球各算一次，同球重复报告通过 `Visit(0, 0)` 去重。持续贴住、Trigger 和连线接触不蓄力。
2. 第 8 次自动生成 16 颗新球，各自基础价值 1、速度 6、不扣玩家血量。Lua 围绕棋盘法向按 `i × 360 / 16` 旋转方向，在棋盘平面均匀发射，间隔 22.5°，不依赖世界水平面。
3. Lua 计算出生圆半径 `max(装置半径 + 球半径 + 0.02, (球直径 + 0.02) / (2 × sin(π / 16)))`，保证相邻出生球留出空间，出生高度为球半径加 0.005。
4. 整批成功生成后扣 1 耐久、清空蓄力并开始 5 秒冷却；冷却期间碰撞仍物理反弹，不增加蓄力。截止后从 0 重新计数，暂停不推进冷却。第 20 轮仍完整发射，之后本回合停用；`-1` 无限。
5. 出口受阻或达到弹珠上限时保留满蓄力，每 0.1 模拟秒尝试新批，不扣耐久、不提前进入冷却，也不要求第 9 击。装置移除后不再重试，新回合清空蓄力与冷却。
6. Lua 比较 `SpawnSourceId` 与 `InstanceId`，自身发出的球及其复制/分裂后代不会为自己蓄力；它们仍可影响其他装置。各装置独立保存普通 Lua 状态，状态文字显示蓄力进度、等待发射或冷却倒计时。

`Authoring/Data/StoneBurst.device.json`：`Value = 8` 是蓄力次数（取整数），`Strength = 6` 是速度，`Range = 5` 是冷却秒数。Lua 顶部 `projectileCount = 16`、`projectileValue = 1` 控制数量与价值。元数据普通、耐久 20、购买价 5、出售价 120、初始库存 0。效果只组合碰撞数据、访问记录、计时、`Vectors.Rotate`、批量生成和耐久登记。

验证入口为统一运行时验证中的 `GameContentValidation.StoneBurst.cs`：真实碰撞、倾斜棋盘均匀出射、正常反弹与球价值、8 次/冷却边界、同一步多球、来源过滤、容量/物理生成中途失败回滚、自动重试、最后一点与无限耐久、新回合清理，以及批对象有效期。

## 弹弹：镜面反射方向附近随机弹出

制作源文件为 `MResPacker/Authoring/Lua/Reflector.lua`，打包配置为 `Authoring/Profiles/Reflector.asset`，成品为 `MResPacker/Output/Reflector.mres`，游戏副本为 `Resources/Devices/9002_Reflector.mres`。圆柱使用实体凸 MeshCollider；真实碰撞时获得表面法线，并保留求解器反弹前的速度快照。

Lua 分别组合碰撞数据、向量计算、随机数和速度设置，没有引擎侧“随机反弹装置”分支：

```lua
function OnContact(api)
    local hit = api.Impact
    if hit == nil then return end
    local motion = api.Motion
    local source = motion.Source
    local vectors = api.Vectors
    local incoming = vectors:ProjectOnPlane(hit.IncomingVelocity, source.Up)
    local normal = vectors:ProjectOnPlane(hit.Normal, source.Up)
    if incoming.sqrMagnitude < 0.000000000001 or normal.sqrMagnitude < 0.000000000001 then return end
    normal = vectors:Normalize(normal)
    if vectors:Dot(incoming, normal) >= 0 then return end

    local reflected = vectors:Normalize(vectors:Reflect(incoming, normal))
    local spread = math.min(90, math.max(0, api.Parameter))
    local angle = vectors:SignedAngle(normal, reflected, source.Up)
    local minimum = math.max(-spread, -90 - angle)
    local maximum = math.min(spread, 90 - angle)
    if minimum > maximum or not api:Trigger(0) then return end
    local offset = api:RandomRange(minimum, maximum)
    local direction = source:Rotate(reflected, offset)
    motion:TrySetVelocity(direction * hit.IncomingSpeed)
end
```

`game/device.json.Value = 30` 表示相对于镜面反射方向的 **±30° 半角范围**。Lua 把可配置半角限制在 `[0, 90]`；`0` 表示镜面反射。方向在装置所在棋盘平面内计算，出射速度大小取完整的 `IncomingSpeed`，仍受当前弹珠限速约束。只改变当前速度，不传送，也不主动修改角速度。

擦边时，Lua 将角度区间与“朝向表面外侧”的半平面取交集，再从有效区间取样，避免朝圆柱内部弹出。入射平面速度过小、顶部法线在平面上投影为零、正在离开表面或没有碰撞几何数据时，不改变普通物理结果，也不消耗触发次数/随机数。默认无装置额外冷却、每球触发次数不限，同一逻辑帧仍受通用去重规则约束。ID 9002，初始库存 0，购买价 5、出售价格 120。

### 碰撞快照 `api.Impact`

只在实体 Collider 碰撞产生的 `OnContact` 中提供；Trigger、连线、扫掠触发器、缺少碰撞几何的手动接触及 `OnHoldEnd` 中为 `nil`。数据来自发生接触的物理子步，之后修改弹珠运动不会改写快照。

| 属性 | 含义 |
| --- | --- |
| `Position` | 真实接触点的世界坐标，与弹珠中心位置不同。 |
| `Normal` | 从接触表面朝向弹珠的世界单位法线；采用该次碰撞第一个接触点。 |
| `IncomingVelocity` / `IncomingSpeed` | 碰撞所在物理子步开始时、求解器反弹前的世界速度快照及大小，不是 `Motion.Velocity` 的碰撞后速度。 |

快照不额外预测本子步内部的重力、场力或其他碰撞；多子步模拟会持续更新下一子步的入口速度。同一装置接触按现有物理桥接规则去重。无真实法线时引擎不会伪造圆心径向法线。

### 通用向量运算 `api.Vectors`

以下方法是独立纯计算，不改变弹珠运动、属性或随机状态。向量均为世界坐标，角度为度。非有限数值、过大导致长度平方溢出的向量和要求非零却提供零方向时抛出参数异常。

| 方法 | 含义 |
| --- | --- |
| `Normalize(vector)` | 转成单位向量；长度小于 `1e-6` 返回零向量。 |
| `ProjectOnPlane(vector, normal)` | 将向量投影到法线指定的平面，法线无需预先归一化。 |
| `Reflect(vector, normal)` | 按法线计算镜面反射向量，自动归一化法线，保留输入向量大小。 |
| `Dot(a, b)` | 点积，可判断向量朝向表面内侧还是外侧。 |
| `SignedAngle(from, to, axis)` | 返回 `[-180, 180]` 的有符号夹角，由旋转轴决定符号。两个方向和轴要求非零；用于平面角度时先投影到该平面。 |
| `Rotate(vector, axis, degrees)` | 绕任意世界轴旋转向量，轴自动归一化，保留向量长度。轴要求非零，角度有限；只计算向量，不改变位置或速度。 |

`RandomRange`、`Roll` 和装置事件上下文的 `RandomRange`，与初始装置抽取、商店生成/刷新、赌博开奖共用**整局唯一随机序列**。进入下一回合延续状态，不重置；商店刷新或任何装置多抽一次，都会推进后续随机结果。同样的种子和调用次序得到相同结果，不使用 Unity 全局随机数或 Lua `math.random`。

`MarbleGameController` Inspector 的「每次开新局刷新随机种子」默认开启，只在新开局时生成一个共享种子；关闭后使用 `GameBalance.json` 的 `Marble.RandomSeed`。装置 Lua 接口签名和脚本用法不变，现有 MRes 无需重新打包。非法上下界（NaN、无穷、下界大于上界或跨度溢出）抛出异常且不消耗随机状态。

`api.Impact`、`api.Vectors` 和 `RandomRange` 遵守当前回调有效期；保存 Impact 或 Vectors 后跨回调访问会报错。需要延迟使用某个入射量时，保存普通数值/向量副本。`DeviceEventContext` 同样提供全部 `Vectors` 纯运算，用于没有当前弹珠时计算生成布局等。

## 逐弹珠更新 `OnBallUpdate(api)`

只有声明该函数的装置才会接收事件。每个固定模拟步，物理读回、滞留到期和接触结算完成后，为每个已放置且有物理视图的装置逐个更新仍在飞行的弹珠。按装置实例 ID、弹珠出生序号排序。Lua 自行选择范围和条件，可组合现有运动、向量、属性和计时接口；不需要发生 Collider 接触，也不受弹珠是否带磁性限制。

- 传入新的 `DeviceContactContext`；`Hold` / `Impact` 均为 `nil`，`LinkKey = 0`。`Time`、`DeltaTime`、`RoundId`、`BallId`、`Radius` 和 `Range` 可用于模拟计时、按球保存普通数据和空间筛选。
- 已结算、待销毁、滞留中、寿命已到期的弹珠不更新；排空已超时也不更新。当前固定步已成功执行运动命令的弹珠跳过，避免覆盖刚完成的碰撞/释放效果；同时受多个装置影响时，较早成功操作的装置优先。
- 范围外仍会调用，便于 Lua 对先前进入的弹珠继续完成效果。没有全局自动吸引、自动计时或自动释放行为。
- 即时效果用 `Trigger` 或 `Visit` 与 `TryUse`；储存效果首球用 `TryPrepareTrigger`，其他参与者仅 `Visit`，释放时按批 `TryUseOnce`。这样同一步的其他弹珠不会被公共冷却挡掉。`Visit` 本身不扣耐久、不应用机器倍率或复制修正。
- 暂停模拟不推进事件或时间。装置移除/收回/视图消失后不再分发；滞留的取消仍由通用滞留系统负责。新回合会获得新 `RoundId`，Lua 可据此清空上回合的普通数据。
- 回调异常会记录错误并关闭上下文，其他弹珠继续更新；已成功的操作不回滚。每次回调只操作当前弹珠，不能保存 API 待以后调用。

以下是改变运动方向的最小组合示例（每球只接受一次，不包含聚集、滞留或释放规则）：

```lua
function OnBallUpdate(api)
    local motion = api.Motion
    local delta = api.Vectors:ProjectOnPlane(motion.Source.Position - motion.Position, motion.Source.Up)
    if delta.magnitude > api.Range then return end
    if not api:Visit(0, 1) or not api:TryUse() then return end
    motion:TrySetVelocity(api.Vectors:Normalize(delta) * math.min(api.Strength, delta.magnitude / api.DeltaTime))
end
```

## 吸吸：聚集后共同向下释放

完整效果位于 `MResPacker/Authoring/Lua/Collector.lua`。制作配置是 `Authoring/Profiles/Collector.asset`，输出 `MResPacker/Output/Collector.mres`，运行时副本为 `Resources/Devices/9003_Collector.mres`。

Lua 组合 `OnBallUpdate`、范围/向量计算、`TrySetVelocity`、带障碍检查的 `TryMove`、`TryHold` 和 `OnHoldEnd`：

1. 第一颗合格弹珠进入范围时启动磁力，保存共同截止时间 `Time + Parameter`。后续弹珠加入同一批，不延长截止时间；范围使用棋盘平面距离，并排除明显高于/低于棋盘的弹珠。
2. 为每颗弹珠保存进入时的速度大小，按各自半径预留相邻、互不重叠的位置。Lua 设置朝向该位置的收拢速度，由正常物理步移动；接近时用 `TryMove` 对齐，并 `TryHold(截止时间 - Time)`。这是一种由 Lua 控制收拢速度的效果；不调用旧磁铁的 `OnField` 力积分。
3. 磁力满 2 秒后，在同一个固定步释放已聚集弹珠，各自设置为 `Source.Down * 进入时速度`。保留聚集位置，不把所有球送到同一个出口；零速仍为零，之后由普通重力继续运动，当前速度上限仍生效。
4. 截止时还没抵达、目标受阻的弹珠同样结束吸引，在当前位置向下释放。满员找不到安全预留位置时不接纳新球。每球对同一实例只参加一次，新弹珠可启动下一批；不同实例的 Lua 状态独立。

配置 `game/device.json`：`Value = 2` 为磁力秒数（Lua 最大取 60），`Range = 1.2` 为世界吸引半径，`Strength = 3` 为世界单位/秒的收拢速度。非正时长/收拢速度不启动。ID 9003，圆柱直径 0.4、高 0.1，使用触发器，购买价 5、出售价格 120，初始库存 0。`Cooldown = 0`、`MaxTriggersPerBall = 1`；首球用 `TryPrepareTrigger` 应用通用修正，其他参与者只登记访问，聚集不扣耐久。到期释放用同一个 `batch:n` 登记一次；最后一点耐久释放整批，并申请一个固定步的完成窗口，让尚未抵达的成员也能完成。窗口期间不接纳新的批次。

Lua 只跨回调保存 ID、数字和世界位置副本。聚集位置在接纳时固定；滞留阶段不跟随移动装置，释放方向读取当前装置姿态。移除装置会取消滞留并恢复滞留前运动；不会补发 Lua 的向下释放。弹珠消失时引擎回收物理/计时状态，其少量 Lua 普通数据最迟在下一回合首次更新时清空；正常释放立即删除对应记录。

## 持续力事件

磁铁、加速力场等效果使用 `OnField(a)`，在**每个 PhysX 子步之前**针对每颗未滞留的弹珠调用。此时 `a` 是 `DeviceFieldContext`，并非 `DeviceContactContext`，不能调用 `Motion` / `Trigger`。Lua 判断作用区域、方向与大小，C# 累加力并交给正常物理积分。

| API | 含义 |
| --- | --- |
| `OverlapsBox(nodePath)` | 判断当前弹珠球体是否与来源装置的指定 BoxCollider 相交/接触。路径相对于包的根节点，例如 `"Field"`、`"Zones/Field"`；空字符串查根节点。包含 Collider 的中心、大小、实际旋转和缩放，按真实盒体判断，不用世界 AABB 代替旋转矩形。缺失、禁用或未激活的 Collider 返回 `false`。只查询，不施力。 |
| `TryUse()` | 接受当前球的一次区域作用；先由 Lua 检查区域。连续停留每个子步调用也只扣一次耐久，离开后重入再扣。返回 `false` 时应立即结束回调，不施力。 |
| `TryAddForce(worldForce)` | 为当前弹珠、当前物理子步累加世界坐标力向量，单位 N。有效输入返回 `true`；NaN、无穷或导致长度平方/累加溢出的输入返回 `false`，累计力不变。可多次调用，零向量合法；不立即改写速度，不指定区域。 |
| `AddRadialForce(force)` | 兼容旧磁铁：沿棋盘平面指向装置中心的方向加力，负值向外排斥；内部复用通用力累加。具体规则仍见 `112_Magnet.mres` 的 Lua。 |
| `Forward` / `Up` | 来源装置的世界朝向（本地 **-Z**）和棋盘法线（本地 +Y）；只表示方向，旋转装置会更新它们，缩放不会改变力的大小。 |
| `DeltaTime` | 当前**物理子步**秒数，可能小于逻辑固定步。调用 `TryAddForce` 时不要再乘这个值。 |
| `Strength` / `Range` | 来源装置配置的力度、范围。如何解释、是否使用，由 Lua 决定。 |
| `Magnetic` / `Response` / `MagneticRadius` | 弹珠磁性标记、磁力响应及磁性范围参数；引擎不自动要求力场只能影响磁性弹珠。 |
| `Distance` / `ForwardOffset` | 弹珠到装置中心的棋盘平面距离、沿装置朝向的有符号偏移。 |

`TryAddForce` 不带吸引、区域检测、冷却或后续释放逻辑。每个子步从零开始收集所有力场的力，按向量相加，并沿用总场力 **100 N** 上限；随后用 `F × 子步秒数` 的冲量交给 Rigidbody，得到 `Δv = F × dt / mass`。Lua 应传力本身，不能重复乘时间或除质量。重力另行计算，最终仍遵守弹珠的 `BALL_MAX_SPEED`。恒力不会覆盖侧向速度，不主动施加扭矩；碰撞和重力正常生效，ECS 在物理步后读回结果。

范围检测使用物理子步开始时的位置；球体刚与盒体相交就生效，包含矩形外沿的圆角距离检查。离开区域后下一次检测不再加力，已获得的速度保持。滞留、装置视图移除/禁用时不会继续施力，暂停物理也不累积力。上下文及所有公开属性/方法只在当前 `OnField` 有效，不能保留后跨回调调用；异常时关闭上下文并记录错误，其他力场和弹珠继续处理，异常前已经接受的力不回滚。

### 加速力场

制作源脚本为 `MResPacker/Authoring/Lua/AccelerationField.lua`，成品为 `MResPacker/Output/AccelerationField.mres`，游戏副本为 `Resources/Devices/9004_AccelerationField.mres`：

```lua
function OnField(api)
    if api:OverlapsBox("Field") and api:TryUse() then
        api:TryAddForce(api.Forward * api.Strength)
    end
end
```

`Field` 是制作 Prefab 中的普通 BoxCollider 节点名，由这份 Lua 选择，不是引擎硬编码名称。默认区域长 **1.2**（本地 Z）、宽 **0.4**（本地 X）、高 **0.24**（本地 Y），中心位于本地 Y=0.14。使用透明蓝色盒体、边框和指向本地 -Z 的箭头；Collider 为 Trigger，不阻挡弹珠。

`game/device.json.Strength = 3` 即 3 N。进入区域的弹珠持续受力，较重弹珠加速更慢；逆着力场运动会先减速，侧向速度保留。没有磁性要求、距离衰减或公共冷却；每球每次进入扣 1 耐久，停留不重复扣。最后一次进入仍执行当前力场回调，随后本回合停止施力。区域直接读取 BoxCollider 几何，不依赖 `Range` 或 `Value`：修改 Prefab 的 `Field` 节点缩放或 BoxCollider 参数后重新打包即可，边框和箭头可同步调整。

ID 9004，初始库存 0、购买价 5、出售价格 120。配置 `Radius = 0.633` 是放置时的保守外接圆半径，不是实际力场形状；通用装置放置半径支持 `[0.01, 10]`，以容纳长形资源。改变宽度/长度时应同步更新此放置半径，避免通用放置检查低估占用范围。宽 `w`、长 `l` 的居中矩形可用 `sqrt((w/2)^2 + (l/2)^2)` 并略向上取整。

## 代码与验证

- 引擎接口：`Scripts/System/DeviceMotionApi.cs`、`DeviceContactContext.cs`、`DeviceHoldInfo.cs`、`DeviceImpactInfo.cs`、`DeviceVectorApi.cs`；事件分发见 `DeviceScriptSystem.cs`。
- ECS/物理同步：`BallInteractionSystem.cs`、`HeldMotionSystem.cs` 和 `Scripts/Physics/PhysX/MarblePhysicsBridge.Content.cs` / `MarblePhysicsBridge.Suspension.cs`。
- `Scripts/link.xml` 保留 Lua 反射访问的类型，供 Player 裁剪使用。
- Unity 菜单 `Marbles ECS/Validate Attribute and Content Runtime` 执行包内 Lua、旋转出口、零速/低速、角速度保留、失败状态、限速、上下文生命周期以及真实 PhysX 阻挡测试，并回归现有商店/库存/装置功能。
- `GameContentValidation.Durability.cs` 验证所有包默认元数据、购买价和出售到账、有限耐久的第 20/21 次、实例独立与下回合恢复、无限耐久；黏黏、力场、黑洞专项同时检查最后一次作用、滞留完成、区域进入计数、无残留力和碰撞体恢复。Packer / Loader EditMode 测试覆盖四种稀有度、`-1`、旧包兼容及非法元数据。
- 储存类专项验证免费接纳/充能、每批释放只扣一次、部分/全部出口受阻重试、最后一次黑洞完整 10 秒及期间超过 20 次吞球、吸吸未聚拢成员完成、黏黏待释放数量；通用验证覆盖跨回调/实例/回合的批次 key、无限耐久与完成窗口到期。
- `Marbles ECS/Validate Sticky Device` 单独验证黏黏的包内 Lua、真实圆柱触发器、完整滞留时长、下坡方向、速度/旋转恢复、阻挡与移除、多球计时、寿命和对象池清理。
- 组合能力验证还覆盖无结束回调、独立属性修改、重复滞留、消耗弹珠、新旧上下文有效期、装置实时朝向及异常恢复。测试会主动注入一次标记为 `EXPECTED_HOLD_COMPLETION_FAILURE` 的 Lua 异常，检查记录错误后其他弹珠仍能正常完成。
- 弹弹验证加入 `Marbles ECS/Validate Attribute and Content Runtime`，覆盖倾斜棋盘、低速、正碰/擦边、随机角度边界、种子复现、零角度、限速、非法/无几何接触、回调有效期，以及真实圆柱在自然弹性为零时仍能通过 Lua 保留入射速度弹出。
- 吸吸验证 `Editor/Validation/CollectorDeviceValidation.cs` 也由同一菜单运行，覆盖包内 Lua 的真实物理吸引、多球/不同半径聚集、后来者共享截止时间、零速/低速/限速、棋盘下坡方向、释放防重抓、范围筛选、目标受阻、装置取消、寿命/对象池/跨回合清理和新 API 有效期。
- 加速力场验证 `Editor/Validation/AccelerationFieldValidation.cs` 覆盖质量与恒力积分、不同固定步/多物理子步、旋转盒体的边缘/角点/高度、区域缩放、力场叠加、限力/限速、真实进入离开、重复进入、禁用/滞留/重置及上下文有效期；同时回归旧磁铁。
- 传送门验证 `Editor/Validation/PortalDeviceValidation.cs` 覆盖真实 `BuyOffer` 一次购买两个装置、只扣整对价格、独立配对编号、分别放置/双向传送、容量和资金不足时不产生半对、零速/极低速/三维速度、旋转/角速度/质量/重力保持，以及入口/出口分别有无力场的四种物理情况。`GameContentValidation.LuaApi.cs` 继续覆盖未放置出口、实际阻挡、传送锁及通用运动 API。
- 黑洞验证 `Editor/Validation/BlackHoleDeviceValidation.cs` 覆盖真实圆柱吞噬、同一步多球充能、未充满/重复点击、等待期间穿过、局部物理场景点击、精确价值×10结算、重复结算拒绝、小数/上限、距离衰减、质量积分、两种固定步下的无球倒计时、重复充能、多实例与跨回合/会话清理。

## 无当前弹珠的装置事件

引擎仅向已放置且有有效物理视图的装置分发以下战斗事件。暂停时不推进模拟时间；建造、商店、回合结束阶段不会运行这些战斗事件。`OnCreated` / `OnOfferAccepted` / `OnRoundStart` 属于前述独立的生命周期事件，不受此限制。事件参数每次新建，用完关闭，缓存后再调用会抛出异常。

| 事件/API | 契约 |
| --- | --- |
| `OnTick(api)` | 每个固定模拟步**开始**调用一次，顺序按库存实例 ID。即使当前没有弹珠也会调用；`Time` 是此步开始的回合秒数，`DeltaTime` 是即将模拟的步长。与物理子步和渲染帧数无关，可用于 Lua 计时/状态更新。 |
| `OnActivate(api)` | 玩家左键点击装置时触发，不要求存在当前弹珠。Lua 自行判断是否充满、能否再次激活。Playing 和 Draining 阶段可用。 |
| `InstanceId` / `RoundId` / `Time` / `DeltaTime` | 库存实例 ID、回合 ID、当前模拟秒数、固定步秒数。点击发生于物理步之间，尚未运行过固定步时 `DeltaTime` 为 0。 |
| `Parameter` / `Strength` / `Range` / `ScoreMultiplier` | 装置 `game/device.json` 中的 `Value`、`Strength`、`Range`、`ScoreMultiplier`。不包含弹珠属性。 |
| `SetStatus(text)` | 与接触上下文相同的通用状态文字发布接口。 |
| `RemainingDurability` | 当前剩余耐久，`-1` 表示无限。点击分发本身不扣，Lua 在效果生效时登记。耗尽且没有手动滞留或有效完成窗口时停止事件。 |
| `TryUse()` / `CanUseOnce(key)` / `TryUseOnce(key)` | 显式登记单次效果，或跨回调按批去重，详见耐久章节。 |
| `TryKeepActive(seconds)` | 保留有期限的效果完成窗口，详见耐久章节。 |
| `HeldCount` / `ReleaseHeld()` | 查询当前装置的存活滞留数量，或释放调用时的滞留集合并逐球调用新 `OnHoldEnd`；详见大炮章节。 |
| `Forward` / `Up` / `TrySetYaw(degrees)` | 获取当前世界朝向/棋盘法向，或设置相对摆放姿态的绝对旋转偏移；详见大炮章节。 |
| `Position` / `Radius` | 当前放置装置的世界位置和占位半径，读取实际物理视图。 |
| `Vectors` / `CreateSpawnBatch()` | 无需当前弹珠的纯向量运算和批量生成；返回对象仅本次回调有效，详见对应章节。 |

该上下文没有 `Motion`、`Consume` 或 `TrySettle`，因为它不代表任何弹珠。计时保存为 Lua 普通数字，例如 `endsAt = api.Time + api.Parameter`，不要保存旧的 `api`。装置移除后不再收到事件，会话重置释放脚本与状态文字；跨回合 Lua 使用 `RoundId` 自行清理玩法状态。单个回调抛错会关闭上下文并记录异常，其他装置继续执行，异常前已生效的操作不回滚。

`MarbleSimulation.TryActivateDevice(instanceId)` 返回是否成功分发回调，**不代表 Lua 一定接受激活**。缺少装置/视图/函数、未放置、错误阶段或正在物理步中时返回 `false`。Controller 的同名入口另外拒绝暂停时的点击。`GetDeviceStatus(instanceId)` 只读本回合发布的文字。

Controller 自动安装通用 `DeviceActivationUI`，左键拾取此 Controller 的**本地 PhysX 场景**内带 `OnActivate` 的最近装置，包括 Trigger；可以指定 `ViewCamera`，默认取放置界面的相机或主相机。按钮、点击/拖动控件会阻挡点击，装饰性全屏 Graphic 不阻挡；旧 IMGUI HUD 的侧栏也阻挡。悬浮文字显示在装置上方，点击装置本体释放。其他输入系统可以直接调用 Controller 的 `TryActivateDevice(instanceId)` / `TryActivateDevice(ray)`。预览没有物理身份，也不会接收点击。

## 黑洞：吞噬充能，点击释放全局衰减引力

制作源 `MResPacker/Authoring/Lua/BlackHole.lua`，游戏使用 `Resources/Devices/9005_BlackHole.mres`。所有状态均为各实例私有 Lua 普通变量；引擎不包含黑洞名称、ID、充能颗数或持续时间分支。

1. 未充满时碰到圆柱触发器就 `TryConsume(false)`，不计分、不返还、不扣耐久，成功吞噬累计 10 颗后显示“黑洞已充能 · 点击释放”。同一步多球最多吞噬缺少的颗数，不调用有公共冷却的 `Trigger`。
2. 满充后等待左键点击，期间新进入的弹珠穿过，不额外吞噬/计分；未充满的点击无效果。
3. `OnActivate` 用 `TryUse()` 扣 1 并设置截止时间，`TryKeepActive(Parameter)` 保证最后一点耐久也持续完整 10 个模拟秒；每个 `OnField` 对当前场景内所有普通运动弹珠施加向内径向力，无磁性要求、无最远距离截断。
4. 生效期间进入黑洞的弹珠调用 `api:TrySettleAmount(api.Value * api.ScoreMultiplier)`，按**当前 `BALL_BASE_VALUE × 10`**直接结算，不再扣耐久或叠加其他结算倍率。已结算球不会被其他装置或计分区重复结算，正常发出得分事件和回收刚体。
5. 到期清空充能，耐久足够时可再吞噬 10 颗重新激活；有效期间重复点击不刷新时间、不扣耐久。默认耐久 20 可启动 20 次，每次有效期内吞球数量不受耐久限制；最后一次结束后才停用。`-1` 无限，新回合从 0 颗重新充能并恢复耐久；暂停不消耗时间，滞留球不受连续力。

力的大小由 Lua 控制，采用平滑衰减公式（`Distance` 是弹珠到装置中心在棋盘平面上的距离）：

```lua
function OnField(api)
    if not active then return end
    local ratio = api.Distance / api.Range
    api:AddRadialForce(api.Strength / (1 + ratio * ratio))
end
```

默认 `Strength = 8 N`，`Range = 1` 是**半强度距离**，不是范围边界；距离 1、2、20 时分别为 4、1.6、约 0.01995 N。引力沿装置平面指向中心，中心处零向量避免奇点，不把弹珠拉离棋盘。引擎负责 `Δv = F × dt / mass`，保留重力/其他区域力；多个黑洞或力场的力相加，沿用总场力 100 N 和弹珠限速约束。时间按固定步量化，力在到期后的下一步停止，最多一个固定步误差；没有弹珠时 `OnTick` 也会及时到期。

参数位于 `MResPacker/Authoring/Data/BlackHole.device.json`：`Value = 10` 是持续秒数，`ScoreMultiplier = 10` 是结算倍率，`Strength = 8`、`Range = 1` 控制衰减，`Radius = 0.2` 是放置占位半径。充能颗数为 Lua 顶部 `chargeRequired = 10`。购买价 5、出售价格 120、一次购买一个、初始库存 0。圆柱模型、圆环、材质、Prefab、场景和打包配置都在 `MResPacker` 内；修改后重新打包并同步游戏副本。

新增代码入口：`DeviceEventContext.cs`、`DeviceScriptSystem.cs`、`MarbleSimulation.Devices.cs`、`ContentSettlementSystem.TrySettleAmount`、`MarblePhysicsBridge.Picking.cs`、`DeviceActivationUI.cs`。这些 API 均可独立用于其他装置，充能流程与公式只存在于 Lua。

## 文档维护约定

本文件是游戏装置效果 API 的统一入口。新增或修改对 Lua 暴露的 API 时，同步更新接口表、参数来源、返回值、回调有效期、暂停/销毁/失败行为及至少一个 Lua 使用示例；效果脚本保存在制作目录并随 `.mres` 发布。引擎增加可复用能力，装置自己的触发条件、方向、时长和数值由 Lua 配置与组合。
