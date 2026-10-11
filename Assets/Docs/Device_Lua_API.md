# 装置 Lua API

装置效果写在 `Resources/Devices/*.mres` 包内的 Lua 中。引擎向 `OnContact(a)`、`OnHoldEnd(a)`、`OnBallUpdate(a)` 传入各自独立的 `DeviceContactContext`：C# 提供弹珠运动、属性、触发和计分等通用能力；Lua 选择调用哪些能力、调用顺序及数值。新增效果不需要在 C# 按装置 ID 或 `Kind` 增加分支。

没有当前弹珠的装置事件 `OnTick(a)`、`OnActivate(a)` 使用 `DeviceEventContext`；连续力 `OnField(a)` 使用 `DeviceFieldContext`。不同上下文提供各自阶段的能力，不能互换或跨回调保存。

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

耐久按库存实例独立计算。有限耐久每次有效作用扣 1，最后一次仍生效，随后本回合停用：停止接触、持续力、逐球更新、计时和点击事件，同时关闭碰撞体。已开始的滞留仍调用 `OnHoldEnd` 正常释放，不追加扣除；新回合恢复满耐久并重新启用碰撞体。`-1` 始终返回 `-1`，不扣减，界面显示“无限耐久”。商店/库存显示稀有度、价格和耐久，棋盘上显示剩余耐久与失效状态。

| 使用方式 | 计数契约 |
| --- | --- |
| `Trigger(key)` | 通过触发率、冷却和访问检查后扣 1；同一回调后续 `TryUse`、`Consume`、`TrySettle` 共用这次作用，不重复扣除。失败的触发检查不扣。 |
| 接触/逐球回调 `TryUse()` | 登记一次作用，成功返回 `true`。同一回调重复调用不重复扣，耗尽返回 `false`；不增加访问次数、冷却或机器倍率。适用于自行管理触发条件的 Lua。 |
| `Consume(refund)` / `TrySettle(score)` | 接受吞噬/有效结算时自动登记一次作用；非法或重复结算不扣。黑洞充能与生效期间每吞一球扣 1，点击和引力不另扣。 |
| 力场回调 `TryUse()` | 在 Lua 判断球处于作用区域后调用。引擎按弹珠与装置记录连续进入状态，停留期间后续子步不重复扣，离开后再次进入扣 1。未调用就不登记区域作用；黑洞全局引力按吞噬计数，因此不调用它。 |
| `OnHoldEnd` 中 `TryUse()` | 返回 `true`，完成已经接受的效果，不追加扣耐久。 |

运动、向量、属性 API 不隐式扣耐久；Lua 在接受一次新作用时组合 `Trigger` 或 `TryUse`。持续多阶段效果只在纳入新弹珠时登记，后续更新与释放不重复登记；`Visit` 本身只记录访问次数。成功登记后若后续运动受阻，不回滚已经接受的触发、冷却或耐久。

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

所有 `Try*` 返回布尔值。非法数值（NaN/无穷）、负速率、零出射方向、无效出口、找不到刚体，会返回 `false`。即时位移的终点被阻挡也返回 `false`；位移只检查终点，不要求沿途无障碍。速度操作统一执行全局限速，成功后同步 PhysX 和 ECS，保留角速度；成功操作后同一逻辑帧的后续弹珠交互会跳过，避免使用旧接触位置重复触发。延时操作的返回值及异常处理见下一节。

### 黏黏：滞留后向下释放

制作源文件为 `MResPacker/Authoring/Lua/Sticky.lua`，成品为 `MResPacker/Output/Sticky.mres`，游戏资源为 `Resources/Devices/9001_Sticky.mres`。

```lua
function OnContact(api)
    if not api:Trigger(0) then return end
    api.Motion:TryHold(api.Parameter)
end

function OnHoldEnd(api)
    local motion = api.Motion
    local source = motion.Source
    local speed = api.Hold.EntrySpeed
    if not motion:TryLaunch(source, source.Down, speed) then
        motion:TrySetVelocity(source.Down * speed)
    end
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

### 结束信息 `api.Hold`

`OnContact` / `OnBallUpdate` 中为 `nil`；`OnHoldEnd` 中是只读的 `DeviceHoldInfo`。以下数据仅在本次结束回调内有效，跨回调使用请保存普通数字或向量副本。

| 属性 | 含义 |
| --- | --- |
| `EntryVelocity` / `EntrySpeed` | 滞留开始时的世界线速度向量及大小，不受等待期间修改限速的影响；实际运动命令仍执行当前限速。 |
| `EntryAngularVelocity` | 滞留开始时的世界角速度向量。 |
| `Duration` | 调用 `TryHold` 时请求的秒数。 |
| `Elapsed` | 实际经过的模拟秒数，受固定步量化影响，可能略大于请求时长。 |

结束回调沿用来源装置及原接触的 `LinkKey`。`Trigger` 在结束回调返回 `false`，避免把完成事件当成第二次碰撞消耗冷却/次数或重复应用触发修正。

黏黏默认 `MaxTriggersPerBall = 1`，同一弹珠对同一个黏黏只滞留一次，避免出口受阻后反复吸附；不同黏黏分别计数。`Cooldown = 0` 仍遵守 `Trigger` 的通用同逻辑帧触发限制。购买价 5、出售价格 120、初始库存为 0，可在制作配置中调整。

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
| `Get(attribute)` / `Set(attribute, value)` / `Global(attribute)` | 读取弹珠属性、设置基础属性、读取全局属性。名称使用 `GameAttribute` 中的大写字符串。 |
| `Value` / `Parameter` / `Strength` | 弹珠基础价值、装置 `Value` 参数、装置力度参数。 |
| `ScoreMultiplier` / `RushChanceAdd` | 装置的计分、Rush 配置。 |
| `Time` / `PairId` / `LinkKey` | 当前回合秒数、库存配对 ID、当前连线标识。 |
| `RoundId` / `BallId` | 当前回合 ID；弹珠的稳定字符串 ID，格式为 `回合:出生序号`，避免 Lua 数字丢失 64 位序号精度。 |
| `DeltaTime` | 当前固定模拟步的秒数，不是渲染帧间隔。 |
| `Radius` / `Range` | 当前弹珠世界半径；来源装置配置的作用范围。引擎不自动用 `Range` 过滤更新事件。 |
| `StoredValue` | 可读写的装置储存数值。 |
| `TransportLockedUntil` / `ExitDown` | 可读写的弹珠传送锁截止时间、旧一次下坡出射标志。当前只改位置的传送门不使用/消费 `ExitDown`；需要方向覆盖的其他脚本可自行读取和清除。 |
| `RushOnSettlement` / `RefundOnDespawn` | 可读写的弹珠结算 Rush 概率、消失返还标志。 |
| `Consume(refund)` / `AddCoins(value)` | 消耗当前弹珠（参数控制是否保留返还资格）、增加金币。消耗后应结束本次回调。 |
| `TrySettle(score)` | 按指定最终分数立即结算并消耗当前弹珠，成功返回 `true`。Lua 决定公式；不额外套用机器/全局/药物/Rush 倍率，不触发 Rush 抽签或增长。非法、负数、已经结算/待消失的弹珠返回 `false`；有限的大值遵守原有单球/回合分数上限。保留小数累计、正常计分事件与原有消失返还资格；成功后不要继续操作弹珠。 |
| `SetStatus(text)` | 发布当前装置的显示文字，最多 256 字符，`nil` 清空；用于进度/提示/倒计时。只改变表现信息，不激活效果或推进时间。结算后仍可更新装置的文字。 |
| `Children(count, replace)` | 通过通用生成系统产生子球，可替换父球；继承、代数与数量上限由引擎处理。 |
| `MultiplyScore(factor)` / `AddRushChance(amount)` | 修改弹珠计分倍率、Rush 概率增量。 |
| `Roll(probability)` | 使用本局统一随机状态进行概率判定。 |
| `RandomRange(minimum, maximum)` | 使用本局统一随机状态在指定区间均匀取数；有限数值且下界不大于上界。上下界相等时直接返回该值，不消耗随机状态。 |
| `BreakPinsOnCross()` / `RemoveDevice()` | 清除当前装置十字范围内的普通钉子、消耗当前装置。 |

旧 `a.Speed`、`ScaleVelocity`、`Exit`、`ExitPartner` 作为兼容别名保留，内部也走新的通用运动 API。新脚本优先使用 `a.Motion`。

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

`RandomRange` 与 `Roll` 共用当前回合的随机状态。同样的种子和调用次序得到相同结果，不使用 Unity 全局随机数或 Lua `math.random`。非法上下界（NaN、无穷、下界大于上界或跨度溢出）抛出异常且不消耗随机状态。

`api.Impact`、`api.Vectors` 和 `RandomRange` 遵守当前回调有效期；保存 Impact 或 Vectors 后跨回调访问会报错。需要延迟使用某个入射量时，保存普通数值/向量副本。

## 逐弹珠更新 `OnBallUpdate(api)`

只有声明该函数的装置才会接收事件。每个固定模拟步，物理读回、滞留到期和接触结算完成后，为每个已放置且有物理视图的装置逐个更新仍在飞行的弹珠。按装置实例 ID、弹珠出生序号排序。Lua 自行选择范围和条件，可组合现有运动、向量、属性和计时接口；不需要发生 Collider 接触，也不受弹珠是否带磁性限制。

- 传入新的 `DeviceContactContext`；`Hold` / `Impact` 均为 `nil`，`LinkKey = 0`。`Time`、`DeltaTime`、`RoundId`、`BallId`、`Radius` 和 `Range` 可用于模拟计时、按球保存普通数据和空间筛选。
- 已结算、待销毁、滞留中、寿命已到期的弹珠不更新；排空已超时也不更新。当前固定步已成功执行运动命令的弹珠跳过，避免覆盖刚完成的碰撞/释放效果；同时受多个装置影响时，较早成功操作的装置优先。
- 范围外仍会调用，便于 Lua 对先前进入的弹珠继续完成效果。没有全局自动吸引、自动计时或自动释放行为。
- `Trigger` 可用于启动一次装置效果；同时影响多球时，可以由 Lua 对首球 `Trigger`，随后参与者组合 `Visit` 和 `TryUse`，避免装置公共冷却把同一步的其他弹珠挡掉。`Visit` 本身不扣耐久、不应用机器倍率或复制修正。
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

配置 `game/device.json`：`Value = 2` 为磁力秒数（Lua 最大取 60），`Range = 1.2` 为世界吸引半径，`Strength = 3` 为世界单位/秒的收拢速度。非正时长/收拢速度不启动。ID 9003，圆柱直径 0.4、高 0.1，使用触发器，购买价 5、出售价格 120，初始库存 0。`Cooldown = 0`、`MaxTriggersPerBall = 1`；首球启动应用一次通用触发修正，其余参与者登记访问并扣 1 耐久。

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
- `Marbles ECS/Validate Sticky Device` 单独验证黏黏的包内 Lua、真实圆柱触发器、完整滞留时长、下坡方向、速度/旋转恢复、阻挡与移除、多球计时、寿命和对象池清理。
- 组合能力验证还覆盖无结束回调、独立属性修改、重复滞留、消耗弹珠、新旧上下文有效期、装置实时朝向及异常恢复。测试会主动注入一次标记为 `EXPECTED_HOLD_COMPLETION_FAILURE` 的 Lua 异常，检查记录错误后其他弹珠仍能正常完成。
- 弹弹验证加入 `Marbles ECS/Validate Attribute and Content Runtime`，覆盖倾斜棋盘、低速、正碰/擦边、随机角度边界、种子复现、零角度、限速、非法/无几何接触、回调有效期，以及真实圆柱在自然弹性为零时仍能通过 Lua 保留入射速度弹出。
- 吸吸验证 `Editor/Validation/CollectorDeviceValidation.cs` 也由同一菜单运行，覆盖包内 Lua 的真实物理吸引、多球/不同半径聚集、后来者共享截止时间、零速/低速/限速、棋盘下坡方向、释放防重抓、范围筛选、目标受阻、装置取消、寿命/对象池/跨回合清理和新 API 有效期。
- 加速力场验证 `Editor/Validation/AccelerationFieldValidation.cs` 覆盖质量与恒力积分、不同固定步/多物理子步、旋转盒体的边缘/角点/高度、区域缩放、力场叠加、限力/限速、真实进入离开、重复进入、禁用/滞留/重置及上下文有效期；同时回归旧磁铁。
- 传送门验证 `Editor/Validation/PortalDeviceValidation.cs` 覆盖真实 `BuyOffer` 一次购买两个装置、只扣整对价格、独立配对编号、分别放置/双向传送、容量和资金不足时不产生半对、零速/极低速/三维速度、旋转/角速度/质量/重力保持，以及入口/出口分别有无力场的四种物理情况。`GameContentValidation.LuaApi.cs` 继续覆盖未放置出口、实际阻挡、传送锁及通用运动 API。
- 黑洞验证 `Editor/Validation/BlackHoleDeviceValidation.cs` 覆盖真实圆柱吞噬、同一步多球充能、未充满/重复点击、等待期间穿过、局部物理场景点击、精确价值×10结算、重复结算拒绝、小数/上限、距离衰减、质量积分、两种固定步下的无球倒计时、重复充能、多实例与跨回合/会话清理。

## 无当前弹珠的装置事件

引擎仅向已放置且有有效物理视图的装置分发以下事件。暂停时不推进模拟时间；建造、商店、回合结束阶段不会运行这些事件。事件参数每次新建，用完关闭，缓存后再调用会抛出异常。

| 事件/API | 契约 |
| --- | --- |
| `OnTick(api)` | 每个固定模拟步**开始**调用一次，顺序按库存实例 ID。即使当前没有弹珠也会调用；`Time` 是此步开始的回合秒数，`DeltaTime` 是即将模拟的步长。与物理子步和渲染帧数无关，可用于 Lua 计时/状态更新。 |
| `OnActivate(api)` | 玩家左键点击装置时触发，不要求存在当前弹珠。Lua 自行判断是否充满、能否再次激活。Playing 和 Draining 阶段可用。 |
| `InstanceId` / `RoundId` / `Time` / `DeltaTime` | 库存实例 ID、回合 ID、当前模拟秒数、固定步秒数。点击发生于物理步之间，尚未运行过固定步时 `DeltaTime` 为 0。 |
| `Parameter` / `Strength` / `Range` / `ScoreMultiplier` | 装置 `game/device.json` 中的 `Value`、`Strength`、`Range`、`ScoreMultiplier`。不包含弹珠属性。 |
| `SetStatus(text)` | 与接触上下文相同的通用状态文字发布接口。 |
| `RemainingDurability` | 当前剩余耐久，`-1` 表示无限。点击本身不扣耐久；耗尽的装置不再收到点击/计时事件。 |

该上下文没有 `Motion`、`Consume` 或 `TrySettle`，因为它不代表任何弹珠。计时保存为 Lua 普通数字，例如 `endsAt = api.Time + api.Parameter`，不要保存旧的 `api`。装置移除后不再收到事件，会话重置释放脚本与状态文字；跨回合 Lua 使用 `RoundId` 自行清理玩法状态。单个回调抛错会关闭上下文并记录异常，其他装置继续执行，异常前已生效的操作不回滚。

`MarbleSimulation.TryActivateDevice(instanceId)` 返回是否成功分发回调，**不代表 Lua 一定接受激活**。缺少装置/视图/函数、未放置、错误阶段或正在物理步中时返回 `false`。Controller 的同名入口另外拒绝暂停时的点击。`GetDeviceStatus(instanceId)` 只读本回合发布的文字。

Controller 自动安装通用 `DeviceActivationUI`，左键拾取此 Controller 的**本地 PhysX 场景**内带 `OnActivate` 的最近装置，包括 Trigger；可以指定 `ViewCamera`，默认取放置界面的相机或主相机。按钮、点击/拖动控件会阻挡点击，装饰性全屏 Graphic 不阻挡；旧 IMGUI HUD 的侧栏也阻挡。悬浮文字显示在装置上方，点击装置本体释放。其他输入系统可以直接调用 Controller 的 `TryActivateDevice(instanceId)` / `TryActivateDevice(ray)`。预览没有物理身份，也不会接收点击。

## 黑洞：吞噬充能，点击释放全局衰减引力

制作源 `MResPacker/Authoring/Lua/BlackHole.lua`，游戏使用 `Resources/Devices/9005_BlackHole.mres`。所有状态均为各实例私有 Lua 普通变量；引擎不包含黑洞名称、ID、充能颗数或持续时间分支。

1. 未充满时碰到圆柱触发器就 `Consume(false)`，不计分、不返还，累计 10 颗后显示“黑洞已充能 · 点击释放”。同一步多球最多吞噬缺少的颗数，不调用有公共冷却的 `Trigger`。
2. 满充后等待左键点击，期间新进入的弹珠穿过，不额外吞噬/计分；未充满的点击无效果。
3. `OnActivate` 设置截止时间，持续 10 个模拟秒；每个 `OnField` 对当前物理场景内所有普通运动弹珠施加向内的径向力，无磁性要求、无最远距离截断。
4. 生效期间进入黑洞的弹珠调用 `api:TrySettle(api.Value * api.ScoreMultiplier)`，按**当前 `BALL_BASE_VALUE × 10`**直接结算，不再叠加其他结算倍率。已结算球不会再被同一步的其他装置或计分区重复结算，正常发出得分事件和回收刚体。
5. 到期清空充能，耐久足够时再吞噬 10 颗可重复使用；有效期间重复点击不刷新截止时间。每个黑洞独立充能和计时。默认耐久 20：充能 10 颗后还剩 10 次吞噬，耗尽则立即停止全局引力和吞噬；`-1` 不受耐久限制。新回合从 0 颗开始并恢复耐久；暂停不消耗持续时间，滞留球不受连续力。

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
