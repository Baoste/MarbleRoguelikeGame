# MRes 装置目录

本目录的原始 `.mres` 是装置外观、碰撞体、参数和行为的权威来源。当前包含以下 15 种装置，商店与背包使用这个目录；`Prefabs/Devices`、手工 prefab 绑定和 C# 装置行为分支已删除。

| ID | 装置 | 包文件 |
| --- | --- | --- |
| 103 | 传送门 | `103_Portal.mres` |
| 113 | Multi Ding | `113_MultiDing.mres` |
| 9000 | 示例弹射台 | `9000_BouncePad.mres` |
| 9001 | 黏黏 | `9001_Sticky.mres` |
| 9002 | 弹弹 | `9002_Reflector.mres` |
| 9003 | 吸吸 | `9003_Collector.mres` |
| 9004 | 加速力场 | `9004_AccelerationField.mres` |
| 9005 | 黑洞 | `9005_BlackHole.mres` |
| 9006 | 反重力 | `9006_AntiGravity.mres` |
| 9007 | 大炮 | `9007_Cannon.mres` |
| 9008 | 沙包 | `9008_Sandbag.mres` |
| 9009 | 大石碎胸口 | `9009_StoneBurst.mres` |
| 9010 | 矿老板 | `9010_GoldMine.mres` |
| 9011 | 淘金热 | `9011_GoldRush.mres` |
| 9012 | 猪猪存钱罐 | `9012_PiggyBank.mres` |

## 添加或更新装置

1. 将符合 `MRes/README.md` 格式的 `.mres` 放在本目录或子目录中。文件名可自定，ID 必须唯一。
2. 包内包含 `metadata.json`、节点/网格/材质/碰撞体数据、Lua 脚本，以及类型为 `Other (255)` 的 `game/device.json`。
3. `game/device.json` 使用 `DeviceDefinition` 的字段名，至少提供唯一 `Id` 和所需玩法参数。名称、描述、购买价、出售价格、稀有度和耐久以 Metadata 的 `name`、`description`、`purchasePrice`、`value`、`rarity`、`durability` 为准；`type` 必须为 `device`。购买价为正整数，出售价格可为 0，耐久 `-1` 表示无限。
4. 包引用的 shader ID 必须在 `ShaderRegistry.asset` 显式注册。装置使用 `device-lit`；BouncePad 使用已注册的 `MRes/ExampleSurface`。
5. 等待 Unity 导入，或执行 `Tools/MRes/Rebuild Device Catalog`。进入 Play 和构建 Player 前也会校验并刷新。

`Resources/GameContent.json` 的 `Devices` 同步为本目录当前全部 MRes 的完整列表，包含子目录；包被删除后对应 JSON 条目也移除。打开项目、脚本重载和 JSON 导入同样会检查同步，不保留旧 JSON/Excel 的装置数据。其他属性、药物与内容规则继续使用原配置。离线可运行 `python Assets/Editor/Balance/sync_mres_devices.py`，`--check` 检查快照是否最新。

Unity 不会将未知扩展名直接作为 `TextAsset` 收录，因此 `DeviceCatalog.asset` 保存原包的完整字节副本，供编辑器和 Player 统一读取。它由编辑器生成，不需要手工编辑，也不使用 ScriptedImporter，不会解包文件到磁盘。发布时需包含 Resources 中的目录资产和 shader 注册表。损坏包、重复 ID、缺失 shader 会阻止刷新、进入 Play 或构建，并报告源文件。

`GameContent.json` 中的 `Devices` 由目录刷新生成，作为现有离线平衡/工作簿工具的兼容快照；运行时会用 `.mres` 定义覆盖它。请修改包内参数，不要修改该快照。属性和药物仍在 `GameContent.json` 编辑。

## 包内通用参数

- `StartingCount`：开局赠送份数；`PurchaseQuantity`：每份产生的装置数量（传送门为 2），自动分配配对 ID。
- `Radius`：放置半径；`Cooldown`、`MaxTriggersPerBall`：默认触发冷却和每球次数上限。
- `Value`、`Strength`、`Range`：供脚本读取的数值。
- `RushCategory`：商店 Rush 分类；`LinkGroup`：相同非空组名的装置可连接；`GroupClearance`：同组放置间距。
- `ScoreMultiplier`、`RushChanceAdd`：供脚本读取的倍率和概率参数。
- `Kind`、`SourceId`：历史分类/来源标签，供平衡工具识别，不用于运行时装置行为分派。

## Lua 事件与游戏接口

完整接口、传送门及其他运动效果示例见 [装置 Lua API](../../Docs/Device_Lua_API.md)。新脚本通过 `a.Motion` 读取速度、获取出口并调用传送/出射/变速接口；当前传送门保留完整世界速度方向和大小，区域力在出口重新计算。

储存类耐久在释放效果时扣：黑洞启动一次引力、吸吸释放一批、大炮成功发射一批、黏黏释放一颗，分别扣 1；充能/储存/取消/空点击不扣。最后一次仍完成整批或完整持续时间，受阻重试不重复扣。其他即时装置仍按有效触发计数，持续力场每球每次进入扣 1；每回合恢复，`durability = -1` 无限。

淘金热一次只运输一颗球，隐藏并等待 10 秒后，等概率返还原球并加 2 金币、消耗球加 5 金币、或消耗球无金币；三种结果均在完成时扣 1 耐久，运输及取消不扣。等待中拒绝其他球，结果后恢复接收；返还沿棋盘向下且保留进入速度大小。隐藏使用通用 `Motion.TrySetHeldVisible`，释放、取消与回收都会恢复显示。

每件库存装置拥有独立 Lua 环境，共享一个 xLua VM。`OnContact(a)` 在确定性接触处理阶段执行，`OnField(a)` 在手动物理子步前执行。脚本顶层只做声明和初始化；此时可能尚未生成视觉根节点。`self` 在实际视图绑定后可用，常规 Start/Update/物理事件仍由 MRes Loader 转发。

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
- `TryConsume(refund)`、`TrySettleAmount(score)`、`AddCoins(amount)`、`Children(count, replace)`：只负责吞噬、精确结算、加金币、产生子球；耐久由 Lua 组合登记。旧 `Consume` / `TrySettle` 保留隐式计数兼容。
- `ScaleVelocity(scale)`、`Exit(direction, speed, angle)`、`ExitPartner(direction, speed)`：运动操作；方向为 `down` 或 `forward`，出口仍执行障碍检查。
- `StoredValue`、`RushOnSettlement`、`RefundOnDespawn`、`ExitDown`、`TransportLockedUntil`：装置或弹珠状态。
- `Coins`：只读当前钱包；猪猪存钱罐每满 10 金币提供 `1 + 0.1 × 档数` 的倍率，不花金币。
- `AddScoreMultiplier(amount)`：传入完整倍率，累加 `amount - 1`，到得分区以 `1 + 累计增量` 统一计算。两次 1.5 得到 2，两次 1.2 得到 1.4；基础 1 只算一次。`MultiplyScore` 是相同语义的旧包兼容名。Multi Ding、猪猪存钱罐、装置通用倍率修正与旧倍率钉统一使用此规则。
- `AddRushChance`、`Roll`、`BreakPinsOnCross`、`RemoveDevice`：概率、随机与通用场景操作。

磁力脚本通过 `DeviceFieldContext` 的距离、方向、磁性参数计算作用力，再调用 `AddRadialForce`。C# 只负责通用物理执行及总力/速度上限，磁极方向与距离衰减公式在包内 Lua。

预览不创建 Lua 实例，并关闭所有碰撞体。正式视图先进入棋盘所在的物理场景、设置位置，再启用。视图重建保留该装置的 Lua 状态；出售、消耗或结束会话会释放脚本。每回合的冷却/蓄积数值由通用 ECS 状态复位。调用上下文只在当前回调期间有效，不应保存后继续操作。

## 检查

- `Marbles ECS/Validate Attribute and Content Runtime`：当前全部资源的原包字节一致性、加载、预览隔离、场景和生命周期，以及玩法/库存/商店回归。
- Unity Test Runner 的 `MRes.Tests`：格式校验、资源释放、共享脚本、预览和 Lua/物理事件。
- `Marbles ECS/实体UI/验证跨回合购买与鼠标点击`：真实 TestScene 的购买、预览、摆放和跨回合交互。

原 `Prefabs/DeviceItem.prefab` 是库存 UI 卡片，继续使用；普通固定钉和弹珠也保留其通用组件。
