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

临时重力是弹珠状态，计算为 `Physics.gravity × 当前 BALL_GRAVITY_MOD × scale`，不修改该属性本身，不反转力场、磁力或其他外力。期间基础重力或药物修正改变时继续参与计算；恢复时移除临时倍率，不用进入时的旧属性覆盖现值。

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
| `Get(attribute)` / `Set(attribute, value)` / `Global(attribute)` | 读取弹珠属性、设置基础属性、读取全局属性。名称使用 `GameAttribute` 中的大写字符串。 |
| `AddBase(attribute, amount)` | 给当前弹珠保存的基础属性加上一个有限增量，再重新计算当前属性修正；支持负增量，遵守属性上下限。无返回值，NaN/无穷或相加溢出抛出参数异常且不修改值。只负责属性运算，不扣耐久、不结算、不设置计时。 |
| `Value` / `Parameter` / `Strength` | 弹珠基础价值、装置 `Value` 参数、装置力度参数。 |
| `Coins` | 当前玩家持有的金币，只读；弹珠回调和 `OnTick` / `OnActivate` 的装置上下文均可读取。返回 Lua number，钱包上限 9,000,000,000,000,000 范围内整数精确；不预留或消耗金币，不缓存历史余额，跨回调读取旧上下文会报失效。 |
| `ScoreMultiplier` / `RushChanceAdd` | 装置的计分、Rush 配置。 |
| `Time` / `PairId` / `LinkKey` | 当前回合秒数、库存配对 ID、当前连线标识。 |
| `RoundId` / `BallId` | 当前回合 ID；弹珠的稳定字符串 ID，格式为 `回合:出生序号`，避免 Lua 数字丢失 64 位序号精度。 |
| `InstanceId` / `SpawnSourceId` | 当前装置的库存实例 ID；当前弹珠的生成来源装置 ID，玩家发射为 `0`。复制/分裂继承来源。引擎只提供信息，是否允许反哺来源装置由 Lua 判断。 |
| `CreateSpawnBatch()` | 创建当前回调内使用的 `DeviceSpawnBatch`，逐颗指定位置、速度、价值并整批提交；不消费当前弹珠，详见批量生成章节。 |
| `DeltaTime` | 当前固定模拟步的秒数，不是渲染帧间隔。 |
| `Radius` / `Range` | 当前弹珠世界半径；来源装置配置的作用范围。引擎不自动用 `Range` 过滤更新事件。 |
| `StoredValue` | 可读写的装置储存数值。 |
| `TransportLockedUntil` / `ExitDown` | 可读写的弹珠传送锁截止时间、旧一次下坡出射标志。当前只改位置的传送门不使用/消费 `ExitDown`；需要方向覆盖的其他脚本可自行读取和清除。 |
| `RushOnSettlement` / `RefundOnDespawn` | 可读写的弹珠结算 Rush 概率、消失返还标志。 |
| `TryConsume(refund)` / `AddCoins(value)` | 吞噬当前弹珠（参数控制是否保留返还资格）、增加金币，均不隐式扣耐久。重复吞噬返回 `false`；吞噬后应结束本次回调。旧 `Consume(refund)` 保留自动登记耐久的兼容语义。 |
| `TrySettleAmount(score)` | 按指定最终分数立即结算并消耗当前弹珠，不隐式扣耐久，成功返回 `true`。Lua 决定公式；不额外套用机器/全局/药物/Rush 倍率，不触发 Rush 抽签或增长。非法、负数、已经结算/待消失的弹珠返回 `false`；有限大值遵守单球/回合上限。保留小数累计、计分事件与消失返还资格；成功后不要继续操作弹珠。旧 `TrySettle(score)` 另有自动登记耐久的兼容行为。 |
| `SetStatus(text)` | 发布当前装置的显示文字，最多 256 字符，`nil` 清空；用于进度/提示/倒计时。只改变表现信息，不激活效果或推进时间。结算后仍可更新装置的文字。 |
| `Children(count, replace)` | 通过通用生成系统产生子球，可替换父球；继承、代数与数量上限由引擎处理。 |
| `AddScoreMultiplier(amount)` | 传入本次完整倍率，引擎累加 `amount - 1`，最终装置倍率为 `1 + Σ(amount - 1)`；两次 1.5 为 2。只记录，得分区统一计算，不改基础价值、不立即加分、不隐式扣耐久。有限非负数有效，1 为中性、0 贡献 -1；负数、NaN、无穷抛错且不修改状态。增量最高 999，最终装置倍率最低 0、最高 1000；负增量保留用于后续相加。正常结算另遵守 `MACHINE_MULT` 等内容上限。 |
| `MultiplyScore(factor)` | 旧包兼容名称，内部转发 `AddScoreMultiplier`，同样只累加 `factor - 1`；新脚本使用新名称。 |
| `AddRushChance(amount)` | 增加当前弹珠 Rush 概率。 |
| `Roll(probability)` | 使用本局统一随机状态进行概率判定。 |
| `RandomRange(minimum, maximum)` | 使用本局统一随机状态在指定区间均匀取数；有限数值且下界不大于上界。上下界相等时直接返回该值，不消耗随机状态。 |
| `BreakPinsOnCross()` / `RemoveDevice()` | 清除当前装置十字范围内的普通钉子、消耗当前装置。 |

旧 `a.Speed`、`ScaleVelocity`、`Exit`、`ExitPartner` 作为兼容别名保留，内部也走新的通用运动 API。新脚本优先使用 `a.Motion`。

`Get` / `Value` 读取的是含当前修正的值，`Set` 写入的是未修正的基础值；做增量效果请用 `AddBase`，避免把已经乘过倍率的数再次写回基础值。例如基础价值 10、当前倍率 ×2 时，`AddBase("BALL_BASE_VALUE", 1)` 后当前值为 22，移除倍率后为 11。它与其他弹珠 API 一样仅在当前回调有效，跨回调使用会抛出失效异常。

## 装置倍率统一累加，到得分区计算

所有提供撞击得分倍率的装置共用一个增量累计值，最终倍率为 `1 + Σ(每次倍率 - 1)`。没有贡献时默认 ×1；两次 ×1.5 得到 `1 + 2 × (1.5 - 1) = ×2`。首次提供 1.2 得到 ×1.2，再提供 1.2 得到 ×1.4，之后 Multi Ding 提供 1.85 得到 ×2.25。基础的 ×1 始终只算一次。球价值 10 时，上述三次撞击不直接增加回合分数，进入普通得分区才得到 22.5（整数分与小数余量按原系统累计）。Lua 传入原始完整倍率，不要提前减 1。

Lua 的 `AddScoreMultiplier`、旧包的 `MultiplyScore`、`Trigger` / `TryPrepareTrigger` 接受的装置 `MACHINE_MULT` 修正，以及旧倍率钉接触都走同一规则；中性的通用修正 ×1、普通钉、Rush 钉不额外提供倍率。装置各自的访问次数、冷却和耐久规则不变，例如 Multi Ding 仍每球每实例一次。复制/分裂继承已累计的装置倍率增量及独立基础倍率，发射器新发射的球从空累计开始。

发射器、非装置永久修正、全局、药物、Rush、得分区倍率继续在正常结算公式中应用；它们不混入装置贡献和。基础价值加减（如沙包、矿老板）仍修改基础属性。黑洞的专用 `TrySettleAmount(Value × 10)` 是原有吞球即时结算效果，保留显式金额语义，不经过撞击倍率累计。

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

## 通用批量生成 `DeviceSpawnBatch`

弹珠回调和装置事件都可调用 `api:CreateSpawnBatch()`。返回对象只在本次回调有效；它先保存普通生成请求，直到 `TryCommit()` 才创建实体与物理球。引擎不包含环形、数量 16、蓄力 8 次或特定装置名称。

| API | 契约 |
| --- | --- |
| `Radius` | 此批新球的世界半径，取当前内容配置中 `BALL_RADIUS` 的默认值并换算。用于 Lua 计算位置间隔。 |
| `Count` | 已接纳的请求数量。每批最多 256 颗，同时受会话总弹珠上限约束。 |
| `TryAdd(position, velocity, value)` | 添加世界位置、完整世界速度、基础价值；无实际生成。速度要求有限且非零，价值有限且非负。拒绝批内距离小于两球半径加 0.001 的重叠请求；失败不加入队列。 |
| `TryCommit()` | 一次性提交所有请求；空批、重复提交、装置未放置/已移除/无物理视图、非运行阶段、尚无有效固定步长、排空超时或超出总上限返回 `false`。物理生成检查实体阻挡，任一球失败则移除本批此前创建的全部 ECS/物理球并恢复数量；异常也执行清理后传播。只允许提交一次，重试请在新回调创建新批。 |

新球是独立普通球：基础价值采用请求值并遵守属性上下限，半径、质量、弹性、重力、寿命、速度上限等采用内容配置的默认值；不复制接触球或玩家当前药物/倍率/滞留状态。出射速度遵守新球速度上限。每球获得独立身份与寿命、零初始角速度、`SpawnSourceId = 当前实例 ID`，复制/分裂后的后代继续保留该来源信息。

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

`RandomRange` 与 `Roll` 共用当前回合的随机状态。同样的种子和调用次序得到相同结果，不使用 Unity 全局随机数或 Lua `math.random`。非法上下界（NaN、无穷、下界大于上界或跨度溢出）抛出异常且不消耗随机状态。

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

引擎仅向已放置且有有效物理视图的装置分发以下事件。暂停时不推进模拟时间；建造、商店、回合结束阶段不会运行这些事件。事件参数每次新建，用完关闭，缓存后再调用会抛出异常。

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
