# 装置实现参考

本页从原装置 Lua API 文档中分离，保留现有装置的组合方式、制作路径及历史验证说明。接口的类型、参数、返回值和生命周期统一以 [装置 Lua API 参考](Device_Lua_API.md) 为准。

带日期、断言数量和包数量的段落是当时的验证记录，不代表本次文档整理重新执行了验证。修改效果时以制作 Lua 和当前 MRes 为准；全局结算规则见 [Balance.md](Balance.md)。

## 索引

- [超大杯全冰美式](#icedamericano)
- [台球](#billiards)
- [传送门：成对购买，保持世界运动方向和速度](#portal)
- [大炮：手动滞留、旋转与批量发射](#cannon)
- [反重力：上方出射与限时重力分别组合](#antigravity)
- [黏黏：滞留后向下释放](#sticky)
- [其他已接入的效果](#motionexamples)
- [降降火](#coolingtea)
- [有锅气](#wok)
- [种植牙](#dentalimplant)
- [星星灯](#starlamp)
- [大胃袋](#gluttonbag)
- [雪糕机](#icecreammachine)
- [less is more](#lessismore)
- [more is more](#moreismore)
- [故障装置](#malfunction)
- [摇钱树：无碰撞经过计数](#moneytree)
- [赚赚：按触发历史兑换金币](#cashout)
- [猪猪存钱罐：按钱包档位提供倍率](#piggybank)
- [沙包：连续撞击加值，连击越高窗口越短](#sandbag)
- [矿老板：减值换金币，每座矿每回合独立限产](#goldmine)
- [淘金热：单球运输十秒，随机返还或消耗](#goldrush)
- [造血工厂](#bloodfactory)
- [弹珠机关枪](#marblemachinegun)
- [草皮](#turf)
- [复制体](#duplicator)
- [分流器](#splitter)
- [大喷菇](#puffmushroom)
- [炸弹](#bomb)
- [大石碎胸口：八次蓄力、环形发射、冷却后重启](#stoneburst)
- [弹弹：镜面反射方向附近随机弹出](#reflector)
- [吸吸：聚集后共同向下释放](#collector)
- [加速力场](#accelerationfield)
- [黑洞：吞噬充能，点击释放全局衰减引力](#blackhole)

<a id="icedamericano"></a>

## 超大杯全冰美式

业务 ID `9032`，源脚本 `MResPacker/Authoring/Lua/IcedAmericano.lua`，运行时 `Resources/Devices/9032_IcedAmericano.mres`。模型为大杯咖啡、六块冰和吸管，使用实体碰撞体，弹珠正常反弹。

每次有效碰撞将当前基础价值乘 **0.5**，保留小数，同时增加 **20 点额外 RUSH 贡献**。通用装置触发仍加 5：新球贡献从 2 变为 27，第二次碰撞后变为 52。此时不直接增加 RUSH 条；到底部按贡献入账，到得分区按贡献 ×1.5 入账。价值为 0 也可触发；同一步重复碰撞通知只处理一次，之后再次撞击可继续生效。

Lua 组合已有 `Visit(0, 0)`、`TryUse()`、`SetCurrent("BALL_BASE_VALUE", api.Value * api.Parameter)` 和 `AddRushContribution(api.Strength)`，无需新增引擎接口。`SetCurrent` 转换包含属性修正后的基础价值，不重复应用旧价值修正；累计得分倍率、物理运动、其他属性保持原状。RUSH 期间临时 +5 独立计算，例如基础价值 10 在碰杯后变为 5，当时有效价值为 10，RUSH 结束后为 5。

`game/device.json` 的 `Value=0.5` 为保留价值比例，`Strength=20` 为额外贡献。每次成功处理扣 1 耐久，最后一次完整生效，耗尽后本回合停用。默认普通、耐久 20（支持 -1）、购买价 5、出售价 120、初始库存 0。

2026-10-14 验证：冰美式专项 **357** 条断言、完整玩法回归 **10341** 条通过，元数据/耐久 **291** 条，全部 **35** 个 MRes 加载通过。覆盖小数与零价值、重复/同时碰撞、最终和无限耐久、回合恢复、已有属性修正、RUSH 临时价值、底部/得分区入账，以及两种固定步下的真实倾斜棋盘碰撞。

<a id="billiards"></a>

## 台球

业务 ID `9031`，源脚本 `MResPacker/Authoring/Lua/Billiards.lua`，运行时 `Resources/Devices/9031_Billiards.mres`。制作源包含绿色台面、框架、洞口与装饰球的模型、Prefab、材质、配置和 `BilliardsAuthoring.unity`。

一次吞入一球，无碰撞隐藏 0.2 秒，然后向棋盘下方吐出一颗编号球，速度至少 6（保留更高的入口速度），基础价值、尺寸、质量和原有效果保留。8 号概率 20%，1～7 号均分剩余 80%，编号和颜色在球上显示。进入得分区时，黑八使全盘条值减 100、最低到 0，其他编号使条值增加 `编号×10`；这替代普通贡献入账，不另外乘 1.5。黑八不结束已激活的 RUSH。

吐出成功才扣 1 耐久并开始 15 秒冷却；吞入中和冷却时其他球直接经过，受阻则继续短暂滞留并重试，重试不重复抽号或扣耐久。每回合重置冷却。`game/device.json`：`Value=0.2` 为滞留秒数，`Strength=6` 为最低吐球速度，`Range=15` 为冷却。普通、购买 5、出售 120、耐久 20（支持 -1）、初始库存 0。

2026-10-14 重构验证：完整玩法回归 **9978** 条断言通过，RUSH 专项 **63** 条、台球专项 **1649** 条、有锅气 **497** 条、降降火 **337** 条；元数据/耐久 **286** 条，全部 **34** 个 MRes 加载通过。覆盖真实台球吞吐、文字/颜色与物体池清理、出口受阻重试、无限/最后耐久，以及两种固定步下的物理行为。

<a id="portal"></a>

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

<a id="cannon"></a>

## 大炮：手动滞留、旋转与批量发射

制作源为 `MResPacker/Authoring/Lua/Cannon.lua`，成品为 `MResPacker/Output/Cannon.mres`，游戏包为 `Resources/Devices/9007_Cannon.mres`。圆柱底座配朝向本地 -Z 的炮管，炮口方向与物理方向一致。

大炮使用以下通用能力；完整接口契约见 [API 参考](Device_Lua_API.md)：

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

<a id="antigravity"></a>

## 反重力：上方出射与限时重力分别组合

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

<a id="sticky"></a>

## 黏黏：滞留后向下释放

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

<a id="motionexamples"></a>

## 其他已接入的效果

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

<a id="coolingtea"></a>

## 降降火

制作源 `MResPacker/Authoring/Lua/CoolingTea.lua`，业务 ID `9030`，游戏包 `Resources/Devices/9030_CoolingTea.mres`。保留青瓷凉茶壶、壶嘴、弧形把手和壶盖的模型、独立材质、Prefab 与制作场景。

首次有效实体碰撞将当前 `BALL_FRICTION ×1.2`、`BALL_BOUNCE ×0.9`，记录 `RushDurationBonus=5`，扣 1 耐久。只有该球在底部/得分区累满 RUSH 条时，才使本次 RUSH 由 6 秒变为 11 秒；未累满、已在 RUSH 中或处于结束后的 3 秒冷却均不加时。物理参数换算、同球一次、跨实例去重保持原规则。

Lua 用共享定义键 `-(4294967296 + DefinitionId)` 和 `Visit(key, 1, true)`；复制/等分/按价值分球继承效果和防重复记录。`SetCurrent` 以实际当前物理属性换算，清除对应旧修正，其他属性不变；下个物理步同步 PhysX 静/动摩擦与弹性，不改已经求解的反弹速度。新球默认额外时长 0，球消失时清理。`Strength=.2`、`Range=.1`、`Value=5` 的数值不变。

<a id="wok"></a>

## 有锅气

业务 ID `9029`，源 `MResPacker/Authoring/Lua/Wok.lua`，运行时 `Resources/Devices/9029_Wok.mres`。敞口锅、双把手、传感器模型保持。

1. 成功入锅调用 `Motion:TryHold(1)`：可见、无碰撞，暂停平移与旋转。
2. `AddRushContribution(10)` 给当前球记录额外贡献，加上这次成功装置作用的 5，共增加 15。普通新球从 2 变为 **17**；未落底之前不改变全盘条值，其他球不受影响。不同锅可继续给同一颗球增加贡献。
3. 完整 1 秒后使用新的 `OnHoldEnd`，向棋盘上方 ±60° 随机抛出，保留入口速度。出射受阻时原位恢复该方向速度。滞留结束不会再次计入同一装置作用。
4. 每颗成功释放扣 1 耐久，入锅、取消不扣。最后一颗离锅后保持原有 **5 秒冷却**，冷却时穿过；结束后可再次入锅并再次增加贡献。同锅容量按剩余耐久预留，最后一点完成释放，-1 无限。

`game/device.json` 的 `Strength=10` 为额外贡献，`Range=60` 为抛射角，`Value=1` 为翻炒时长。每回合清理滞留与冷却；球上已获得的贡献随球存活，不随离锅撤销。普通、购买 5、出售 120、耐久 20。

<a id="dentalimplant"></a>

## 种植牙

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

<a id="starlamp"></a>

## 星星灯

制作源 `MResPacker/Authoring/Lua/StarLamp.lua`，配置 `Authoring/Profiles/StarLamp.asset`，成品 `Output/StarLamp.mres`，运行时 `Resources/Devices/9027_StarLamp.mres`。模型为沿棋盘平面铺开的立体五角星，深色边框、暗金关闭面和黄色发光开启面，只有 Trigger 区域，弹珠直接穿过。

1. 未激活时，每颗弹珠每次进入切换当前灯一次；停留不重复触发，离开重入可再切换，同球同一步报告去重。至少有 2 盏已放置星星灯，且**全部点亮**才开始一轮效果；3 盏只亮 2 盏没有倍率加成。其他类型、背包库存和预览不计。
2. 最后一盏点亮时，Lua 只注册一份 `Board:AddScoreMultiplier(1 + 灯总数, 5)`，为**全盘所有弹珠**临时增加灯总数点装置倍率。3 盏灯为 +3，普通新球当前 ×4；球已持有 ×1.5 时为 ×4.5。包括没有经过灯的球、新发射和复制/分裂的球，作用范围不限于灯区。按激活时数量确定本轮增量，到得分区按当时是否仍处于效果期计算。
3. **持续 5 秒**，参与灯保持亮着，弹珠经过不切换、不叠加、不刷新截止时间。结束时移除临时全盘加成、所有参与灯熄灭，并且**每盏参与灯各扣 1 耐久**；不按弹珠数量或经过次数扣。未凑齐全亮时免费，最后一点耐久完整支持整轮 5 秒；`-1` 无限。
4. 熄灭后，可重新逐盏点亮开始下一轮。耗尽的灯关闭且无法切换，仍放在场上时计入“全部灯”条件，需要下回合恢复或在布置阶段收回；不会保留旧规则的“耗尽亮灯”。每回合开始所有灯关闭、恢复耐久，未完成的上一回合效果取消，不额外扣费。
5. 通过通用装置事件把效果 ID 和截止时间分发给参与灯，各自保存普通回合数值，不保存旧上下文。模型重建通过 `Start()` 恢复外观。倒计时遵循游戏模拟时间，暂停不会推进；来源装置被移除/收回会取消全盘效果，其余灯检测到取消后熄灭、不扣完成耐久。
6. 临时加成不会永久写入球的累计倍率；5 秒结束后，即使球还没结算也会失去这份加成，保留其他已获得的装置增量。正常计分、赌博计分、弹珠信息和赚赚兑换都读取当前有效倍率；只有一次全盘效果，不会因为有多盏灯而再乘或叠加多份。

`Authoring/Data/StarLamp.device.json`：ID 9027，`Value = 5` 为持续秒数（Lua 限制最多 60 秒），`Strength = 1` 为每盏灯的附加增量，`Range` 未使用。这里业务 `Value` 是时长，metadata `value` 仍是售价。默认普通、20 耐久、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Star Lamp Device` 定位配置。

验证源 `Editor/Validation/GameContentValidation.StarLamp.cs`、`GameContentValidation.StarLampApi.cs`、`GameContentValidation.StarLampPhysics.cs` 覆盖全亮条件、同步窗口、未经过灯和新产生的球、永久/临时倍率合成、复制分裂不永久继承、正常/赌博结算与兑换、有限/无限耐久、回合与来源清理、接口有效期，以及两种步长下真实穿过、停留、高速穿越、展示重建和统一熄灯。

<a id="gluttonbag"></a>

## 大胃袋

制作源 `MResPacker/Authoring/Lua/GluttonBag.lua`，配置 `Authoring/Profiles/GluttonBag.asset`，成品 `Output/GluttonBag.mres`，运行时 `Resources/Devices/9026_GluttonBag.mres`。模型为橄榄绿圆柱袋身、浅褐色袋口与牙齿、深色口部，袋身有实体凸碰撞。

1. 先放置大胃袋，布置阶段从背包拖入其他装置；普通、稀有、史诗、传奇分别增加 **2、4、6、8 点附加增量**，消耗提交的单件。不同大胃袋独立成长，喂入另一台大胃袋也只按其稀有度计算，不转移它的成长。
2. 初始增量为 0，不产生效果。喂入普通装置后增量为 2，弹珠碰撞调用 `AddScoreMultiplier(3)`，实际增加 2；同球两次为 `1 + 2 + 2 = 5`。四档各喂一件共增加 20，碰撞提供完整 ×21。重复碰撞继续累加，到得分区统一结算，基础价值不直接修改；仍遵守通用倍率上限 1000。
3. 喂食不扣耐久；有正增量的真实碰撞每次扣 1，同球同一步重复报告去重，不同球各自生效。无成长、Trigger 和连线不生效；最后一点耐久完整作用，`-1` 无限。弹珠正常反弹。
4. 成长用于即将开始的这一回合，进入该回合保留，下一轮布置清空。界面在拖放前提示会消耗库存，接受后显示成长；失败、取消、越界、松开在控件上或离开布置阶段时保留物品。实体物品板与旧 uGUI 背包均支持拖放，拖到合法空白区域仍可正常放置。

`Authoring/Data/GluttonBag.device.json`：ID 9026，`Value = 0` 为初始附加增量，`Strength = 2` 为每档稀有度增量，Lua 使用档位 1/2/3/4；`Range` 未使用。`Strength <= 0` 时拒绝喂食。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Glutton Bag Device` 定位配置。

验证源 `Editor/Validation/GameContentValidation.GluttonBag.cs`、`GameContentValidation.GluttonBagUI.cs` 覆盖四档稀有度、原子提交和回调异常、库存实例校验、配对物品、回合准备与清空、独立实例、耐久、真实拖放控件和两种步长下的实际物理碰撞与得分结算。

<a id="icecreammachine"></a>

## 雪糕机

制作源 `MResPacker/Authoring/Lua/IceCreamMachine.lua`，配置 `Authoring/Profiles/IceCreamMachine.asset`，成品 `Output/IceCreamMachine.mres`，运行时 `Resources/Devices/9025_IceCreamMachine.mres`。模型为粉色圆柱底座、三层奶油色圆柱雪糕和薄荷色顶饰，底座有实体凸碰撞。

1. 每台独立记录本回合成功触发次数 `n`，本次完整倍率为 `max(1, 10 - n × 1)`。首次 `n = 0`，依次提供 ×10、×9、×8、×7、×6、×5、×4、×3、×2，之后最低 ×1。同台由不同弹珠共享次数，不同实例互不影响。
2. Lua 调用 `AddScoreMultiplier(本次完整倍率)`，引擎只增加“倍率 − 1”。同一颗球前两次累计倍率为 `1 + 9 + 8 = 18`，全部九次后为 `1 + 9 + 8 + … + 1 = 46`；不是把这些倍率连乘。碰撞时不结算，到得分区统一计算。
3. 倍率大于 1 才调用 `Visit(0, 0)`、`TryUse()` 登记，施加倍率后次数加 1；默认前九次各扣 1 耐久，降至 ×1 后不生效、不登记成功触发、不扣耐久，不改变球已经获得的倍率。最后一点耐久完整生效；无限耐久也不会绕过本回合的倍率递减。
4. 同球同一步重复碰撞报告去重，不同球同一步按处理顺序取得连续档位；持续贴住、Trigger 和连线不生效。原球正常反弹，基础价值、钱包、血量和运动均不修改。此前归零的累计倍率也可获得新的增量。
5. Lua 只跨回调保存 `roundId` 和 `uses` 普通数值，`RoundId` 变化时重置次数为 0；同回合收回再放置不补充次数。状态显示本回合已触发次数、下次完整倍率和用尽提示。默认耐久 20，因此正常九次后剩余 11 耐久，但倍率已降至 ×1；新回合次数和耐久均重置。

`Authoring/Data/IceCreamMachine.device.json`：ID 9025，`Value = 10` 为初始完整倍率，`Strength = 1` 为每次有效触发后的减量，`Range` 未使用。支持小数；初始倍率不大于 1 时无效果，减量为 0 时一直提供初始倍率，减量跨过 1 时钳制到 1。这里的业务 `Value` 与 metadata 的出售价 `value` 不同。默认普通、20 耐久、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Ice Cream Machine Device` 定位配置。

无需新增引擎 API，计数和递减公式由 Lua 组合已有能力。`Editor/Validation/GameContentValidation.IceCreamMachine.cs` 验证全部倍率档位、重复与同时碰撞、实例和回合隔离、收回再放置、无效事件、有限/无限耐久、参数、归零后恢复和最终结算，以及两种步长下真实倾斜棋盘的 11 次重新碰撞与反弹。

<a id="lessismore"></a>

## less is more

制作源 `MResPacker/Authoring/Lua/LessIsMore.lua`，配置 `Authoring/Profiles/LessIsMore.asset`，成品 `Output/LessIsMore.mres`，运行时 `Resources/Devices/9024_LessIsMore.mres`。模型为紫色圆柱底座与三根逐渐降低的橙银色立柱，底座有实体凸碰撞。

1. 每次真实碰撞用 `CountPlacedDevices(api.InstanceId)` 查询其他已放置装置数 `n`，本次附加倍率为 `max(0, 20 - n × 2)`。不计自身，耗尽但仍在场的装置计入，库存不计；配对门按实际已放置的实例分别计数。
2. 附加倍率是实际增加量，Lua 调用 `AddScoreMultiplier(1 + 附加倍率)`。没有其他装置时增加 20；3 个时增加 14，球初始倍率为 1，撞击一次为 15、两次为 29；9 个时增加 2，10 个及以上不增加。20 是单次增量上限，重复碰撞仍继续累加，到得分区统一计算。
3. 正增量的有效碰撞扣 1 耐久，零增量不登记触发、不扣耐久，也不减少弹珠之前已有的倍率。同球同一步重复报告去重，不同球独立生效；持续贴住、Trigger 和连线不生效。最后一点耐久完整作用，`-1` 无限。原球正常反弹，基础价值、钱包、血量和运动不修改。
4. 状态实时显示其他装置数和本次增量；放置、收回或移除只影响之后的碰撞，不追溯修改已获得的倍率。每个实例只排除自己，新回合恢复耐久并按当前布局查询；此前被归零的累计倍率也能重新增加。

`Authoring/Data/LessIsMore.device.json`：ID 9024，`Value = 20` 为没有其他装置时的附加倍率和单次增量上限，`Strength = 2` 为每个其他装置的减量，`Range` 未使用。支持小数；`Value <= 0` 无效果，`Strength = 0` 表示不随数量衰减。这里的业务 `Value` 与 metadata 的出售价 `value` 不同。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Less Is More Device` 定位制作配置。

无需新增引擎 API；数量查询、去重、耐久登记和倍率累加使用已有通用能力，衰减公式由 Lua 组合。`Editor/Validation/GameContentValidation.LessIsMore.cs` 覆盖数量边界、动态布局、库存与耗尽、重复及跨实例累加、回合与有限/无限耐久、归零后恢复、参数，以及两种步长下真实倾斜棋盘的反弹、零增量边界和结算。

<a id="moreismore"></a>

## more is more

制作源 `MResPacker/Authoring/Lua/MoreIsMore.lua`，配置 `Authoring/Profiles/MoreIsMore.asset`，成品 `Output/MoreIsMore.mres`，运行时 `Resources/Devices/9023_MoreIsMore.mres`。模型为蓝色圆柱底座与三根逐渐增高的青金色立柱，底座有实体凸碰撞。

1. 每次真实碰撞查询其他已放置装置数 `n`，本次附加倍率为 `min(20, n × 2)`；不计自身，耗尽但仍在场的装置照常计数，库存里的装置不计。
2. “附加倍率”是直接增加量。Lua 调用 `AddScoreMultiplier(1 + 附加倍率)`，让引擎恰好增加该数值。3 个其他装置每次增加 6；球初始倍率为 1，碰撞一次为 7、两次为 13，到得分区统一计算。
3. +20 是单次作用上限，达到 10 个其他装置后饱和，继续放置不提高单次增量。重复撞击仍按通用规则累加，不将弹珠的最终累计倍率限制为 20。没有其他装置时增量为 0，不生效、不扣耐久。
4. 有正增量的有效碰撞扣 1 耐久；同球同一步重复报告去重，不同球同一步各自生效，持续贴住、Trigger、连线不生效。最后一点耐久仍完整作用，`-1` 无限。原球正常反弹，基础价值、钱包、血量和运动均不修改。
5. 状态显示实时数量和本次增量。盘面增减只影响之后碰撞，不追溯修改球已经获得的倍率；各实例只排除自己，每回合按当前盘面重新查询。归零后的弹珠也可获得正常增量。

`Authoring/Data/MoreIsMore.device.json`：ID 9023，`Value = 20` 为单次附加倍率上限，`Strength = 2` 为每个其他装置的增量；`Range` 未使用。上限或增量非正时无效果；支持小数参数。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate More Is More Device` 定位制作配置。

`Editor/Validation/GameContentValidation.MoreIsMore.cs` 覆盖 0/1/9/10/11/20 个装置、上限与重复累加、库存/耗尽/移除/传送门计数、独立实例、动态布局、回合与耐久、归零后恢复、查询有效期和副作用，以及两种步长下真实倾斜棋盘的碰撞、反弹和结算。

<a id="malfunction"></a>

## 故障装置

制作源 `MResPacker/Authoring/Lua/Malfunction.lua`，配置 `Authoring/Profiles/Malfunction.asset`，成品 `Output/Malfunction.mres`，运行时 `Resources/Devices/9022_Malfunction.mres`。模型为灰色金属圆柱、黄色警戒环、红色警报灯和倾斜断杆，圆柱有实体凸碰撞。

- 每次真实碰撞从 `{0, 1, 2, 3, 5, 7, 11, 13}` 等概率抽取一个结果，每项概率 1/8。Lua 用 `floor(RandomRange(0, 8)) + 1` 选择索引，使用本局随机序列，不使用独立的 `math.random`。
- 抽到 0：调用 `SetDeviceScoreMultiplier(0)`，清空先前所有装置贡献，将当前累计倍率置为 0；保持基础价值和物理运动，后续仍可增加。抽到 1 不增加累计倍率；其余调用 `AddScoreMultiplier(result)`，每次增加 `result - 1`。
- 例：球价值 10，依次抽到 13 → 0 → 1 → 3，累计倍率依次为 13 → 0 → 0 → 2，进入普通得分区最终得到 20。若归零后未获得新倍率，价值部分按 ×0 结算。没有独立的归零标记，也不改写原有固定加分、黑洞显式金额等结算规则。
- 组合 `Impact`、`Visit(0, 0)`、`TryUse()`、随机接口与倍率接口。八种结果均扣 1 耐久，最后一次仍完成抽取；`-1` 无限。重复碰撞可再次抽取，同球同一步重复报告只算一次；不同球同一步各抽一次。持续接触、Trigger、连线及耗尽耐久不抽签、不推进随机状态。
- 每次有效处理均调用 `MarkTriggered()`，抽到 1 或再次归零也登记效果历史。原球正常反弹，不消耗、不传送、不改价值、不直接计分。装置显示最近一次结果，每回合清空显示；球的累计倍率随复制/分裂按通用规则继承。

`Authoring/Data/Malfunction.device.json`：ID 9022，默认普通、耐久 20、购买价 5、出售价 120、初始库存 0；本装置不使用 `Value` / `Strength` / `Range`。候选倍率在 Lua 顶部 `factors` 修改，等概率随列表长度计算。菜单 `Tools > MRes > Create or Locate Malfunction Device` 定位制作配置。

`Editor/Validation/GameContentValidation.Malfunction.cs` 验证全部八种结果、固定种子序列、归零与恢复、跨装置累加、得分区/赌博区结算、复制分裂继承、有效历史、同帧去重、有限/无限耐久、回合重置、接口范围与有效期，以及两种步长的真实倾斜棋盘碰撞和正常反弹。

<a id="moneytree"></a>

## 摇钱树：无碰撞经过计数

制作源 `MResPacker/Authoring/Lua/MoneyTree.lua`，配置 `Authoring/Profiles/MoneyTree.asset`，成品 `Output/MoneyTree.mres`，运行时 `Resources/Devices/9014_MoneyTree.mres`。模型为树干、绿色树冠和金币果实，只有一个 `isTrigger = true` 的 BoxCollider；树干、树冠和金币均无实体碰撞，不阻挡或弹开弹珠。

- 每颗弹珠每次进入区域计 1，停留不持续增加；离开后再次进入可重新计数。物理桥对进入、Stay 与高速扫掠去重，Lua 再组合 `Visit(0, 0)` 排除同一球同一步重复事件。实体撞击和连线事件不计。
- 每累计 10 次，组合 `TryUse()` 和 `AddCoins(5)` 发放金币并扣 1 耐久，计数减去 10 后继续积累。前 9 次不扣耐久；同一步多颗球可完成多批，每批各扣 1。第 20 批仍发放完整奖励，之后本回合停止计数；`-1` 无限耐久。
- 不消费弹珠，不修改位置、速度、价值或装置倍率，不计回合分数、不返还血量。金币直接进钱包并遵守钱包上限。每次有效累积调用 `MarkTriggered()` 记录纯 Lua 效果，因此经过摇钱树的弹珠可被后续“赚赚”兑换。
- 每个装置实例独立保存普通 Lua 数字（回合 ID、当前计数、产出批数），新回合清零，未满 10 次的进度不结转。`OnTick` 显示进度和本回合累计产出。

`Authoring/Data/MoneyTree.device.json`：`Value = 10` 为每批经过次数，`Strength = 5` 为每批金币，均向下取整；次数须在 1–10000，金币至少 1，否则不计数、不扣耐久。`Range` 未使用；触发区域尺寸在制作 Prefab 的 BoxCollider 配置。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

此效果复用现有 API，没有增加专用引擎能力。`GameContentValidation.MoneyTree.cs` 验证 9/10/20 次边界、重入、同一步多球、实例独立、回合清零、有限/无限耐久、参数和钱包上限，以及倾斜棋盘的真实穿过、长时间停留、高速扫掠和无物理阻挡。

<a id="cashout"></a>

## 赚赚：按触发历史兑换金币

制作源 `MResPacker/Authoring/Lua/CashOut.lua`，配置 `Authoring/Profiles/CashOut.asset`，成品 `Output/CashOut.mres`，运行时 `Resources/Devices/9013_CashOut.mres`。模型为金色立柱、绿色横梁和金币标志的兑换口，根节点 BoxCollider 为入口触发器。

- 进入时先查询 `HasTriggeredOtherDevice`。存在其他装置有效作用时，金币为 `floor(Value × EffectiveDeviceScoreMultiplier + 0.5)`，**没有额外 ×2**。例如价值 10、累计装置倍率 ×2，返还 20 金币；价值 10.5、倍率 ×1，返还 11 金币。使用当前 `Value`（含基础价值修正），累计倍率按 `1 + Σ(倍率 - 1)` 计算。
- Lua 组合 `TryConsume(false)` 与 `AddCoins`，吞噬成功后只加一次金币，不计回合得分、不返还发射血量、不触发同一步的得分区结算。零价值也消耗；金额遵守钱包上限。`AddCoins` 本身仍向下取整，四舍五入由本装置 Lua 完成。
- 无其他装置有效作用时，组合 `Motion.Source.Down` 与 `TryLaunch`，从棋盘下方出口射出，保留进入速度大小（包括零速，仍受弹珠速度上限约束）。受阻则留在原处、不给金币、不扣耐久，后续重新进入可重试。不把当前实例以前的放行算作其他装置。
- 两种成功处理均通过 `TryUse()` 扣 1；第 20 次完整执行，之后本回合失效，`-1` 无限。`Visit(0, 0)` 排除同一步重复进入，连线事件不参与。不同球可在同一步分别处理。

Metadata 默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。`game/device.json` 的 `Value`、`Strength`、`Range` 对此效果不使用。`GameContentValidation.CashOut.cs` 覆盖四舍五入、累计倍率、自己与其他实例、失败访问、纯 Lua 蓄力、复制与新球、钱包上限、耐久/跨回合、真实入口与旋转棋盘出射、真实力场历史及对象池清理。

<a id="piggybank"></a>

## 猪猪存钱罐：按钱包档位提供倍率

制作源 `MResPacker/Authoring/Lua/PiggyBank.lua`，配置 `Authoring/Profiles/PiggyBank.asset`，成品 `Output/PiggyBank.mres`，运行时包 `Resources/Devices/9012_PiggyBank.mres`。模型为粉色陶瓷小猪，包含投币口、猪鼻、耳朵、四脚和尾巴，使用实体 CapsuleCollider。

- 每次真实撞击读取 `Coins`，计算 `n = floor(Coins / 10)`，提供 `1 + 0.1 × n` 的倍率，引擎累计其中的 `0.1 × n` 增量。例如 25 金币提供 1.2，撞两次为 `1 + 0.2 + 0.2 = 1.4`；之后钱包剩 15 金币，再撞一次加 0.1，最终倍率 1.5。钱包改变不追溯修改以前的贡献，也不消耗金币。
- 组合 `Impact`、`Visit(0, 0)`、`TryUse()` 和 `AddScoreMultiplier`。金币不足 10 时不生效、不扣耐久；有效撞击扣 1，最后一次仍完整提供倍率，`-1` 无限。持续贴住、同球同一步重复事件、Trigger/连线不算新撞击，不设公共冷却，同一步不同球各算一次。
- 不消耗弹珠、不改基础价值或速度，得分区使用累计总和。实例分别计算耐久，球分别保存倍率；新回合清理球状态并恢复耐久。`OnTick` 显示当前钱包对应的本次撞击倍率。

`Authoring/Data/PiggyBank.device.json`：`Value = 10` 为每档金币数（正数），`Strength = 0.1` 为每档提供的倍率增量（正数），`Range` 未使用。Metadata 普通、耐久 20、购买价 5、出售价 120、初始库存 0。效果遵守已有总倍率和得分上限；大额钱包不会使倍率调用溢出或抛错。

`GameContentValidation.PiggyBank.cs` 验证金币边界、动态钱包、倍率减 1 后累加与混合装置、普通得分区、通用触发修正、旧倍率钉、复制继承、上下文有效期、耐久和真实物理碰撞。

<a id="sandbag"></a>

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

<a id="goldmine"></a>

## 矿老板：减值换金币，每座矿每回合独立限产

制作源 `MResPacker/Authoring/Lua/GoldMine.lua`，配置 `Authoring/Profiles/GoldMine.asset`，输出 `MResPacker/Output/GoldMine.mres`，运行时包 `Resources/Devices/9010_GoldMine.mres`。模型为直径 0.5、高 0.2 的石质圆柱，带金色矿脉和顶部金块装饰；实体凸 MeshCollider 负责正常碰撞。

- 每次真实撞击给当前弹珠基础价值减 2，最低降到 0，同时立即给玩家加 4 金币。价值为 0 或不足 2 也会产出完整 4 金币。球保留，正常反弹，之后以剩余价值结算。
- 每座矿独立累计本回合产出的金币，默认最多 20，即成功触发 5 次。达到上限后的撞击不减值、不加金币、不扣耐久，仍保留实体碰撞。新回合恢复产出额度；卖掉或移除后随实例清理 Lua 状态。
- `OnContact` 先检查 `Impact`、连线和剩余额度，再组合 `Visit(0, 0)`、`TryUse()`、`AddBase`、`AddCoins`。同球同一步去重，不使用装置公共冷却；不同球同一步撞击按接触顺序共享这座矿的剩余额度。持续贴住不反复触发。
- 每次实际产出扣 1 耐久，最后一点仍完整生效；`-1` 无限耐久也遵守每回合 20 金币上限。`OnTick` 与接触回调按 `RoundId` 重置产量，状态显示“本回合 n/20 金币”，采完后提示“已采完”。
- 复用现有属性 API：`AddBase("BALL_BASE_VALUE", -2)` 修改未修正的基础值，再计算现有属性修正，与沙包一致。例如基础 10、价值倍率 ×2 时，采矿后基础 8、当前值 16；移除倍率后仍为 8。不会把已有倍率写回基础值。`AddCoins(4)` 直接增加玩家金币，不是回合得分，也不套用兑现时的金币收益倍率；遵守通用钱包上限。

`Authoring/Data/GoldMine.device.json`：`Value = 2` 为每次基础价值扣除量，`Strength = 4` 为单次金币产量，`Range = 20` 为每回合产出上限；后两项按整数取整，非正产量/额度不生效，负扣除量不生效。自定义上限不是产量整数倍时，最后一次只发剩余额度，仍扣一次价值和耐久。这里的 `Range` 是金币额度，不是空间距离。Metadata 的 `value` 仍为单件出售价，与上述 `Value` 无关。

默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。没有新增专用引擎 API。`Editor/Validation/GameContentValidation.GoldMine.cs` 随统一验证执行，覆盖低值/零值、实例和回合隔离、同时碰撞、额度耗尽、最后一点/无限耐久、价值修正与正常结算，以及真实 PhysX 接触和持续贴住去重。

<a id="goldrush"></a>

## 淘金热：单球运输十秒，随机返还或消耗

制作源 `MResPacker/Authoring/Lua/GoldRush.lua`，配置 `Authoring/Profiles/GoldRush.asset`，输出 `MResPacker/Output/GoldRush.mres`，运行时包 `Resources/Devices/9011_GoldRush.mres`。外观为木支架人工矿洞、轨道和洞口停放的矿车，入口为 BoxCollider 触发器，放置半径 0.6。

1. 空闲时接收一颗弹珠，组合 `Visit(0, 0)`、`Motion:TryHold(10)` 和 `Motion:TrySetHeldVisible(false)` 开始运输。等待期间不参与物理碰撞或结算；同一矿洞拒绝其他球，入库不扣耐久、不抽随机数。多球同一步进入也只收第一颗，其他球正常经过。
2. 等待按模拟时间计算，满 10 秒后的首个固定步进入 `OnHoldEnd`。用新的上下文调用一次 `RandomRange`，三种结果各占 1/3：返还原弹珠并加 2 金币；消耗弹珠并加 5 金币；消耗弹珠且没有金币。后两种使用 `TryConsume(false)`，不计分、不返还发射血量。
3. 返还保留弹珠原价值、属性和进入时的速度大小，用 `TryLaunch(source, source.Down, speed)` 沿棋盘下坡方向出射；受阻则用 `TrySetVelocity` 在原来的安全位置向下恢复运动。两种方式均遵守当前限速。引擎在回调前恢复显示与角速度。
4. 三种结果都在完成时以 `TryUseOnce("trip:" .. tripNumber)` 扣 1 耐久，包括一无所获。一次结果只扣一次，最后一点也完整生效；`-1` 无限。结果出来后恢复接收，返回的同一颗球以后再次进入也能开始新的运输。

Lua 只跨回调保存本回合 ID、球 ID、截止时间、批次编号和显示文字。各库存实例独立；状态显示运输倒计时及最近结果。弹珠消失/到期、装置移除/收回/视图消失或新回合取消运输，不抽结果、不补金币、不扣耐久；寿命和回合排空计时仍正常推进，完成与寿命/排空超时同一步时以清理优先。无球时也通过 `OnTick` 清理失效的忙碌标记。暂停不推进运输时间。

参数 `Authoring/Data/GoldRush.device.json`：`Value = 10` 为运输秒数，范围 `(0, 60]`；`Strength = 2` 为返还弹珠时的金币数；`Range = 5` 为仅金币结果的金币数，后两项取非负整数。概率权重在 Lua 顶部 `returnWeight, coinsWeight, lossWeight = 1, 1, 1`，可编辑为非负权重且总和必须为正。这里的 `Range` 为金币参数，不代表空间距离。Metadata 默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

`Editor/Validation/GoldRushDeviceValidation.cs` 随统一验证执行，覆盖固定随机种子的全部结果、十秒边界、容量/实例隔离、重复运输、最后一次/无限耐久、原球价值/速度、受阻回退、显示所有权及恢复、取消/寿命/回合/对象池清理和真实洞口触发器。

<a id="bloodfactory"></a>

## 造血工厂

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

<a id="marblemachinegun"></a>

## 弹珠机关枪

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

<a id="turf"></a>

## 草皮

制作源 `MResPacker/Authoring/Lua/Turf.lua`，配置 `Authoring/Profiles/Turf.asset`，成品 `Output/Turf.mres`，运行时 `Resources/Devices/9021_Turf.mres`。模型为棕色圆柱土层、绿色草冠与草叶，土层有实体凸碰撞体。

- 首次真实碰撞只记录球 ID、碰撞时的当前基础价值、累计装置倍率和截止时间；5 秒内另一颗不同弹珠碰撞，生成一颗新球。恰好 5 秒仍可配对，超过截止时间清空记录，下一次有效碰撞重新开始。
- 新球价值为 `(首次价值 + 当前价值) / 2`，保留小数；累计装置倍率为两次快照倍率的平均值。例如价值 4 和 10、倍率 2 和 4，生成价值 7、倍率 3 的球，到得分区结算。原球的价值、倍率和正常反弹均不改变。
- Lua 组合 `CreateCopy()`、`TrySetValue()`、`TrySetDeviceScoreMultiplier()` 和 `TrySpawn()`，新球从棋盘下方以速度 6 发出。尺寸、质量、其他属性和剩余寿命沿用第二颗球；不额外抽血，不复制旧固定加分。遵守通用复制代数及在场数量上限。
- 同球重复碰撞不配对、不延长窗口；同一步不同球可组成一对，第三颗开始下一对。成功生成后立即重置并扣 1 耐久，第二颗不会同时成为下一对的第一颗。存入、超时、无效碰撞、失败不扣；最后一点耐久仍完整生成，`-1` 无限。
- 出口阻挡、球数或代数限制导致生成失败时，保留首次快照，等待原窗口内下一次不同球的有效碰撞，不自动逐帧出球。超时照常清空。首次球消失不影响已经保存的数值；各实例独立，每回合清空状态，不保存过期上下文或球对象。
- `HasCopiedFrom(api.InstanceId)` 排除本实例产生的球及其复制/分裂后代。它们仍会物理反弹，但不占配对位置、不扣耐久，可以触发其他装置。

`Authoring/Data/Turf.device.json`：ID 9021，`Value = 5` 是配对窗口秒数，`Strength = 6` 是出射速度。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。菜单 `Tools > MRes > Create or Locate Turf Device` 定位制作配置。

`Editor/Validation/GameContentValidation.Turf.cs` 覆盖价值/倍率快照与平均、小数/零价值、5 秒边界、同球与同帧去重、重置、祖先过滤、独立实例、容量/物理失败重试、有限/无限耐久、球消失/跨回合、接口有效期，以及两种步长下真实倾斜棋盘的碰撞、反弹、出口阻挡和向下出射。

<a id="duplicator"></a>

## 复制体

制作源 `MResPacker/Authoring/Lua/Duplicator.lua`，配置 `Authoring/Profiles/Duplicator.asset`，成品 `Output/Duplicator.mres`，运行时 `Resources/Devices/9018_Duplicator.mres`。模型为浅蓝金属圆柱，使用实体碰撞。

- 原球保留并由物理系统正常反弹。Lua 用碰撞前速度和表面法线，在棋盘平面内计算镜面反射方向；副本按该方向、以碰撞前速度大小射出，尺寸和质量不变。
- 副本初始价值为 0，旧固定加分清空；继承累计倍率，之后可以被其他装置增值、吞噬或触发其效果，到得分区统一结算。
- 每个实例对每颗原球成功复制一次，自己的复制后代（包括再分裂后的后代）不再触发它。这里不触发指不再复制；圆柱仍有实体碰撞，球会正常反弹。
- 成功复制扣 1 耐久，阻挡、数量/代数限制及无效碰撞不扣；失败后原球下次碰撞仍可重试。复制球放在原球反射方向前方，留出两球半径及少量间隔，避免重叠。
- 默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。`Authoring/Data/Duplicator.device.json` 的 ID 为 9018；价值 0 的选择和每球一次规则均在 Lua 中。

`Editor/Validation/GameContentValidation.Duplicator.cs` 验证零价值及固定加分清理、其他装置增值、祖先记录、重试/容量/耐久、回调有效期，以及两种物理步长下的真实倾斜棋盘正撞和斜撞。

<a id="splitter"></a>

## 分流器

制作源 `MResPacker/Authoring/Lua/Splitter.lua`，配置 `Authoring/Profiles/Splitter.asset`，成品 `Output/Splitter.mres`，运行时 `Resources/Devices/9015_Splitter.mres`。模型为圆柱集流仓与三个管口，只有一个 BoxCollider Trigger，所有管口均无实体碰撞。

- 原球进入后，Lua 创建 3 球等分批次；每颗价值、体积和质量各为原球 1/3，继承累计装置倍率。
- Lua 沿棋盘向下及左右各 30° 设置出口，保留进入速度大小；原球静止时输出也为零速。出生距离同时满足装置外部及子球间距要求。
- 完整分流成功扣 1 耐久，第 20 次仍生成全部 3 球；失败保留原球，不扣耐久，下次进入可重试。
- 分出的球及其后代再次经过同一台时**直接穿过**，不反弹、不改速度、不再分裂、不扣耐久。其他分流器实例可再次分流，但仍受通用代数、属性下限和数量上限约束。

`Authoring/Data/Splitter.device.json`：`Range = 30` 为侧口相对向下的角度，支持 1–80°；`Value` / `Strength` 未使用。修改侧口角度时同步调整制作 Prefab 的管口朝向。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

行为验证位于 `Editor/Validation/GameContentValidation.Splitter.cs`，覆盖均分守恒、得分结算、同实例后代穿过、其他实例分流、复制继承、失败回滚、容量、耐久、回合恢复、零速，以及真实倾斜棋盘的三出口与物理阻挡。

<a id="puffmushroom"></a>

## 大喷菇

制作源 `MResPacker/Authoring/Lua/PuffMushroom.lua`，配置 `Authoring/Profiles/PuffMushroom.asset`，成品 `Output/PuffMushroom.mres`，运行时 `Resources/Devices/9016_PuffMushroom.mres`。紫色蘑菇模型带八个扇形喷口，根 BoxCollider Trigger 检测进入，模型和喷口均无实体碰撞。

1. Lua 使用 `CreateSplitBatch(8)`，把原球价值、体积和质量各均分成八份；每颗半径为原球一半，继承累计装置倍率，到得分区才结算。
2. 以 `source.Down * -1` 得到**棋盘平面内向上**的方向，不是离开棋盘的法向 `source.Up`。八个方向覆盖总计 120°，角度从 −60° 到 +60°，相邻间隔 `120 / 7` 度。
3. `source:Rotate` 计算每个方向，`batch:TryAdd` 设置出生位置与入射速度大小；零速仍输出零速。出生距离为 `max(装置半径 + 子球半径 + 0.02, (子球直径 + 0.02) / (2 × sin(相邻夹角 / 2)))`，避免八球重叠。
4. 全部出口成功后才消耗原球，并用 `TryUse()` 扣 1 耐久；第 20 次仍完成全部八球。任一出口受阻时撤销整批，原球保留、不扣耐久，下次进入可重试。
5. `HasSplitFrom(api.InstanceId)` 排除本实例产生的球及后代，再次经过时直接穿过，不反弹、不改变速度、不再分裂、不扣耐久。其他实例仍可分流，受通用属性下限、代数和数量上限约束。

`Authoring/Data/PuffMushroom.device.json` 的 `Range = 60` 表示半扇形角，即两侧各 60°、总计 120°；支持半角 1–90°，符合通用参数上限。`Value` / `Strength` 未使用，出口数量固定为 Lua 顶部 `outlets = 8`。修改扇形角或数量时同步调整制作 Prefab 的喷口。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

现有质量下限为 0.1，因此八等分要求原球质量至少 0.8；默认质量 1 的弹珠可以触发，刚八等分出的默认小球（质量 0.125）不能继续八等分。沿用 `DeviceSplitBatch` 的拒绝规则，失败保留原球和耐久，不强行钳制子球质量。未新增专用引擎 API。

`Editor/Validation/GameContentValidation.PuffMushroom.cs` 验证八等分与计分守恒、120° 上方扇形、可调夹角、来源继承、失败回滚、容量与耐久，以及真实倾斜棋盘上的阻挡、零速/非零速出射和后代穿过。

<a id="bomb"></a>

## 炸弹

制作源 `MResPacker/Authoring/Lua/Bomb.lua`，配置 `Authoring/Profiles/Bomb.asset`，成品 `Output/Bomb.mres`，运行时 `Resources/Devices/9017_Bomb.mres`。模型包含固定投掷底座、隐藏的黑色圆球引信模板 `BombTemplate` 和爆炸闪光模板 `BlastTemplate`。底座有实体碰撞，飞出的炸弹和闪光无碰撞。

- 每回合首次等待 5 秒，此后每 5 秒随机选择棋盘平面 0–360° 方向。以速度 3 投出，1 秒后在准确终点爆炸；暂停冻结投掷及引信时间。
- Lua 的 `OnFuseEnd` 先扣 **1 次耐久，空爆也扣**，然后查询爆点半径 0.8 内的弹珠。投出不扣，取消不扣，最后一次爆炸仍完整处理全部目标。
- `OnBlastBall` 为每颗符合条件的原球创建 `CreateSplitBatch(8)`，价值、体积和质量各均分为八份，继承各自累计装置倍率。围绕原球在棋盘平面每隔 45° 布置一个出口，朝四周以速度 6 散开，遵守子球速度上限；倍率到得分区结算。
- 同一装置产生的后代不再被它分裂；这一爆仍扣耐久。沿用分裂代数、质量/半径下限和容量限制，默认八等分至少需要原球质量 0.8。某颗原球八等分受阻时整颗回滚保留，继续处理其他原球；并非整个范围共同回滚，失败或全不符合条件仍算一次爆炸。
- 范围查询取球形相交和原球快照，不重复选中新碎片；已滞留的球不受影响。爆炸不提前结算，不扣血或生成额外总价值。

`Authoring/Data/Bomb.device.json`：`Value = 5` 为投掷间隔，支持 0.1–60 秒；`Strength = 3` 为投掷速度，`Range = 0.8` 为爆炸半径。Lua 顶部 `flightSeconds = 1`、`scatterSpeed = 6`、`pieces = 8` 控制引信、散射速度和份数。修改爆炸半径时同步调整 `BlastTemplate` 的显示大小。默认普通、耐久 20、购买价 5、出售价 120、初始库存 0。

`Editor/Validation/GameContentValidation.Bomb.cs` 覆盖周期/引信、空爆扣除、最终一次处理多球、随机方向、价值/倍率守恒、失败与容量回滚、来源过滤、取消/回合清理、新回调有效期及异常隔离，以及真实倾斜棋盘的飞行视觉和物理碎片。

<a id="stoneburst"></a>

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

<a id="reflector"></a>

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

<a id="collector"></a>

## 吸吸：聚集后共同向下释放

完整效果位于 `MResPacker/Authoring/Lua/Collector.lua`。制作配置是 `Authoring/Profiles/Collector.asset`，输出 `MResPacker/Output/Collector.mres`，运行时副本为 `Resources/Devices/9003_Collector.mres`。

Lua 组合 `OnBallUpdate`、范围/向量计算、`TrySetVelocity`、带障碍检查的 `TryMove`、`TryHold` 和 `OnHoldEnd`：

1. 第一颗合格弹珠进入范围时启动磁力，保存共同截止时间 `Time + Parameter`。后续弹珠加入同一批，不延长截止时间；范围使用棋盘平面距离，并排除明显高于/低于棋盘的弹珠。
2. 为每颗弹珠保存进入时的速度大小，按各自半径预留相邻、互不重叠的位置。Lua 设置朝向该位置的收拢速度，由正常物理步移动；接近时用 `TryMove` 对齐，并 `TryHold(截止时间 - Time)`。这是一种由 Lua 控制收拢速度的效果；不调用旧磁铁的 `OnField` 力积分。
3. 磁力满 2 秒后，在同一个固定步释放已聚集弹珠，各自设置为 `Source.Down * 进入时速度`。保留聚集位置，不把所有球送到同一个出口；零速仍为零，之后由普通重力继续运动，当前速度上限仍生效。
4. 截止时还没抵达、目标受阻的弹珠同样结束吸引，在当前位置向下释放。满员找不到安全预留位置时不接纳新球。每球对同一实例只参加一次，新弹珠可启动下一批；不同实例的 Lua 状态独立。

配置 `game/device.json`：`Value = 2` 为磁力秒数（Lua 最大取 60），`Range = 1.2` 为世界吸引半径，`Strength = 3` 为世界单位/秒的收拢速度。非正时长/收拢速度不启动。ID 9003，圆柱直径 0.4、高 0.1，使用触发器，购买价 5、出售价格 120，初始库存 0。`Cooldown = 0`、`MaxTriggersPerBall = 1`；首球用 `TryPrepareTrigger` 应用通用修正，其他参与者只登记访问，聚集不扣耐久。到期释放用同一个 `batch:n` 登记一次；最后一点耐久释放整批，并申请一个固定步的完成窗口，让尚未抵达的成员也能完成。窗口期间不接纳新的批次。

Lua 只跨回调保存 ID、数字和世界位置副本。聚集位置在接纳时固定；滞留阶段不跟随移动装置，释放方向读取当前装置姿态。移除装置会取消滞留并恢复滞留前运动；不会补发 Lua 的向下释放。弹珠消失时引擎回收物理/计时状态，其少量 Lua 普通数据最迟在下一回合首次更新时清空；正常释放立即删除对应记录。

<a id="accelerationfield"></a>

## 加速力场

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

<a id="blackhole"></a>

## 黑洞：吞噬充能，点击释放全局衰减引力

制作源 `MResPacker/Authoring/Lua/BlackHole.lua`，游戏使用 `Resources/Devices/9005_BlackHole.mres`。所有状态均为各实例私有 Lua 普通变量；引擎不包含黑洞名称、ID、充能颗数或持续时间分支。

1. 未充满时碰到圆柱触发器就 `TryConsume(false)`，不计分、不返还、不扣耐久，成功吞噬累计 10 颗后显示“黑洞已充能 · 点击释放”。同一步多球最多吞噬缺少的颗数，不调用有公共冷却的 `Trigger`。
2. 满充后等待左键点击，期间新进入的弹珠穿过，不额外吞噬/计分；未充满的点击无效果。
3. `OnActivate` 用 `TryUse()` 扣 1 并设置截止时间，`TryKeepActive(Parameter)` 保证最后一点耐久也持续完整 10 个模拟秒；每个 `OnField` 对当前场景内所有普通运动弹珠施加向内径向力，无磁性要求、无最远距离截断。
4. 生效期间进入黑洞的弹珠调用 `api:TrySettleAmount(api.EffectiveValue * api.ScoreMultiplier)`，按**当前有效价值（含 RUSH 临时加值）×10**直接结算，不再扣耐久或叠加其他结算倍率。已结算球不会被其他装置或计分区重复结算，正常发出得分事件和回收刚体。
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
