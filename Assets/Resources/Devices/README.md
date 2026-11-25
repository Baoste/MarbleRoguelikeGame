# 装置资源

本目录包含 35 种装置。游戏读取 [GameContent.json](../GameContent.json) 的 `Devices` 数字 ID 列表，再从对应的子目录加载资源。

| ID | 装置 | 目录 |
| --- | --- | --- |
| 103 | 传送门 | `103/` |
| 113 | Multi Ding | `113/` |
| 9000 | 示例弹射台 | `9000/` |
| 9001 | 黏黏 | `9001/` |
| 9002 | 弹弹 | `9002/` |
| 9003 | 吸吸 | `9003/` |
| 9004 | 加速力场 | `9004/` |
| 9005 | 黑洞 | `9005/` |
| 9006 | 反重力 | `9006/` |
| 9007 | 大炮 | `9007/` |
| 9008 | 沙包 | `9008/` |
| 9009 | 大石碎胸口 | `9009/` |
| 9010 | 矿老板 | `9010/` |
| 9011 | 淘金热 | `9011/` |
| 9012 | 猪猪存钱罐 | `9012/` |
| 9013 | 赚赚 | `9013/` |
| 9014 | 摇钱树 | `9014/` |
| 9015 | 分流器 | `9015/` |
| 9016 | 大喷菇 | `9016/` |
| 9017 | 炸弹 | `9017/` |
| 9018 | 复制体 | `9018/` |
| 9019 | 造血工厂 | `9019/` |
| 9020 | 弹珠机关枪 | `9020/` |
| 9021 | 草皮 | `9021/` |
| 9022 | 故障装置 | `9022/` |
| 9023 | more is more | `9023/` |
| 9024 | less is more | `9024/` |
| 9025 | 雪糕机 | `9025/` |
| 9026 | 大胃袋 | `9026/` |
| 9027 | 星星灯 | `9027/` |
| 9028 | 种植牙 | `9028/` |
| 9029 | 有锅气 | `9029/` |
| 9030 | 降降火 | `9030/` |
| 9031 | 台球 | `9031/` |
| 9032 | 超大杯全冰美式 | `9032/` |

## 添加或更新装置

在 `Resources/Devices/<id>/` 创建以下资源。以 [示例弹射台](9000/manifest.json) 的目录为参考。

| 文件 | 用途 |
| --- | --- |
| `manifest.json` | 版本 `1`、装置资源名称，以及同目录的元数据、玩法配置、Prefab 和 Lua 文件名 |
| `metadata.json` | 名称、描述、购买价、售价、稀有度、耐久和 `type: "device"` |
| `device.json` | `DeviceDefinition` 的玩法字段，`Id` 与目录名一致 |
| `prefab.prefab` | Unity 物体层级、网格和材质引用、碰撞体与刚体 |
| `main.lua` | 装置行为的 UTF-8 源文件，由 Unity 导入为 TextAsset |

在 `GameContent.json.Devices` 加入该装置的唯一正整数 ID，即可让它进入游戏目录。移除 ID 会从下一次进入 Play 或启动游戏时停用该装置。完整字段及回调约定见 [装置 Lua API](../../Docs/Device_Lua_API.md)。

模型、材质、贴图和装置预览场景位于 `Assets/Art/Devices`。在 Prefab 的 `PlacementVolume` 子节点用禁用的 SphereCollider 设置摆放占地。场景使用原生碰撞体进行物理模拟。

## 通用参数

- `StartingCount` 为初始赠送份数，当前全部为 `0`。开启场景的「开局获得全部装置」后，背包会收到 ID 列表中每种装置一套。传送门的 `PurchaseQuantity` 为 2，系统自动分配配对 ID。
- `Cooldown` 和 `MaxTriggersPerBall` 控制触发冷却和每球次数上限。
- `Value`、`Strength`、`Range` 为脚本参数。买卖参数取 `metadata.json`。
- `LinkGroup` 指定可连接的装置组，`GroupClearance` 指定同组放置间距。
- `ScoreMultiplier`、`RushChanceAdd` 为兼容倍率和概率参数。
- `Kind`、`SourceId` 是历史分类和来源标签。玩法回调由 Lua 提供。

## Lua 事件与游戏接口

完整接口、传送门及其他运动效果示例见 [装置 Lua API](../../Docs/Device_Lua_API.md)。新脚本通过 `a.Motion` 读取速度、获取出口并调用传送/出射/变速接口；当前传送门保留完整世界速度方向和大小，区域力在出口重新计算。

储存类耐久在释放效果时扣：黑洞启动一次引力、吸吸释放一批、大炮成功发射一批、黏黏释放一颗，分别扣 1；充能/储存/取消/空点击不扣。最后一次仍完成整批或完整持续时间，受阻重试不重复扣。其他即时装置仍按有效触发计数，持续力场每球每次进入扣 1；每回合恢复，`durability = -1` 无限。

淘金热一次只运输一颗球，隐藏并等待 10 秒后，等概率返还原球并加 2 金币、消耗球加 5 金币、或消耗球无金币；三种结果均在完成时扣 1 耐久，运输及取消不扣。等待中拒绝其他球，结果后恢复接收；返还沿棋盘向下且保留进入速度大小。隐藏使用通用 `Motion.TrySetHeldVisible`，释放、取消与回收都会恢复显示。

每件库存装置拥有独立 Lua 环境，共享一个 xLua VM。`OnContact(a)` 在确定性接触处理阶段执行，`OnField(a)` 在手动物理子步前执行。脚本顶层只做声明和初始化；此时可能尚未生成视觉根节点。`self` 在实际视图绑定后可用，常规 Start/Update/物理事件由装置视图转发。

接触示例（增值透镜）：

```lua
function OnContact(a)
    if not a:Trigger(0) then return end
    a:AddScoreMultiplier(a.Parameter)
end
```

`DeviceContactContext` 提供通用操作：

- `Trigger(key)`：检查触发率、冷却和每球次数，应用通用机器倍率/复制修饰。`0` 使用当前装置 ID；连接线使用 `LinkKey`，配对装置可用 `-PairId`。
- `TryPrepareTrigger(key)`：同样的触发检查与修正，不扣耐久；储存效果在释放时用 `CanUseOnce(key)` / `TryUseOnce(key)` 按实例、回合、批次去重。`TryKeepActive(seconds)` 让最后一点耐久对应的持续效果完成。
- `Visit(key, maximum)`：只记录每球访问次数，不施加装置公共冷却；倍率钉和 Rush 钉用它保证同时接触的各颗球都能各触发一次。
- `Get/Set/Global(attribute)`：读取、修改属性；`Value/Parameter/Strength/Speed` 等属性提供当前值和配置。
- `AddBase(attribute, amount)`：增减弹珠未修正的基础属性，再计算现有修正，避免重复乘倍率。沙包用它叠加连击奖励；矿老板用它每次减 2（最低 0），再用 `AddCoins(4)` 产出金币。价值不足也触发，每座矿每回合最多 20 金币，每次实际产出扣 1 耐久；采完后不再扣值或耐久。
- `CreateSpawnBatch()`：Lua 逐颗指定位置、速度和价值，整批提交生成；容量不足或物理受阻时整批撤销，不扣血。`SpawnSourceId` 提供来源信息。大石碎胸口在第 8 次撞击后均匀发射 16 颗，成功扣 1 耐久并冷却 5 秒。
- `CreateSplitBatch(count)`：Lua 指定等分子球的出口，全部生成成功才消耗原球；继承倍率，价值、质量和体积按数量均分。分流器向下生成 3 球，大喷菇向棋盘上方 120° 扇形均匀生成 8 球，成功各扣 1 耐久；`HasSplitFrom(instanceId)` 排除来源祖先，同实例再次经过时直接穿过。遵守通用分裂代数及最小质量/半径限制。
- `CreateCopy()` / `TrySetValue` / `TrySpawn`：复制并单独指定价值和出射位置/速度，保留原球。复制体生成零价值副本，沿镜面反射方向等速出射；其他装置可对其增值。每颗原球一次，自己的复制后代不再复制但仍正常碰撞，成功扣 1 耐久。
- `SetCurrent(attribute, value)` / `RestoreBlood(amount)`：分别替换弹珠指定属性的当前值、按上限回复玩家血量。造血工厂将价值减半向下取整，按减半后价值 ×5 回复血量；正价值处理扣 1，零价值不触发。回血足够后可从耗尽血量状态继续发射，主动提现不会被取消。
- `CreatePortion(value)`：成功生成一颗子球才从原球转移指定价值，失败保留剩余值。机关枪组合滞留回调逐颗射出价值 1 的小球，0.1 秒一颗，在下方 45° 扇形往返扫射；不足 1 的余数舍弃，完成一批扣 1 耐久，忙碌时其他球及自身后代直接穿过。
- `TrySpawnVisual` / `ForEachBallInRadius`：装置事件组合计时移动视觉、完成回调及范围逐球处理。炸弹每 5 秒投出一次，1 秒后爆炸，把范围内符合条件的每颗球八等分；爆炸一次扣 1 耐久，空爆也扣。投掷物不算弹珠，不参与物理碰撞和计分。
- `TryConsume(refund)`、`TrySettleAmount(score)`、`AddCoins(amount)`、`Children(count, replace)`：只负责吞噬、精确结算、加金币、产生子球；耐久由 Lua 组合登记。旧 `Consume` / `TrySettle` 保留隐式计数兼容。
- `ScaleVelocity(scale)`、`Exit(direction, speed, angle)`、`ExitPartner(direction, speed)`：运动操作；方向为 `down` 或 `forward`，出口仍执行障碍检查。
- `StoredValue`、`RushOnSettlement`、`RefundOnDespawn`、`ExitDown`、`TransportLockedUntil`：装置或弹珠状态。
- `Coins`：只读当前钱包；猪猪存钱罐每满 10 金币提供 `1 + 0.1 × 档数` 的倍率，不花金币。
- `AddScoreMultiplier(amount)`：传入完整倍率，累加 `amount - 1`，到得分区以 `1 + 累计增量` 统一计算。两次 1.5 得到 2，两次 1.2 得到 1.4；基础 1 只算一次。`MultiplyScore` 是相同语义的旧接口的兼容名。Multi Ding、猪猪存钱罐、装置通用倍率修正与旧倍率钉统一使用此规则。
- `SetDeviceScoreMultiplier(amount)`：替换当前累计装置倍率；故障装置抽到 0 时设为 0，随后仍可通过 `AddScoreMultiplier` 增加，进入得分区按当前累计值结算。
- `CountPlacedDevices(excludedInstanceId = 0)`：实时统计已放置实例，可排除指定实例。more is more 排除自身，每个其他装置提供 +2，单次最多 +20；less is more 同样排除自身，从 +20 起每个其他装置减 2，最低为 0。耗尽但仍在场的装置计入，库存不计；公式由各自 Lua 决定。
- `RoundId` 配合 Lua 实例状态：雪糕机每回合依次提供 ×10、×9 至 ×2，之后 ×1 无效果；每次只累加“倍率 − 1”，有效作用扣耐久，下一回合重置次数。
- `OnOffer` / `Accept` 与 `GetRoundValue` / `SetRoundValue` 用于接受提交的装置并准备下一回合。大胃袋可接收盘面和背包中的装置，回调成功后消耗该装置并按稀有度增加 2/4/6/8。增量在碰撞时施加，下一轮布置清空。
- `AddRushChance`、`Roll`、`BreakPinsOnCross`、`RemoveDevice`：概率、随机与通用场景操作。

磁力脚本通过 `DeviceFieldContext` 的距离、方向、磁性参数计算作用力，再调用 `AddRadialForce`。C# 只负责通用物理执行及总力/速度上限，磁极方向与距离衰减公式在装置 Lua。

预览不创建 Lua 实例，并关闭所有碰撞体。正式视图先进入棋盘所在的物理场景、设置位置，再启用。视图重建保留该装置的 Lua 状态；出售、消耗或结束会话会释放脚本。每回合的冷却/蓄积数值由通用 ECS 状态复位。调用上下文只在当前回调期间有效，不应保存后继续操作。

## 检查

- `Marbles ECS/Validate Attribute and Content Runtime` 检查 ID 选择、Prefab、Lua、预览、场景以及玩法、库存和商店。
- `Marbles ECS/实体UI/验证跨回合购买与鼠标点击`：真实 TestScene 的购买、预览、摆放和跨回合交互。

原 `Prefabs/DeviceItem.prefab` 是库存 UI 卡片，继续使用；普通固定钉和弹珠也保留其通用组件。

RUSH 使用累积条。`RushChanceAdd`、`RushOnSettlement`、`AddRushChance` 只保留兼容，不参与新触发。球贡献、固定得分区入账、临时价值和编号显示 API 统一见 [装置 Lua API](../../Docs/Device_Lua_API.md)。当前台球为 `9031/`，有锅气的额外贡献为 +10，装置增益不翻倍。
