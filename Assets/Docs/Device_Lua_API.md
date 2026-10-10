# 装置 Lua API

装置效果写在 `Resources/Devices/*.mres` 包内的 Lua 中。引擎向 `OnContact(a)` 传入 `DeviceContactContext`：C# 提供弹珠运动、属性、触发和计分等通用能力；Lua 选择调用哪些能力、调用顺序及数值。新增效果不需要在 C# 按装置 ID 或 `Kind` 增加分支。

## 传送门：保持速度，按出口朝向出射

核心操作是 `a.Motion:TryTeleport(exit)`。引擎读取弹珠当前线速度大小，把弹珠移到出口外侧，并设置 `velocity = exit.Forward * speed`。零速仍为零，低于 `0.1` 的速度也不被提高；角速度和弹珠自身旋转保持不变。正常情况下速度大小完全保留，仍受通用 `BALL_MAX_SPEED` 上限约束。

以下是已写入 `103_Portal.mres` 的逻辑（保留已有的“下坡出射”药物效果）：

```lua
function OnContact(a)
    if a.PairId == 0 or a.TransportLockedUntil > a.Time then return end
    local motion = a.Motion
    local exit = motion:FindPartner()
    if exit == nil or not a:Trigger(-a.PairId) then return end

    local moved
    if a.ExitDown then
        moved = motion:TryLaunch(exit, exit.Down, motion.Speed)
    else
        moved = motion:TryTeleport(exit)
    end
    if moved then
        a.ExitDown = false
        a.TransportLockedUntil = a.Time + 0.5
    end
end
```

配对关系来自通用库存的 `PurchaseQuantity` / `PairId`。未放置或没有物理视图的配对装置不会成为出口。出口被实体挡住时返回 `false`，弹珠位置、速度保持原状，Lua 不设置传送锁，也不消耗下坡药物标记。`Trigger` 已成功登记的访问次数、公共冷却和通用修正不会回滚；需要其他失败规则时，在 Lua 中明确安排触发与操作的顺序。

## 运动接口 `a.Motion`

所有位置和向量均为 Unity **世界坐标**，速度单位为世界单位/秒，角度单位为度。通过实例调用方法使用冒号 `:`；读写属性使用点 `.`。

| API | 含义 |
| --- | --- |
| `Position` / `Velocity` / `Speed` | 当前弹珠的位置、线速度向量、线速度大小。成功操作后立刻能读到新值。 |
| `Source` | 当前触发装置的只读出口对象 `DeviceEndpoint`。 |
| `FindDevice(instanceId)` | 在当前游戏会话查找已放置且有物理视图的装置；不存在返回 `nil`。ID 是库存实例 ID。 |
| `FindPartner()` | 查找当前装置同 `PairId` 的另一个已放置出口；无配对返回 `nil`。超过两件时按实例 ID 取最小的有效对象。 |
| `TryTeleport(exit)` | 保持当前速度大小，沿 `exit.Forward` 从出口外侧出射。 |
| `TryLaunch(exit, direction, speed)` | 指定世界方向和非负速度，从出口外侧出射。方向由引擎归一化。 |
| `TryMove(position, velocity)` | 指定世界位置和完整速度向量，执行带障碍检查的位移。 |
| `TrySetVelocity(velocity)` | 原地设置速度，不执行传送障碍检查，也不改变位置。 |
| `TryScaleSpeed(scale)` | 保持方向，将当前速度乘以非负倍率；`0` 使弹珠停止。 |

所有 `Try*` 返回布尔值。非法数值（NaN/无穷）、负速率、零出射方向、无效出口、找不到刚体或位移终点被阻挡，都会返回 `false`。位移只检查终点，不要求沿途无障碍。速度操作统一执行全局限速，成功后同步 PhysX 和 ECS，保留角速度；成功操作后同一逻辑帧的后续弹珠交互会跳过，避免使用旧接触位置重复触发。

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

`a`、`a.Motion` 和出口对象仅在本次 `OnContact` 回调内有效。不要保存后在 `Update`、下一次碰撞或其他装置回调中使用；跨回调访问会报错，向另一个回调传入出口会被拒绝。可以保存普通数字作为 Lua 状态。运动接口面向当前弹珠，不暴露 ECS 实体或 Rigidbody；不要通过 `self` 绕过运动接口修改弹珠刚体。

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

## 其他接触 API

| API | 含义 |
| --- | --- |
| `Trigger(key)` | 检查触发率、装置冷却和每球次数，成功后应用通用机器倍率/复制修正。`0` 使用当前装置 ID，连线用 `LinkKey`，配对装置可用 `-PairId`。 |
| `Visit(key, maximum)` | 只记录每球访问次数，无装置公共冷却；`maximum = 0` 表示不限制总次数，同一帧不重复。 |
| `Get(attribute)` / `Set(attribute, value)` / `Global(attribute)` | 读取弹珠属性、设置基础属性、读取全局属性。名称使用 `GameAttribute` 中的大写字符串。 |
| `Value` / `Parameter` / `Strength` | 弹珠基础价值、装置 `Value` 参数、装置力度参数。 |
| `ScoreMultiplier` / `RushChanceAdd` | 装置的计分、Rush 配置。 |
| `Time` / `PairId` / `LinkKey` | 当前回合秒数、库存配对 ID、当前连线标识。 |
| `StoredValue` | 可读写的装置储存数值。 |
| `TransportLockedUntil` / `ExitDown` | 可读写的弹珠传送锁截止时间、一次下坡出射标志。 |
| `RushOnSettlement` / `RefundOnDespawn` | 可读写的弹珠结算 Rush 概率、消失返还标志。 |
| `Consume(refund)` / `AddCoins(value)` | 消耗当前弹珠（参数控制是否保留返还资格）、增加金币。消耗后应结束本次回调。 |
| `Children(count, replace)` | 通过通用生成系统产生子球，可替换父球；继承、代数与数量上限由引擎处理。 |
| `MultiplyScore(factor)` / `AddRushChance(amount)` | 修改弹珠计分倍率、Rush 概率增量。 |
| `Roll(probability)` | 使用本局统一随机状态进行概率判定。 |
| `BreakPinsOnCross()` / `RemoveDevice()` | 清除当前装置十字范围内的普通钉子、消耗当前装置。 |

旧 `a.Speed`、`ScaleVelocity`、`Exit`、`ExitPartner` 作为兼容别名保留，内部也走新的通用运动 API。新脚本优先使用 `a.Motion`。

## 持续力事件

磁铁等效果使用 `OnField(a)`，在物理子步之前调用。此时 `a` 是 `DeviceFieldContext`，并非接触 API；提供 `Magnetic`、`Response`、`MagneticRadius`、`Range`、`Strength`、`Distance`、`ForwardOffset`，以及 `AddRadialForce(force)`。Lua 计算磁性条件、方向与衰减，C# 累加径向力并执行物理积分。具体示例见 `112_Magnet.mres`。

## 代码与验证

- 引擎接口：`Scripts/System/DeviceMotionApi.cs`、`DeviceContactContext.cs`。
- ECS/物理同步：`BallInteractionSystem.cs` 和 `Scripts/Physics/PhysX/MarblePhysicsBridge.Content.cs`。
- `Scripts/link.xml` 保留 Lua 反射访问的类型，供 Player 裁剪使用。
- Unity 菜单 `Marbles ECS/Validate Attribute and Content Runtime` 执行包内 Lua、旋转出口、零速/低速、角速度保留、失败状态、限速、上下文生命周期以及真实 PhysX 阻挡测试，并回归现有商店/库存/装置功能。
