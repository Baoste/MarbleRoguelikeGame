# 装置 Lua API 参考

本文定义游戏装置脚本可使用的事件、上下文、属性和方法。接口以当前 C# 实现为准；制作脚本位于 `MResPacker/Authoring/Lua`，发布脚本包含在 `Resources/Devices/*.mres` 中。

文档采用《异星工厂》官方文档的“事件、类、成员、类型”组织方式，参见 [类参考](https://lua-api.factorio.com/latest/classes/LuaEntity.html)与[事件参考](https://lua-api.factorio.com/latest/events.html)。本项目的函数名称、调用语法和生命周期以本文为准。

装置配方、制作路径和原有设计记录见 [装置实现参考](Device_Lua_Examples.md)；全局结算和调参规则见 [Balance.md](Balance.md)。

## 索引

| 分类 | 内容 |
| --- | --- |
| 基础 | [调用约定](#conventions)、[事件](#events)、[共享成员](#shared) |
| 上下文 | [DeviceContactContext](#devicecontactcontext)、[DeviceEventContext](#deviceeventcontext)、[DeviceFieldContext](#devicefieldcontext)、[DeviceOfferContext](#deviceoffercontext) |
| 运动与快照 | [DeviceMotionApi](#devicemotionapi)、[DeviceEndpoint](#deviceendpoint)、[DeviceHoldInfo](#deviceholdinfo)、[DeviceImpactInfo](#deviceimpactinfo) |
| 计算与全盘效果 | [DeviceVectorApi](#devicevectorapi)、[DeviceBoardApi](#deviceboardapi) |
| 生成请求 | [DeviceCopy](#devicecopy)、[DevicePortion](#deviceportion)、[DeviceSplitBatch](#devicesplitbatch)、[DeviceSpawnBatch](#devicespawnbatch) |
| 表现与配置 | [DeviceTransientInfo](#devicetransientinfo)、[MResLuaObject](#mresluaobject)、[Metadata](#metadata) |
| 附录 | [兼容接口](#compatibility)、[规则与生命周期](#lifecycle)、[示例](#examples)、[维护](#maintenance) |

<a id="conventions"></a>

## 调用约定

方法使用冒号，属性使用点。事件是在脚本环境中定义的全局函数，参数由引擎传入。

```lua
function OnContact(api)
    if not api:Trigger() then return end
    api:AddScoreMultiplier(api.ScoreMultiplier)
end
```

| 标记 / 类型 | 含义 |
| --- | --- |
| `number` | Lua 数值。除特别说明外，要求有限数；具体边界见成员说明。 |
| `integer` | 整数参数或返回值。定义 ID 对应 C# `uint`；实例 ID 通常对应 `int`。 |
| `boolean` / `string` | 布尔值 / 字符串。 |
| `Vector3` | xLua 提供的 Unity 世界坐标向量，可读 `x/y/z`、`magnitude`、`sqrMagnitude`，并进行向量加减和数乘。不是任意 Lua table。 |
| `T?` | 可为 `nil` 的对象。 |
| `void` | 不返回值。 |
| `参数 = 默认值` | 可以省略该参数；默认值由引擎提供。 |
| 只读 / 读写 | 公开属性的访问方式；配置字段按只读数据使用。 |

位置、距离和半径使用世界单位，线速度使用世界单位/秒，角度使用度。力使用牛顿；`Hold.EntryAngularVelocity` 使用 Unity 刚体角速度单位（弧度/秒）。`api.Radius` 已换算为世界半径，`Get("BALL_RADIUS")` 仍使用属性配置单位。

回调上下文及其派生对象仅在本次回调内有效。跨回调保存 ID、字符串、数值和向量值；下一次回调重新取得 API 对象。失效上下文会引发异常，不能把 `Try*` 的失败返回值视为对失效上下文的保护。

方法的失败方式以各条目为准：有些返回 `false`、`0` 或 `nil`，有些抛出参数异常。普通回调不会回滚此前已成功的操作；[库存提交](#deviceoffercontext)和[生成请求](#generation-rules)有各自的提交规则。

<a id="events"></a>

## 事件

引擎按函数名调用。脚本只需声明自己使用的函数，不需要注册事件 ID。事件的 Lua 返回值不参与玩法判定。

### 游戏事件

| 签名 | 参数类型 | 调用时机 |
| --- | --- | --- |
| `OnContact(api)` | [DeviceContactContext](#devicecontactcontext) | 弹珠与装置实体、触发区域或连接线产生有效接触报告时。只有真实碰撞可能提供 `Impact`。 |
| `OnHoldEnd(api)` | [DeviceContactContext](#devicecontactcontext) | 定时滞留到期或 `ReleaseHeld()` 释放后。运动和显示先恢复，再调用；`Hold` 非空。 |
| `OnBallUpdate(api)` | [DeviceContactContext](#devicecontactcontext) | 每个固定模拟步，在物理读回、滞留到期、接触和计时视觉处理之后逐球调用。 |
| `OnTick(api)` | [DeviceEventContext](#deviceeventcontext) | 每个固定模拟步开始时调用，无弹珠时也调用。 |
| `OnActivate(api)` | [DeviceEventContext](#deviceeventcontext) | 玩家在 Playing / Draining 阶段激活已放置装置时。分发成功不代表 Lua 接受了激活。 |
| `OnField(api)` | [DeviceFieldContext](#devicefieldcontext) | 每个 PhysX 子步前，为未滞留弹珠收集连续力。 |
| `OnOffer(api)` | [DeviceOfferContext](#deviceoffercontext) | Build 阶段向已放置装置提交一件未放置库存物品时。 |
| `OnCreated(api)` | [DeviceEventContext](#deviceeventcontext) | 库存实例创建后一次；配对购买中的每件实例分别调用。 |
| `OnOfferAccepted(api)` | [DeviceEventContext](#deviceeventcontext) | 库存提交事务成功后一次，读取已提交状态。 |
| `OnRoundStart(api)` | [DeviceEventContext](#deviceeventcontext) | 回合重置和耐久恢复后，按实例 ID 通知全部已拥有装置。 |

`OnCreated`、`OnOfferAccepted`、`OnRoundStart` 不要求实例已放置、有物理视图或有剩余耐久。没有物理视图时，读取装置姿态属性会失败。

战斗事件要求来源装置仍有效、已放置并有物理视图。耐久耗尽后的分发取决于待完成滞留和完成窗口，见[耐久规则](#durability-rules)。暂停时不推进模拟事件及模拟时间，Controller 拒绝暂停时的点击激活。

### OnBallUpdate 的筛选与顺序

按装置实例 ID、弹珠出生序号排序。仅声明该函数的装置接收事件。

- `Hold = nil`、`Impact = nil`、`LinkKey = 0`。
- 跳过滞留、已结算、待回收、寿命到期和本固定步已成功执行运动命令的弹珠；排空超时后不更新。
- 不按 `Range` 或磁性自动筛选，Lua 自行判断区域。
- 多个装置先后操作同一球时，较早成功的运动命令优先。
- 不要仅因事件被调用就扣耐久。持续观察与一次有效作用应分别处理。

### 自定义回调

| 来源 | 被调用函数签名 | 参数类型 |
| --- | --- | --- |
| [ForEachPlacedDevice(definitionId, callback, value)](#foreachplaceddevice) | `callback(api, value)` | 新的 `DeviceEventContext`、`number` |
| `ForEachBallInRadius(center, radius, callback)` | `callback(api)` | 新的 `DeviceContactContext`，无碰撞快照 |
| `TrySpawnVisual(..., callback)` | `callback(api)` | 新的 `DeviceEventContext`，`Transient` 非空 |

回调名是当前接收实例脚本中的全局函数名。一次回调异常不会阻止列表中的其他接收者；异常前已完成的修改保持有效。

### MRes 表现事件

这组事件来自 MRes 展示对象，不接收游戏上下文，不应代替固定模拟事件实现游戏计时。

| 签名 | 参数 | 说明 |
| --- | --- | --- |
| `Start()` | 无 | 展示对象启动时，可根据 Lua 普通缓存恢复外观。重建展示对象可能再次调用。 |
| `Update(deltaTime)` | `number` | Unity 渲染更新秒数。 |
| `FixedUpdate(deltaTime)` | `number` | Unity FixedUpdate 秒数，不等同于本游戏手动模拟的 `OnTick`。 |
| `OnCollisionEnter(other)` / `OnCollisionStay(other)` / `OnCollisionExit(other)` | `MResLuaObject` | MRes 原始碰撞通知。 |
| `OnTriggerEnter(other)` / `OnTriggerStay(other)` / `OnTriggerExit(other)` | `MResLuaObject` | MRes 原始触发器通知。 |
| `OnDestroy()` | 无 | Lua 脚本实例被释放时调用；共享脚本的展示对象解绑不等于释放脚本。 |

原始碰撞通知不提供计分、耐久或 ECS 弹珠上下文。装置玩法使用游戏事件；表现事件使用[表现对象](#mresluaobject)。MRes 表现回调抛错时，该展示行为组件会停止后续分发。

<a id="shared"></a>

## 共享成员

以下是文档分组，不是基类或额外的 Lua 对象。C、E、O 分别表示 `DeviceContactContext`、`DeviceEventContext`、`DeviceOfferContext`；仅表中列出的上下文提供该成员。

### 属性

| 属性 | 类型 | 访问 | 上下文 | 说明 |
| --- | --- | --- | --- | --- |
| `InstanceId` | `integer` | 只读 | C / E / O | 当前库存实例 ID。 |
| `DefinitionId` | `integer` | 只读 | C / E / O | 装置定义 ID，同类实例相同。 |
| `RoundId` | `integer` | 只读 | C / E / O | C / E 为模拟当前回合；O 为 Build 阶段即将开始的回合。 |
| `Parameter` | `number` | 只读 | C / E / O | `game/device.json.Value`。 |
| `Strength` | `number` | 只读 | C / E / O | `game/device.json.Strength`，具体用途由脚本决定。 |
| `DurabilityLimit` | `integer` | 只读 | C / E / O | 当前实例有效耐久上限；`-1` 无限，`0` 休眠。 |
| `RemainingDurability` | `integer` | 只读 | C / E / O | 本回合剩余次数；`-1` 无限。O 可读到本次暂存修改。 |
| `Time` | `number` | 只读 | C / E | 当前回合模拟秒数。 |
| `DeltaTime` | `number` | 只读 | C / E | 固定模拟步秒数；E 在尚未运行固定步时可能为 0。 |
| `Range` | `number` | 只读 | C / E | `game/device.json.Range`，不会自动筛选范围。 |
| `ScoreMultiplier` | `number` | 只读 | C / E | 当前装置配置倍率，不是球的累计倍率。 |
| `Coins` | `number` | 只读 | C / E | 实时钱包余额，上限 9,000,000,000,000,000。 |
| `Blood` / `BloodCapacity` | `number` | 只读 | C / E | 玩家当前血量 / 容量。 |
| `HeldCount` | `integer` | 只读 | C / E | 当前装置拥有的存活滞留球数量。 |
| `Vectors` | [DeviceVectorApi](#devicevectorapi) | 只读 | C / E | 纯向量计算。 |
| `Board` | [DeviceBoardApi](#deviceboardapi) | 只读 | C / E | 全盘状态与临时效果。 |

### 方法索引

| 方法 | 返回值 | 上下文 |
| --- | --- | --- |
| [GetRoundValue(key)](#getroundvalue) | `number` | C / E / O |
| [SetRoundValue(key, value)](#setroundvalue) | `void` | C / E / O |
| [GetInstanceValue(key)](#getinstancevalue) | `number` | C / E / O |
| [SetInstanceValue(key, value)](#setinstancevalue) | `void` | C / E / O |
| [SetDurabilityLimit(maximum)](#setdurabilitylimit) | `void` | C / E / O |
| [RestoreDurability()](#restoredurability) | `void` | C / E / O |
| [SetStatus(text)](#setstatus) | `void` | C / E / O |
| [TryUse()](#tryuse) | `boolean` | C / E |
| [CanUseOnce(key)](#canuseonce) / [TryUseOnce(key)](#tryuseonce) | `boolean` | C / E |
| [TryKeepActive(seconds)](#trykeepactive) | `boolean` | C / E |
| [RestoreBlood(amount)](#restoreblood) | `number` | C / E |
| [CountPlacedDevices(excludedInstanceId = 0)](#countplaceddevices) | `integer` | C / E |
| [CountPlacedDevicesOfDefinition(definitionId)](#countplaceddevicesofdefinition) | `integer` | C / E |
| [CountPlacedDevicesWithRoundValue(definitionId, key, value)](#countplaceddeviceswithroundvalue) | `integer` | C / E |
| [ForEachPlacedDevice(definitionId, callback, value)](#foreachplaceddevice) | `integer` | C / E |
| [RandomRange(minimum, maximum)](#randomrange) | `number` | C / E |
| [CreateSpawnBatch()](#createspawnbatch) | `DeviceSpawnBatch` | C / E |

<a id="getroundvalue"></a>

### GetRoundValue(key) → number

参数：`key: string`，非空，UTF-8 最多 120 字节。

读取当前实例的回合数值。不存在或属于其他回合时返回 0。Build 阶段访问即将开始的回合；其余阶段访问当前回合。O 可读取本次暂存值。非法 key 抛出异常。

<a id="setroundvalue"></a>

### SetRoundValue(key, value) → void

参数：`key: string`，非空且 UTF-8 最多 120 字节；`value: number`，有限数。

每实例每回合最多 32 个不同 key。C / E 写入立即生效，O 暂存至接受提交。进入目标回合时保留，后续回合读为 0；同一回合收回再放置保留。非法参数或超过 key 数量上限抛出异常。

<a id="getinstancevalue"></a>

### GetInstanceValue(key) → number

参数：`key: string`，非空且 UTF-8 最多 120 字节。

读取跨回合的实例数值，缺失返回 0。O 可读取本次暂存值。非法 key 抛出异常。

<a id="setinstancevalue"></a>

### SetInstanceValue(key, value) → void

参数：`key: string`，非空且 UTF-8 最多 120 字节；`value: number`，有限数。

每实例最多 32 个不同 key。跨回合、收回再放置及模型重建保留；实例出售、删除或会话重置后清理。C / E 立即写入，O 暂存。不是磁盘存档接口。非法参数或超过 key 数量上限抛出异常。

<a id="setdurabilitylimit"></a>

### SetDurabilityLimit(maximum) → void

参数：`maximum: integer`，范围 `[-1, 10000]`。`-1` 无限，`0` 休眠。

修改当前实例上限，保留本回合已消耗次数。覆盖值跨回合保留，不修改包内 Metadata 或其他实例。C / E 立即刷新可用性，O 暂存。越界抛出异常。

<a id="restoredurability"></a>

### RestoreDurability() → void

无参数。清除已消耗次数，恢复至当前有效上限；不修改上限、冷却、访问次数或批次去重记录。O 暂存该修改。

<a id="setstatus"></a>

### SetStatus(text) → void

参数：`text: string?`，`nil` 清空。

发布当前装置状态文字，显示最多 256 个 C# 字符。C 使用当前回合；E / O 在 Build 阶段使用下一回合。O 接受成功后才提交。只影响显示，不激活效果、不扣耐久。

<a id="tryuse"></a>

### TryUse() → boolean

无参数。为当前回调登记一次耐久作用；成功返回 `true`，不可用返回 `false`。同一回调重复调用共用一次登记。

C 与 `Trigger`、旧 `Consume`、旧 `TrySettle` 共用登记；在 `OnHoldEnd` 内直接返回 `true`，不再扣除。延后扣耐久的完成事件使用 `TryUseOnce`。

不记录访问、设置冷却或施加倍率。C 的成功登记还遵循本回调的 RUSH 贡献规则。连续力的同名方法另见 [DeviceFieldContext](#field-tryuse)。

<a id="canuseonce"></a>

### CanUseOnce(key) → boolean

参数：`key: string`，非空且 UTF-8 最多 120 字节。

同实例、同回合的 key 已登记，或仍有可登记耐久时返回 `true`。只查询；非法 key、实例失效或无可用次数返回 `false`。

<a id="tryuseonce"></a>

### TryUseOnce(key) → boolean

参数：`key: string`，限制同 `CanUseOnce`。

第一次成功登记扣一次耐久；同实例、同回合、同 key 再次调用返回 `true`，即使剩余耐久已经为 0。非法 key 或无法接受新作用返回 `false`。

每个新批次使用不同 key；同一批的各成员及重试沿用同一 key。不要再用 `Trigger` / `TryUse` 对该次效果重复计数。恢复耐久不会清除已登记 key。

<a id="trykeepactive"></a>

### TryKeepActive(seconds) → boolean

参数：`seconds: number`，有限数，范围 `(0, 60]`。

把来源装置的完成窗口设为 `Time + seconds`；再次调用替换截止时间。成功返回 `true`；非法时长、实例不存在或未放置返回 `false`。

允许最后一点耐久已耗尽的持续效果完成接触、施力和更新，不增加耐久。来源移除、收回或回合结束时清理。

<a id="restoreblood"></a>

### RestoreBlood(amount) → number

参数：`amount: number`，有限且非负，可带小数。

返回实际回复量，超过血量容量的部分舍弃；满血、0 回复或已结束的回合返回 0。负数或非有限数抛出异常。不扣耐久、不修改球价值或家族返还记录。C 实际回复大于 0 时记录有效作用。

因缺血进入排空的回合，在血量足够支付最低档发射时恢复可发射状态；不自动发射。主动提现后不恢复发射。

<a id="countplaceddevices"></a>

### CountPlacedDevices(excludedInstanceId = 0) → integer

参数：`excludedInstanceId: integer`，默认 0，非负。

返回全部已放置库存实例数；0 不排除任何实例，其他值仅排除对应实例。未知 ID 不影响结果。耗尽耐久的已放置装置照常计数，配对装置逐件计数；背包、商品、固定钉和预览不计。负数抛出异常。

<a id="countplaceddevicesofdefinition"></a>

### CountPlacedDevicesOfDefinition(definitionId) → integer

参数：`definitionId: integer`，有效非零 `uint`。

统计指定定义的已放置实例，包括自己与耗尽耐久的实例。未知定义返回 0；0 抛出异常。

<a id="countplaceddeviceswithroundvalue"></a>

### CountPlacedDevicesWithRoundValue(definitionId, key, value) → integer

| 参数 | 类型 | 限制 |
| --- | --- | --- |
| `definitionId` | `integer` | 有效非零 `uint`。 |
| `key` | `string` | 非空，UTF-8 最多 120 字节。 |
| `value` | `number` | 有限数，使用精确相等比较。 |

返回同定义、已放置且回合数值等于 `value` 的实例数，包括自己和耗尽实例。缺失 key 视为 0；Build 查询下一回合。未知定义返回 0，非法参数抛出异常。

<a id="foreachplaceddevice"></a>

### ForEachPlacedDevice(definitionId, callback, value) → integer

参数：`definitionId: integer`，非零；`callback: string`，非空且最多 64 字符；`value: number`，有限数。

取得已放置实例快照，按实例 ID 升序调用各实例的 `callback(api, value)`，使用新的 `DeviceEventContext`，包括调用者。返回正常完成的回调数量。

仅运行回合且来源可执行时发送。接收者需仍有物理视图并可执行或完成既有效果；无函数或不可用者跳过。非法参数或事件嵌套超过 8 层抛出异常；单个接收者异常只记录并继续。发送不扣耐久、不共享上下文。

<a id="randomrange"></a>

### RandomRange(minimum, maximum) → number

参数：`minimum: number`、`maximum: number`，两者及差值有限，且 `minimum <= maximum`。

从整局随机序列均匀取 `[minimum, maximum)`。边界相等时直接返回该值，不推进序列。非法边界抛出异常且不推进序列。与商店、初始装置、开奖及其他装置共用随机状态，跨回合延续。

<a id="createspawnbatch"></a>

### CreateSpawnBatch() → DeviceSpawnBatch

无参数。创建独立弹珠生成请求；本次回调内有效，创建时不生成弹珠、不扣耐久。成员见 [DeviceSpawnBatch](#devicespawnbatch)。

<a id="devicecontactcontext"></a>

## DeviceContactContext

当前弹珠的游戏上下文。由 `OnContact`、`OnHoldEnd`、`OnBallUpdate` 及范围逐球回调提供。

源码：[DeviceContactContext.cs](../Scripts/System/DeviceContactContext.cs)。本类型还提供[共享成员表](#shared)中标注 C 的全部成员。

### 属性

| 属性 | 类型 | 访问 | 说明 |
| --- | --- | --- | --- |
| `Motion` | [DeviceMotionApi](#devicemotionapi) | 只读 | 当前弹珠运动接口。 |
| `Hold` | [DeviceHoldInfo?](#deviceholdinfo) | 只读 | 仅滞留完成回调非空。 |
| `Impact` | [DeviceImpactInfo?](#deviceimpactinfo) | 只读 | 仅带真实碰撞几何的接触非空；逐球更新、范围查询、Trigger 和滞留完成为 nil。 |
| `BallId` | `string` | 只读 | 稳定 ID，格式为“回合:出生序号”；避免 64 位序号转换为 Lua 浮点数。 |
| `Value` | `number` | 只读 | 当前 `BALL_BASE_VALUE`，含属性修正，不含 RUSH 临时加值。 |
| `EffectiveValue` | `number` | 只读 | 当前基础价值加 RUSH 临时加值。 |
| `DeviceScoreMultiplier` | `number` | 只读 | 球持有的累计装置倍率，范围 0–1000；不含临时全盘增量。 |
| `EffectiveDeviceScoreMultiplier` | `number` | 只读 | 球持有增量与当前全盘增量合成后的装置倍率，范围 0–1000。 |
| `HasTriggeredOtherDevice` | `boolean` | 只读 | 是否已有其他装置实例的有效作用历史；相同定义的另一实例也算其他装置。 |
| `SpawnSourceId` | `integer` | 只读 | 最近生成来源的装置实例 ID；玩家发射为 0。 |
| `PairId` | `integer` | 只读 | 当前装置配对 ID，0 表示未配对。 |
| `LinkKey` | `integer`（C# long） | 只读 | 当前接触的连线键。逐球更新和范围查询为 0，完成事件沿用原接触键。 |
| `Radius` | `number` | 只读 | 当前弹珠世界半径，不是装置占位半径。 |
| `RushActive` | `boolean` | 只读 | 当前全盘是否处于 RUSH。 |
| `RushContribution` | `number` | 只读 | 当前球普通 RUSH 贡献。 |
| `RushDurationBonus` | `number` | 读写 | 该球使条值累满时增加的秒数；有限 `[0, 3600]`，非法写入抛错，不刷新正在运行的 RUSH。 |
| `TransportLockedUntil` | `number` | 读写 | 球的传送锁截止回合时间；由 Lua 自行检查和设置。 |
| `RefundOnDespawn` | `boolean` | 读写 | 球的消失返还标志，仍受家族返还规则约束。 |
| `StoredValue` | `number` | 读写 | 实例旧储存字段，每回合重置；命名状态优先使用回合/实例数值接口。 |

旧属性 `Speed`、`ExitDown`、`RushChanceAdd`、`RushOnSettlement` 见[兼容接口](#compatibility)。

### 方法索引

| 方法 | 返回值 | 用途 |
| --- | --- | --- |
| [Trigger(visitKey = 0)](#trigger) | `boolean` | 检查访问和冷却，登记耐久及通用修正。 |
| [TryPrepareTrigger(visitKey = 0)](#trypreparetrigger) | `boolean` | 同类触发准备，不扣耐久。 |
| [Visit(visitKey, maximum, inheritOnCopy = false)](#visit) | `boolean` | 访问去重，不使用公共冷却。 |
| [MarkTriggered()](#marktriggered) | `void` | 登记纯 Lua 效果的成功作用。 |
| [Get(attribute)](#get) / [Global(attribute)](#global) | `number` | 读取球属性 / 全局属性。 |
| [Set(attribute, value)](#set) / [AddBase(attribute, amount)](#addbase) / [SetCurrent(attribute, value)](#setcurrent) | `void` | 修改基础属性或替换当前属性。 |
| [AddScoreMultiplier(amount)](#addscoremultiplier) / [SetDeviceScoreMultiplier(amount)](#setdevicescoremultiplier) | `void` | 累加 / 替换球持有的装置倍率。 |
| [AddRushContribution(amount)](#addrushcontribution) | `void` | 增加球的普通 RUSH 贡献。 |
| [SetScoreRushContribution(amount)](#setscorerushcontribution) / [ClearScoreRushContribution()](#clearscorerushcontribution) | `void` | 设置 / 清除得分区固定贡献。 |
| [SetAppearance(label, red, green, blue)](#setappearance) | `void` | 修改球文字和颜色。 |
| [TryConsume(refund)](#tryconsume) / [TrySettleAmount(score)](#trysettleamount) | `boolean` | 消耗球 / 按最终金额结算球。 |
| [AddCoins(value)](#addcoins) | `void` | 向钱包增加整数金币。 |
| [Roll(probability)](#roll) | `boolean` | 概率抽取。 |
| [CreateCopy()](#createcopy) | `DeviceCopy` | 复制请求。 |
| [CreatePortion(value)](#createportion) | `DevicePortion` | 按价值分出请求。 |
| [CreateSplitBatch(count)](#createsplitbatch) | `DeviceSplitBatch` | 等分请求。 |
| [HasCopiedFrom(instanceId)](#hascopiedfrom) / [HasSplitFrom(instanceId)](#hassplitfrom) | `boolean` | 查询生成祖先。 |
| [BreakPinsOnCross()](#breakpinsoncross) / [RemoveDevice()](#removedevice) | `void` | 移除普通钉 / 当前装置。 |

<a id="trigger"></a>

### Trigger(visitKey = 0) → boolean

参数：`visitKey: integer`，对应 C# long。0 使用当前装置的接触目标键；连接线可传 `LinkKey`，配对装置可传 `-PairId`。

成功返回 `true`，并执行以下操作：

1. 检查剩余耐久、装置公共冷却和 `MaxTriggersPerBall`。
2. 登记访问和本次耐久。
3. 设置下次可触发时间。
4. 将非中性的装置 `MACHINE_MULT` 修正交给 `AddScoreMultiplier`。

当前实现的“触发率”用于冷却缩放，不进行随机抽签。冷却间隔至少为一个固定模拟步；该回调已准备过触发、处于 `OnHoldEnd` 或检查未通过时返回 `false`。成功的触发不会因后续出射失败而回滚。

<a id="trypreparetrigger"></a>

### TryPrepareTrigger(visitKey = 0) → boolean

参数同 `Trigger`。执行相同的访问、冷却和通用倍率修正，但不扣耐久；成功返回 `true`。

用于多阶段效果的接纳阶段，完成时由 Lua 调用 `TryUseOnce`。耗尽耐久且未获本回调登记、同回调已准备或 `OnHoldEnd` 中返回 `false`。不负责预留待释放数量。

<a id="visit"></a>

### Visit(visitKey, maximum, inheritOnCopy = false) → boolean

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `visitKey` | `integer`（C# long） | 0 使用当前接触目标键，其他值作为共享访问键。 |
| `maximum` | `integer` | 正数限制每球总访问次数；0 表示不限次数。 |
| `inheritOnCopy` | `boolean` | 默认 false；true 允许该条记录随 `CreateCopy` 继承。 |

登记成功返回 `true`；同球同键同一步重复访问或达到次数上限返回 `false`。不扣耐久、不施加倍率、不设置公共冷却。等分和按价值分出继承全部访问记录；普通复制只继承显式标记的记录。

<a id="marktriggered"></a>

### MarkTriggered() → void

无参数。记录当前实例对当前球的成功作用，重复记录幂等。用于 Lua 自己完成的状态效果。

不扣耐久、不增加访问次数或倍率。满足当前回调的 RUSH 登记条件时，同时登记一次通用装置贡献；完成滞留不会重复登记。哪些操作自动记录历史见[作用历史](#effect-history)。

<a id="get"></a>

### Get(attribute) → number

参数：`attribute: string`，使用 [GameAttribute](../Scripts/Marbles/Rules/GameAttributes.cs) 中区分大小写的大写名称。

返回该球包含当前修正的属性值，例如 `Get("BALL_BASE_VALUE")`。无效名称抛出异常；属性单位和上下限来自 `GameContent.json.Attributes`。

<a id="global"></a>

### Global(attribute) → number

参数同 `Get`。读取该属性的全局值，不代表当前球的最终属性。无效名称抛出异常。

<a id="set"></a>

### Set(attribute, value) → void

参数：`attribute: string`，合法属性名；`value: number`，目标基础值。

写入基础值并重新计算当前修正，按属性上下限钳制；NaN 抛出异常。既有该属性修正仍保留。当前结果发生改变时记录有效作用；不扣耐久。

<a id="addbase"></a>

### AddBase(attribute, amount) → void

参数：`attribute: string`；`amount: number`，有限增量，可为负数。

给保存的基础值加上增量，再应用当前修正。非法增量或相加溢出抛出异常且不写入。例如基础值 10、修正 ×2，增加 1 后当前值为 22，撤销修正后为 11。不扣耐久。

<a id="setcurrent"></a>

### SetCurrent(attribute, value) → void

参数：`attribute: string`；`value: number`，有限目标值。

替换该属性的基础值和当前值，并移除这颗球在该属性上的 ADD/MUL/SET 修正；结果遵守属性上下限。其他属性、累计装置倍率和独立固定加分不受影响。非法数值抛错且不修改；不扣耐久。

<a id="addscoremultiplier"></a>

### AddScoreMultiplier(amount) → void

参数：`amount: number`，完整倍率，有限且非负。

累加 `amount - 1`；最终球持有倍率为 `clamp(1 + 累计增量, 0, 1000)`。增量上限 999，负增量保留供后续相加。两次 1.5 得到 2；1 为中性，0 提供 -1 增量。Lua 不要提前减 1。

只记录倍率，到得分区统一计算；不修改基础价值、不立即计分、不扣耐久。非法参数抛出异常且不写入。旧 `MultiplyScore` 使用相同语义。

<a id="setdevicescoremultiplier"></a>

### SetDeviceScoreMultiplier(amount) → void

参数：`amount: number`，有限 `[0, 1000]`。

把持有增量替换为 `amount - 1`，保留独立基础倍率及固定加分。不立即计分、不扣耐久。非法参数抛出异常。

例如先设为 0，再增加完整倍率 3，结果为 2。该方法不清除全盘临时装置效果。

<a id="addrushcontribution"></a>

### AddRushContribution(amount) → void

参数：`amount: number`，有限 `[0, 1000000]`。

增加当前球普通贡献，总值最多 1,000,000。只写球状态，不立即增加全盘条值、不扣耐久。正数修改登记有效作用；非法参数抛错。通用触发贡献和结算规则见 [RUSH](#rush-rules)。

<a id="setscorerushcontribution"></a>

### SetScoreRushContribution(amount) → void

参数：`amount: number`，有限 `[-1000000, 1000000]`。

设置当前球进入得分区时的固定入账值，替代普通“贡献 ×1.5”，不再乘 1.5。允许负数；底部流失仍读取普通贡献。不立即修改条值、不扣耐久；非法参数抛错。

<a id="clearscorerushcontribution"></a>

### ClearScoreRushContribution() → void

无参数。清除得分区固定贡献覆盖，恢复普通入账规则，不改变普通贡献。

<a id="setappearance"></a>

### SetAppearance(label, red, green, blue) → void

参数：`label: string`，UTF-8 最多 60 字节；`red/green/blue: number`，有限 `[0, 1]`。

设置当前球的显示文字和颜色，立即同步表现；不修改价值或贡献、不扣耐久。非法输入抛错且不部分写入。对象池复用时清理该外观。

<a id="tryconsume"></a>

### TryConsume(refund) → boolean

参数：`refund: boolean`，是否保留消失返还资格。

成功消耗当前球返回 `true`；已结算或待消失返回 `false`。不计分、不隐式扣耐久，成功时记录有效作用。返还仍受家族规则限制；成功后结束对当前球的操作。

<a id="trysettleamount"></a>

### TrySettleAmount(score) → boolean

参数：`score: number`，最终分数，有限且非负。

按明确金额结算并消耗当前球，保留小数累计、计分事件和消失返还资格；有限大值受单球/回合上限限制。成功返回 `true`；非法分数、已结算或待回收返回 `false`。

不额外套用机器、全局、区域或 RUSH 倍率，不增加 RUSH 条或刷新得分区限时叠加，不扣耐久。成功后停止操作当前球；仍可更新装置状态文字。

<a id="addcoins"></a>

### AddCoins(value) → void

参数：`value: number`，有限且非负。

向下取整后增加钱包金币，并遵守钱包上限。不消费弹珠、不计回合分数、不扣耐久。负数或非有限数抛出异常；输入至少为 1 时登记有效作用。

<a id="roll"></a>

### Roll(probability) → boolean

参数：`probability: number`，有限 `[0, 1]`。

消耗一次整局随机数并返回判定结果。0 和 1 也推进随机序列；非法概率抛错且不推进。不要用 Lua `math.random` 替代游戏随机序列。

<a id="createcopy"></a>

### CreateCopy() → DeviceCopy

无参数。创建保留原球的[复制请求](#devicecopy)，不生成球、不扣耐久。

<a id="createportion"></a>

### CreatePortion(value) → DevicePortion

参数：`value: number`，有限且大于 0，否则抛出异常。

创建从原球转移指定价值的[分出请求](#deviceportion)。构造不生成、不扣价值或耐久；余额和属性范围在请求检查/提交时验证。

<a id="createsplitbatch"></a>

### CreateSplitBatch(count) → DeviceSplitBatch

参数：`count: integer`，范围 `[2, 256]`，越界抛出异常。

创建[等分批次](#devicesplitbatch)，此时获取当前属性快照，不生成球、不消耗原球或耐久。

<a id="hascopiedfrom"></a>

### HasCopiedFrom(instanceId) → boolean

参数：`instanceId: integer`，要查询的装置实例。

查询当前球的复制祖先记录。复制、等分和旧 `Children` 保留记录；新发射和独立生成从空记录开始。只查询，不自动禁止再次处理。

<a id="hassplitfrom"></a>

### HasSplitFrom(instanceId) → boolean

参数同 `HasCopiedFrom`。查询当前球的等分/价值分出祖先记录，包含此前来源。再次复制或分出保留记录；新发射和独立生成从空记录开始。

<a id="breakpinsoncross"></a>

### BreakPinsOnCross() → void

无参数。要求物理适配器清除当前装置十字范围内的普通钉子。不自动扣耐久；需要 `IMarblePhysicsCommands` 支持。

<a id="removedevice"></a>

### RemoveDevice() → void

无参数。删除当前装置的库存实体并更新布局版本；不是出售操作，不发放金币。之后不要继续访问该实例。其来源效果由生命周期系统清理。

<a id="deviceeventcontext"></a>

## DeviceEventContext

不绑定当前弹珠的装置上下文。用于生命周期、固定步、激活、跨装置通知和视觉完成事件。

源码：[DeviceEventContext.cs](../Scripts/System/DeviceEventContext.cs)。共享属性及方法见[共享成员](#shared)中的 E。此类型没有 `Motion`、`Value`、`AddCoins` 或弹珠消费/结算接口。

### 属性

| 属性 | 类型 | 访问 | 说明 |
| --- | --- | --- | --- |
| `Position` | `Vector3` | 只读 | 当前物理视图的装置中心。 |
| `Radius` | `number` | 只读 | 装置占位半径。 |
| `Forward` | `Vector3` | 只读 | 本地 -Z 对应的世界单位方向。 |
| `Up` | `Vector3` | 只读 | 本地 +Y 对应的棋盘法向。 |
| `Down` | `Vector3` | 只读 | 世界向下方向在棋盘平面上的投影；退化时使用 Forward。 |
| `Transient` | [DeviceTransientInfo?](#devicetransientinfo) | 只读 | 仅计时视觉完成回调非空。 |

没有已放置物理视图时，读取上述姿态属性抛出异常；生命周期回调可能处于该状态。

<a id="deviceeventcontext-releaseheld"></a>

### ReleaseHeld() → integer

无参数。释放调用时属于本装置的存活滞留集合，返回处理数量。按弹珠生成顺序，先恢复每颗球，再同步调用新的 `OnHoldEnd`；同样支持提前结束定时滞留。

回调中重新登记的滞留不会进入本次释放集合。无回调或单球回调异常不阻止其他球完成。不自动选择出射方向、不扣耐久。

<a id="deviceeventcontext-trysetyaw"></a>

### TrySetYaw(degrees) → boolean

参数：`degrees: number`，有限角度。

相对摆放姿态，绕棋盘法向设置绝对旋转偏移；不累积旋转，不修改背包布局。成功返回 true；非法数值或无支持的物理视图返回 false。

<a id="deviceeventcontext-tryspawnvisual"></a>

### TrySpawnVisual(nodePath, position, velocity, seconds, callback) → integer

| 参数 | 类型 | 限制 |
| --- | --- | --- |
| `nodePath` | `string` | 当前 MRes 根节点下非空模板路径，须存在并包含网格。 |
| `position` | `Vector3` | 有限世界起点。 |
| `velocity` | `Vector3` | 有限世界速度，可为零；终点也必须有限。 |
| `seconds` | `number` | 有限 `(0, 60]`。 |
| `callback` | `string?` | 最多 64 字符；nil / 空字符串表示不执行完成函数。 |

成功返回正整数视觉 ID，条件不满足返回 0。每个模拟最多 256 个活动视觉对象，需物理适配器提供 `IDeviceTransientVisuals`。

只复制 Mesh、Material 和 Transform 层级；模板可以隐藏，不复制碰撞体、刚体或脚本。视觉按 `position + velocity × elapsed` 匀速运动，不受重力/障碍影响，不占弹珠数量、不计分、不扣耐久。

到期先移除视觉对象，再调用新上下文；无函数或函数异常也会清理。来源不可执行、视图丢失/重建、回合结束、排空超时或会话释放时取消，不补发完成效果。耗尽耐久后需要继续的效果应申请完成窗口。

<a id="deviceeventcontext-foreachballinradius"></a>

### ForEachBallInRadius(center, radius, callback) → integer

参数：`center: Vector3`，有限世界坐标；`radius: number`，有限 `[0, 100]`；`callback: string`，非空且最多 64 字符。

冻结与球形区域相交的弹珠列表，包含球自身半径，按出生顺序调用指定函数并传入新的 `DeviceContactContext`。返回正常完成的回调数；非法参数抛出异常。

跳过滞留、已结算、待回收、寿命到期的球；新生成球不进入当前快照。来源需可执行，缺失函数返回 0；单球异常不阻止其他球。查询不扣耐久、不登记效果、不提供 `Impact`。若外层先消耗最后一点耐久，需完成窗口让查询继续。

<a id="devicefieldcontext"></a>

## DeviceFieldContext

仅由 `OnField(api)` 提供。源码：[DeviceFieldContext.cs](../Scripts/Physics/PhysX/DeviceFieldContext.cs)。本类型没有 `Motion`、`Trigger`、`Vectors`、`Time` 或共享状态方法。

### 属性

全部只读，表示当前物理子步开始时的状态。

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `Forward` / `Up` | `Vector3` | 装置本地 -Z 的世界方向 / 棋盘法向。 |
| `DeltaTime` | `number` | 当前物理子步秒数，可能小于逻辑固定步。 |
| `Strength` / `Range` | `number` | 装置配置值，含义和筛选由 Lua 决定。 |
| `Magnetic` | `boolean` | 当前球的磁性标记。 |
| `Response` / `MagneticRadius` | `number` | 当前球的磁力响应 / 磁性范围参数。 |
| `Distance` | `number` | 球到装置中心的棋盘平面距离。 |
| `ForwardOffset` | `number` | 球相对装置中心沿 Forward 的有符号偏移。 |

<a id="devicefieldcontext-overlapsbox"></a>

### OverlapsBox(nodePath) → boolean

参数：`nodePath: string`，相对于 MRes 根节点的 BoxCollider 路径；空字符串查根节点。

检测当前球体是否与真实盒体相交或接触，包含中心、旋转、缩放及球半径；不是世界轴对齐包围盒检测。缺失、禁用或未激活的 Collider 返回 false。只查询，不施力、不扣耐久。

<a id="field-tryuse"></a>

### TryUse() → boolean

无参数。为当前球的一次连续区域作用登记耐久；同一回调及连续停留中重复调用不重复扣，离开后重入可扣一次。无法登记时返回 false，脚本应结束本次施力。

Lua 必须先判断区域，再在区域内调用该方法。引擎依据持续调用记录进入状态；它不会自动根据 Range 判断离开。已按整体激活登记的持续效果无需每球再扣一次。

<a id="devicefieldcontext-tryaddforce"></a>

### TryAddForce(worldForce) → boolean

参数：`worldForce: Vector3`，世界坐标力，单位 N，零向量合法。

为本子步累计力，成功返回 true。非有限分量、长度平方溢出或累加溢出返回 false，原累计力不变。可多次调用，不立即写速度、不自动检查区域、不扣耐久。

各力场向量相加后，统一限制总场力为 100 N，再由物理系统按 `F × dt / mass` 积分。不要在 Lua 重复乘时间或除质量。正常重力、碰撞及最大速度限制继续生效；不主动施加扭矩。异常前已接受的力不会回滚。

<a id="devicefieldcontext-addradialforce"></a>

### AddRadialForce(magnitude) → void

参数：`magnitude: number`，径向力大小，正数向中心、负数向外。

沿棋盘平面方向调用通用力累加；中心处方向为零。非有限大小忽略；不隐含磁性检查、距离衰减、范围筛选或耐久登记。

<a id="deviceoffercontext"></a>

## DeviceOfferContext

Build 阶段的一次库存提交上下文。源码：[DeviceOfferContext.cs](../Scripts/System/DeviceOfferContext.cs)。提供[共享成员](#shared)中标注 O 的成员；没有弹珠运动、计分或力场能力。

### 属性

以下均只读；接收者的身份、配置、耐久见共享属性表。

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `OfferedInstanceId` | `integer` | 本次提交的单件库存实例。 |
| `OfferedDefinitionId` | `integer` | 来源装置定义 ID。 |
| `OfferedName` | `string` | 来源装置名称。 |
| `OfferedRarity` | `string` | `common` / `rare` / `epic` / `legendary`。 |

<a id="deviceoffercontext-accept"></a>

### Accept() → void

无参数。标记接受本次提交，重复调用仍只处理这一件物品。

回调正常结束且已接受时，提交暂存的回合/实例数值、耐久和状态文字，删除来源库存实例，更新布局与可用性，再分发新的 `OnOfferAccepted`。接受不发出售金币、不继承来源成长、不扣接收方耐久，也不自动消费配对的另一件物品。

未调用 Accept 或回调异常时，上述暂存数据不提交，物品保留。Lua 局部变量不属于事务；需要与物品消耗一起提交的成长使用 `SetRoundValue` / `SetInstanceValue`。提交后的 `OnOfferAccepted` 异常不会撤销已完成事务。

### 提交条件

来源须为本会话未放置库存，目标已放置，不能提交自身。暂停、战斗阶段或无 OnOffer 的目标不接受。Build 阶段可以给上回合已耗尽的实例准备下一回合。

控制器的 CanOfferDevice 只检查资格和函数存在，不执行 Lua，也不保证脚本最终 Accept。

<a id="devicemotionapi"></a>

## DeviceMotionApi

访问方式：`api.Motion`，仅 `DeviceContactContext` 提供。源码：[DeviceMotionApi.cs](../Scripts/System/DeviceMotionApi.cs)。

### 属性

| 属性 | 类型 | 访问 | 说明 |
| --- | --- | --- | --- |
| `Position` | `Vector3` | 只读 | 当前球世界中心。 |
| `Velocity` | `Vector3` | 只读 | 当前世界线速度，可能已包含碰撞反弹。 |
| `Speed` | `number` | 只读 | Velocity 的长度。 |
| `Source` | [DeviceEndpoint](#deviceendpoint) | 只读 | 当前来源装置的出口快照。 |

### 方法索引

| 签名 | 返回值 | 说明 |
| --- | --- | --- |
| [FindDevice(instanceId)](#devicemotionapi-finddevice) | `DeviceEndpoint?` | 查找可用的已放置装置出口。 |
| [FindPartner()](#devicemotionapi-findpartner) | `DeviceEndpoint?` | 查找同 PairId 的另一出口。 |
| [TryTeleport(destination)](#devicemotionapi-tryteleport) | `boolean` | 保持速度大小，沿出口朝向发射。 |
| [TryLaunch(destination, direction, speed)](#devicemotionapi-trylaunch) | `boolean` | 指定出口、方向和速度发射。 |
| [TryMove(position, velocity)](#devicemotionapi-trymove) | `boolean` | 同时修改位置与速度。 |
| [TrySetPosition(position)](#devicemotionapi-trysetposition) | `boolean` | 只修改位置。 |
| [TrySetVelocity(velocity)](#devicemotionapi-trysetvelocity) | `boolean` | 原地修改速度。 |
| [TryScaleSpeed(scale)](#devicemotionapi-tryscalespeed) | `boolean` | 缩放当前速度。 |
| [TryHold(seconds)](#devicemotionapi-tryhold) | `boolean` | 定时滞留。 |
| [TryHoldUntilReleased()](#devicemotionapi-tryholduntilreleased) | `boolean` | 手动释放的滞留。 |
| [TrySetHeldVisible(visible)](#devicemotionapi-trysetheldvisible) | `boolean` | 设置本装置滞留球的显示。 |
| [TrySetGravityScale(scale, seconds, resetOnOtherDevice)](#devicemotionapi-trysetgravityscale) | `boolean` | 设置临时重力倍率。 |
| [TryClearGravityScale()](#devicemotionapi-trycleargravityscale) | `boolean` | 移除临时重力倍率。 |

<a id="devicemotionapi-finddevice"></a>

### FindDevice(instanceId) → DeviceEndpoint?

参数：`instanceId: integer`，库存实例 ID。

实例不存在、未放置、剩余耐久为 0 或缺少物理视图时返回 nil；否则返回本回调所属的出口对象。完成窗口不使耗尽实例成为新的可用出口。

<a id="devicemotionapi-findpartner"></a>

### FindPartner() → DeviceEndpoint?

无参数。查找与来源相同非零 PairId 的另一可用出口；多个匹配时选择实例 ID 最小者。未配对或没有可用出口返回 nil。

<a id="devicemotionapi-tryteleport"></a>

### TryTeleport(destination) → boolean

参数：`destination: DeviceEndpoint`，必须来自当前 Motion。

以当前 Speed 调用 TryLaunch，方向为 destination.Forward。会改变方向；需要保持完整世界速度时使用 TrySetPosition。无效/其他上下文的出口或发射失败返回 false。

<a id="devicemotionapi-trylaunch"></a>

### TryLaunch(destination, direction, speed) → boolean

参数：`destination: DeviceEndpoint`，当前 Motion 所属出口；`direction: Vector3`，有限且非零；`speed: number`，有限且非负。

归一化方向，按以下位置和 `direction × speed` 进行带终点检查的位移：

```text
destination.Position
  + destination.Up * (ballRadius + 0.005)
  + normalizedDirection * (destination.Radius + ballRadius + 0.02)
```

零速合法。出口受阻、参数非法、球滞留或没有可用刚体时返回 false。只负责出射，不设置传送锁、冷却、滞留或下一阶段效果。

<a id="devicemotionapi-trymove"></a>

### TryMove(position, velocity) → boolean

参数：`position: Vector3`、`velocity: Vector3`，有限世界坐标和速度。

成功后立即同步 PhysX / ECS，保留旋转和角速度，速度受当前 BALL_MAX_SPEED 限制。只检查终点，不检查沿途路径。非法输入、终点受阻、滞留或物理体不可用返回 false，位置与速度保持原状。

<a id="devicemotionapi-trysetposition"></a>

### TrySetPosition(position) → boolean

参数：`position: Vector3`。

等同于 TryMove(position, Velocity)：保持当前完整世界速度、旋转和角速度，仍受当前限速。失败不改变位置或速度；不自动查配对或设置锁。出口区域的外力在下一物理子步重新计算，不携带入口区域力。

<a id="devicemotionapi-trysetvelocity"></a>

### TrySetVelocity(velocity) → boolean

参数：`velocity: Vector3`，有限世界速度，零向量合法。

原地设速，保留角速度，执行当前限速，不检查终点障碍。非法速度、滞留或物理体不可用返回 false。一次置零不等于持续滞留。

<a id="devicemotionapi-tryscalespeed"></a>

### TryScaleSpeed(scale) → boolean

参数：`scale: number`，有限且非负。

调用 TrySetVelocity(Velocity × scale)，保持方向；0 置零。非法倍率或设速失败返回 false。

<a id="devicemotionapi-tryhold"></a>

### TryHold(seconds) → boolean

参数：`seconds: number`，有限 `(0, 60]`。

在原位置固定球并计时，暂停线运动、旋转、碰撞和外力。成功返回 true；非法时长、已滞留或无法开始返回 false，不覆盖已有滞留。

到期先恢复进入时运动，再以新上下文调用 OnHoldEnd；不自动出射、不扣耐久。等待、取消、异常和寿命优先级见[滞留生命周期](#hold-lifecycle)。

<a id="devicemotionapi-tryholduntilreleased"></a>

### TryHoldUntilReleased() → boolean

无参数。与定时滞留相同，但无自动截止时间，等待来源装置 ReleaseHeld。成功返回 true；已滞留或无法开始返回 false。

不负责容量、移动到中心或出射；弹珠寿命及回合排空计时继续推进。

<a id="devicemotionapi-trysetheldvisible"></a>

### TrySetHeldVisible(visible) → boolean

参数：`visible: boolean`。

只操作当前装置已经滞留的球。false 隐藏，true 恢复本次隐藏前的显示状态，球继续滞留。自由运动球、其他装置滞留球或不支持的物理适配器返回 false。

不改变位置、时间、运动或耐久。重复隐藏不覆盖原始显示快照；完成、取消、回收和会话重置会恢复，原本隐藏的 Renderer 不会被擅自显示。

<a id="devicemotionapi-trysetgravityscale"></a>

### TrySetGravityScale(scale, seconds, resetOnOtherDevice) → boolean

| 参数 | 类型 | 限制 |
| --- | --- | --- |
| `scale` | `number` | 有限 `[-10, 10]`；-1 反转，0 无重力。 |
| `seconds` | `number` | 有限 `(0, 60]`。 |
| `resetOnOtherDevice` | `boolean` | 接触其他有效装置实例时，是否在执行对方 Lua 前取消。 |

临时重力为“世界重力 × 当前 BALL_GRAVITY_MOD × scale”。重复调用替换倍率、来源与截止时间，不乘叠，不改变基础属性、位置、速度或其他外力。

非法参数或设置失败返回 false，不覆盖原效果。计时使用模拟时间，滞留期间继续计时；到期恢复当前基础重力而非旧快照。来源移除/收回/视图丢失时取消，仅耐久耗尽不取消。其他装置接触不要求对方触发成功；固定钉、墙、来源自身和连线不触发提前取消。

<a id="devicemotionapi-trycleargravityscale"></a>

### TryClearGravityScale() → boolean

无参数。有临时效果时移除并返回 true；没有时返回 false。保留当前基础属性及其他修正。

上述运动接口不隐式扣耐久。成功的运动/滞留/重力操作登记作用历史；成功即时运动还使本逻辑步后续交互跳过该球，避免使用旧接触位置重复处理。

<a id="deviceendpoint"></a>

## DeviceEndpoint

通过 Motion.Source、FindDevice 或 FindPartner 获得。只在创建它的回调中有效，不能传给其他回调的 Motion。

| 属性 | 类型 | 访问 | 说明 |
| --- | --- | --- | --- |
| `InstanceId` | `integer` | 只读 | 装置库存实例。 |
| `Position` | `Vector3` | 只读 | 装置世界中心。 |
| `Radius` | `number` | 只读 | 装置占位半径。 |
| `Forward` | `Vector3` | 只读 | 旋转后的本地 -Z 单位方向。 |
| `Up` | `Vector3` | 只读 | 旋转后的本地 +Y 单位方向。 |
| `Down` | `Vector3` | 只读 | 世界向下方向在 Up 所定义平面的投影；退化时为 Forward。 |

<a id="deviceendpoint-rotate"></a>

### Rotate(direction, degrees) → Vector3

参数：`direction: Vector3`，世界方向；`degrees: number`，角度。

返回绕出口 Up 旋转后的方向，不修改装置或球。脚本应传有限值；需要统一向量参数校验时使用 Vectors.Rotate。

<a id="deviceholdinfo"></a>

## DeviceHoldInfo

通过 OnHoldEnd 的 `api.Hold` 获得。全部只读，只在本次完成回调内有效。

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `EntryVelocity` | `Vector3` | 进入滞留时的世界线速度。 |
| `EntrySpeed` | `number` | EntryVelocity 长度；实际恢复/出射仍执行当前限速。 |
| `EntryAngularVelocity` | `Vector3` | 进入时的世界角速度。 |
| `Duration` | `number` | 请求的滞留秒数；手动滞留为 0。 |
| `IsManual` | `boolean` | 是否由 TryHoldUntilReleased 创建。 |
| `Elapsed` | `number` | 实际经过的模拟秒数，受固定步量化影响。 |

<a id="deviceimpactinfo"></a>

## DeviceImpactInfo

通过带真实碰撞几何的 `api.Impact` 获得。全部只读；修改当前运动不会改变快照。

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `Position` | `Vector3` | 真实接触点，不是球中心。 |
| `Normal` | `Vector3` | 第一个接触点从表面朝球的世界单位法线。 |
| `IncomingVelocity` | `Vector3` | 该物理子步开始时、求解器反弹前的速度快照。 |
| `IncomingSpeed` | `number` | IncomingVelocity 长度。 |

快照不预测本子步内部的重力、外力或其他碰撞。没有几何时 Impact 为 nil，引擎不伪造法线。

<a id="devicevectorapi"></a>

## DeviceVectorApi

访问方式：C / E 的 `api.Vectors`。源码：[DeviceVectorApi.cs](../Scripts/System/DeviceVectorApi.cs)。所有方法只计算，不修改物理、属性、耐久或随机状态。

| 签名 | 参数类型 | 返回值 | 说明 |
| --- | --- | --- | --- |
| `Normalize(vector)` | `Vector3` | `Vector3` | 单位化；长度小于 1e-6 时返回零向量。 |
| `ProjectOnPlane(vector, normal)` | `Vector3, Vector3` | `Vector3` | 投影到法线指定平面，normal 非零，无需预先归一化。 |
| `Reflect(vector, normal)` | `Vector3, Vector3` | `Vector3` | 镜面反射，normal 非零，保留输入长度。 |
| `Dot(a, b)` | `Vector3, Vector3` | `number` | 点积。 |
| `SignedAngle(from, to, axis)` | `Vector3, Vector3, Vector3` | `number` | 返回 [-180, 180] 度，三个向量均要求非零。 |
| `Rotate(vector, axis, degrees)` | `Vector3, Vector3, number` | `Vector3` | 绕非零世界轴旋转，角度有限，保留长度。 |

非有限分量、向量长度平方溢出或要求非零的方向长度小于 1e-6 时抛出参数异常。平面夹角请先将方向投影到同一平面。

<a id="deviceboardapi"></a>

## DeviceBoardApi

访问方式：C / E 的 `api.Board`。源码：[DeviceBoardApi.cs](../Scripts/System/DeviceBoardApi.cs)。仅当前回调有效。

### 属性

| 属性 | 类型 | 访问 | 说明 |
| --- | --- | --- | --- |
| `RushActive` | `boolean` | 只读 | 是否处于 RUSH。 |
| `RushMeter` | `number` | 只读 | 当前累积条值。 |
| `RushMaximum` | `number` | 只读 | 条值上限，当前为 100。 |
| `RushChanceBonus` | `number` | 只读 | 旧概率兼容值，新 RUSH 不读取，见兼容接口。 |

<a id="deviceboardapi-addscoremultiplier"></a>

### AddScoreMultiplier(amount, seconds) → integer

参数：`amount: number`，有限 `[0, 1000]` 的完整倍率；`seconds: number`，有限 `(0, 60]`。

注册独立的全盘临时增量 `amount - 1`，成功返回正整数效果 ID。非运行阶段、来源不可执行或同时已有 256 份效果时返回 0；非法参数抛出异常。

不同效果增量相加、各自计时，不自动合并同一来源。不扣耐久；来源耗尽不会提前撤销已开始效果，移除/收回或回合结束时取消。

该增量在结算和有效倍率查询时实时合成，作用于现有和后来生成的球；不写入球持有倍率，复制/分裂不会永久继承它。TrySettleAmount 不隐式追加它。

<a id="deviceboardapi-getscoremultiplierendtime"></a>

### GetScoreMultiplierEndTime(effectId) → number

参数：`effectId: integer`。

返回仍有效效果的回合截止时间。未知、过期、来源移除/收回或回合结束返回 0。可查询其他实例分发来的效果 ID，不延长效果。

旧概率方法 SetRushChanceBonus、RemoveRushChanceBonus、GetRushChanceBonusEndTime 见[兼容接口](#compatibility)。

<a id="generation-rules"></a>

## 生成请求的共同规则

请求对象只在创建它的回调内有效，不应保存到下次更新。创建和配置请求不扣耐久；Lua 在生成成功后登记本次作用，重试时创建新请求。

`TrySpawn` / `TryCommit` 一旦调用，无论成功失败，该请求都不能再次提交。物理生成失败会清理该请求已创建的候选球；异常也执行清理后传播。已分配的球 ID 不回退。

所有生成受游戏阶段、有效固定步长、在场球数上限、物理阻挡和排空超时约束。复制/分出还要求原球未滞留、未结算、未待回收，并满足相应代数限制。只读可行性属性不检查最终物理空间。

| 类型 | 原球 | 价值来源 | 初速度为零 |
| --- | --- | --- | --- |
| `DeviceCopy` | 保留 | 继承或指定 | 允许 |
| `DevicePortion` | 扣除转移价值；全转出时消耗 | 从原球转移 | 允许 |
| `DeviceSplitBatch` | 全部成功后消耗 | 均分原球 | 允许 |
| `DeviceSpawnBatch` | 不操作当前球 | 独立指定 | 不允许 |

<a id="devicecopy"></a>

## DeviceCopy

由 `api:CreateCopy()` 创建。源码：[DeviceCopy.cs](../Scripts/System/DeviceCopy.cs)。

### 属性

| 属性 | 类型 | 访问 | 说明 |
| --- | --- | --- | --- |
| `Value` | `number` | 只读 | 已覆盖的副本价值，否则读取原球当前基础价值。 |
| `Radius` | `number` | 只读 | 原球当前世界半径。 |
| `CanCopy` | `boolean` | 只读 | 未提交、原球和来源有效、容量/回合/复制代数允许。 |

<a id="devicecopy-trysetvalue"></a>

### TrySetValue(value) → boolean

参数：`value: number`，有限且在 BALL_BASE_VALUE 属性范围内。

仅配置副本价值；生成时清除副本继承的该属性修正、固定 ScoreBonus 和 ScoreAdd，保留倍率。未覆盖时继承原球价值与固定加分。非法值或已提交返回 false，不改变原球。

<a id="devicecopy-trysetdevicescoremultiplier"></a>

### TrySetDeviceScoreMultiplier(amount) → boolean

参数：`amount: number`，有限 `[0, 1000]`。

仅覆盖副本的累计装置倍率，内部保存 amount - 1，保留独立基础倍率和其他属性。未调用时继承原球持有倍率。非法值或已提交返回 false。

<a id="devicecopy-tryspawn"></a>

### TrySpawn(position, velocity) → boolean

参数：`position: Vector3`、`velocity: Vector3`，有限世界值，零速允许。

成功生成一颗副本返回 true，原球保留；失败清理候选球并返回 false。副本沿用原球尺寸、质量、属性修正及到期时间、持有倍率、RUSH、外观、临时重力截止时间、家族及剩余寿命。旋转沿用原球，新球角速度为零。

普通访问记录不继承，只有 Visit(..., true) 标记的记录继承；作用历史和复制/等分祖先继承。复制祖先加入本实例，SpawnSourceId 改为本实例，ReplicationGeneration 加 1，Generation 不增加。复制代数使用 MaxSplitGeneration 上限，不重新抽血或增加家族返还次数。

<a id="deviceportion"></a>

## DevicePortion

由 `api:CreatePortion(value)` 创建。源码：[DevicePortion.cs](../Scripts/System/DevicePortion.cs)。

### 属性

| 属性 | 类型 | 访问 | 说明 |
| --- | --- | --- | --- |
| `Value` | `number` | 只读 | 请求转移的价值。 |
| `Radius` / `Mass` | `number` | 只读 | 新球世界半径 / 质量；默认沿用原球。 |
| `CanSplit` | `boolean` | 只读 | 原球价值足够且前提、属性范围、分裂代数与容量允许。 |

<a id="deviceportion-trysetsize"></a>

### TrySetSize(worldRadius, mass) → boolean

参数：`worldRadius: number`、`mass: number`，有限且满足对应属性上下限；半径在校验时换算为属性单位。

配置新球尺寸和质量，成功返回 true；非法值或已提交返回 false。不缩放原球，也不按价值自动计算体积/质量。

<a id="deviceportion-tryspawn"></a>

### TrySpawn(position, velocity) → boolean

参数同 DeviceCopy.TrySpawn。成功创建新球后，才从原球扣除 Value；失败保留原球全部未分出价值。

新球继承持有倍率、全部访问记录、作用历史、祖先、RUSH、外观、家族、剩余寿命及临时重力截止时间。Generation 加 1，等分祖先加入本实例；原球代数不变。全部价值转出时消耗原球，按替换后的数量检查容量。

固定 ScoreBonus / ScoreAdd 按转移价值占原球当前价值的比例分配，原球同步扣除。新球的价值、尺寸和质量不会重复应用旧修正；原球已扣价值不会在下一次属性计算时恢复。其他属性修正保留。不额外抽血或增加家族返还次数。

<a id="devicesplitbatch"></a>

## DeviceSplitBatch

由 `api:CreateSplitBatch(count)` 创建。源码：[DeviceSplitBatch.cs](../Scripts/System/DeviceSplitBatch.cs)。

### 属性

| 属性 | 类型 | 访问 | 说明 |
| --- | --- | --- | --- |
| `TargetCount` | `integer` | 只读 | 请求的目标数量。 |
| `Count` | `integer` | 只读 | 已添加出口数。 |
| `Value` / `Mass` | `number` | 只读 | 每颗子球价值 / 质量，均为原值除以目标数量。 |
| `Radius` | `number` | 只读 | 原半径乘以数量倒数的立方根，总体积保持。 |
| `CanSplit` | `boolean` | 只读 | 前提、分裂代数、容量、属性范围及原属性快照均有效。 |

<a id="devicesplitbatch-tryadd"></a>

### TryAdd(position, velocity) → boolean

参数：`position: Vector3`、`velocity: Vector3`，有限世界值，零速允许。

保存一个出口，不创建实体。超过目标数量、已提交、非法向量或与已登记子球距离小于“两球半径 +0.001”时返回 false，队列不变。实际出射仍执行子球速度上限。

<a id="devicesplitbatch-trycommit"></a>

### TryCommit() → boolean

无参数。要求恰好添加 TargetCount 个出口，且原球当前属性与创建批次时快照一致。

全部创建成功后消耗原球并返回 true；任一失败清理本批全部子球、保留原球并返回 false。均分后低于属性下限时拒绝，不以钳制放大总量；容量按替换后的净数量检查。

子球当前属性作为新基础，不复制属性修正列表；持有装置倍率、独立倍率、RUSH、外观、访问记录、作用历史、祖先、家族和原寿命继承。ScoreBonus / ScoreAdd 均分，其他独立倍率修正及其截止时间保留；临时重力沿用截止时间。

Generation 加 1，SpawnSourceId 改为本实例，等分祖先加入本实例。新球零角速度，区域力按出生位置重新计算。不重新抽血，家族返还仍最多一次。

<a id="devicespawnbatch"></a>

## DeviceSpawnBatch

由 C / E 的 `api:CreateSpawnBatch()` 创建。源码：[DeviceSpawnBatch.cs](../Scripts/System/DeviceSpawnBatch.cs)。

### 属性

| 属性 | 类型 | 访问 | 说明 |
| --- | --- | --- | --- |
| `Radius` | `number` | 只读 | 内容默认 BALL_RADIUS 换算后的世界半径。 |
| `Count` | `integer` | 只读 | 已接纳请求数，每批最多 256。 |

<a id="devicespawnbatch-tryadd"></a>

### TryAdd(position, velocity, value) → boolean

| 参数 | 类型 | 限制 |
| --- | --- | --- |
| `position` | `Vector3` | 有限世界位置。 |
| `velocity` | `Vector3` | 有限，长度至少 1e-6。 |
| `value` | `number` | 有限且非负，提交时按属性上下限钳制。 |

接纳请求返回 true。批已提交、数量超限、输入非法或批内间距小于“两球半径 +0.001”时返回 false，队列不变。

<a id="devicespawnbatch-trycommit"></a>

### TryCommit() → boolean

无参数。一次性创建全部请求；空批、重复提交、来源未放置/无视图/已删除、回合不可运行、固定步长无效、容量不足或排空超时返回 false。

任一物理生成失败，回滚本批已创建球，不保留半批；成功返回 true。生成本身不检查或扣除剩余耐久，Lua 负责是否接受该次效果。

新球使用默认内容属性和指定基础价值，不复制接触球或玩家当前效果。每球拥有独立身份和寿命、零初始角速度、空祖先与作用历史，SpawnSourceId 为当前实例。投入血量和返还额度为 0。后续复制/等分可能把 SpawnSourceId 更新为新的生成来源。

<a id="devicetransientinfo"></a>

## DeviceTransientInfo

通过计时视觉完成事件的 `api.Transient` 获得。全部只读，仅本次完成回调有效。

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `Id` | `integer` | TrySpawnVisual 返回的视觉 ID。 |
| `Position` | `Vector3` | 精确寿命终点：起点 + 速度 × 请求时长。 |
| `Velocity` | `Vector3` | 创建时的世界速度。 |
| `Duration` | `number` | 创建时的请求秒数。 |

<a id="mresluaobject"></a>

## MResLuaObject

脚本全局 `self` 指向当前绑定的 MRes 展示对象；没有展示对象时为 nil。源码：[MResLuaObject.cs](../MRes/Runtime/Lua/MResLuaObject.cs)。

self 不是游戏上下文，生命周期随展示对象绑定。游戏装置通过 Motion 修改弹珠，避免绕过 ECS / PhysX 同步。

<a id="mresluaobject-trysetrenderersvisible"></a>

### TrySetRenderersVisible(nodePath, visible) → boolean

参数：`nodePath: string`，相对根节点路径，空串表示整个实例；`visible: boolean`。

设置目标节点及后代的 Renderer.enabled。路径最多 256 字符，不接受空路径段、反斜杠、"." 或 ".."。目标不存在、没有 Renderer、路径非法或对象销毁返回 false。

不切换 GameObject 激活、碰撞体或刚体，不修改共享材质；可设置未激活节点的 Renderer，但不会激活它。重建展示对象后，可在 Start 中恢复缓存的外观状态。

### 配置查询

| 签名 | 返回值 | 说明 |
| --- | --- | --- |
| `GetName()` | `string?` | Metadata 名称。 |
| `GetDescription()` | `string?` | Metadata 描述。 |
| `GetValue()` | `number` | 单件出售价格，无 metadata 时为 0。 |
| `GetPurchasePrice()` | `number` | 基础购买价格，无 metadata 时为 5。 |
| `GetRarity()` | `string` | 稀有度，无 metadata 时为 common。 |
| `GetDurability()` | `integer` | 包内默认耐久，无 metadata 时为 20；不是剩余耐久。 |
| `GetItemType()` | `string?` | 枚举名称 "Device" / "Medicine"，不是 JSON 小写类型值。 |

### 原始表现操作

以下是 MRes 对象接口，均不提供游戏运动接口的障碍检查、限速和状态同步。

| 签名 | 参数 / 返回值 | 说明 |
| --- | --- | --- |
| `GetPosition()` | 无 → `Vector3` | 读取 Transform 世界位置。 |
| `SetPosition(x, y, z)` | 三个 `number` → `void` | 设置 Transform 世界位置。 |
| `Translate(x, y, z)` | 三个 `number` → `void` | 按世界坐标平移。 |
| `Rotate(x, y, z)` | 三个角度 `number` → `void` | 按自身坐标旋转。 |
| `AddForce(x, y, z)` | 三个 `number` → `void` | 对父级找到的 Rigidbody 施加 Force；无刚体时无操作。 |
| `SetVelocity(x, y, z)` | 三个 `number` → `void` | 直接设置父级 Rigidbody 线速度；无刚体时无操作。 |

<a id="metadata"></a>

## Metadata 与参数来源

包内 `metadata/metadata.json` 定义内容身份和交易基础值；脚本全局 `metadata` 是加载后的 MResMetadata 对象，按只读配置使用。

| JSON 字段 | JSON 类型 | 默认 / 范围 | 含义 |
| --- | --- | --- | --- |
| `name` | `string` | 非空 | 展示名称。 |
| `description` | `string` | 非空 | 展示描述。 |
| `value` | `number` | 装置为非负整数 | 出售单个库存实例的基础金币。 |
| `purchasePrice` | `number` | 默认 5，装置为正整数 | 一次购买的基础价格，商店修正另行计算。 |
| `rarity` | `string` | 默认 common | common / rare / epic / legendary。 |
| `durability` | `integer` | 默认 20；-1 或 1–10000 | 每回合默认次数，-1 无限。包内不接受 0。 |
| `type` | `string` | device / medicine | 包类型；当前装置目录只接受 device。 |

旧包缺少 purchasePrice、rarity、durability 时加载器补默认值。运行时 `metadata.type` 是 C# MResItemType 枚举；需要名称可用 self:GetItemType()，返回首字母大写枚举名。当前游戏尚未提供 Lua 药物玩法接口。

附加业务文件 `game/device.json` 的参数映射：

| 配置 | Lua 入口 | 说明 |
| --- | --- | --- |
| `Id` | `api.DefinitionId` | 装置定义 ID。 |
| `Value` | `api.Parameter` | 脚本自定义数值参数。 |
| `Strength` | `api.Strength` | 脚本自定义力度/系数。 |
| `Range` | `api.Range` | 脚本自定义范围/参数。 |
| `ScoreMultiplier` | `api.ScoreMultiplier` | 装置完整配置倍率。 |
| `Radius` | `Motion.Source.Radius` / E 的 `Radius` | 装置占位半径；C 的 Radius 是弹珠半径。 |
| `Cooldown` / `MaxTriggersPerBall` | `Trigger` / `TryPrepareTrigger` | 公共冷却和每球访问上限。 |
| `PurchaseQuantity` / `LinkGroup` | 库存与配对系统 | 由游戏购买/布置流程处理，不是弹珠属性。 |

`metadata.value` 是售价，`api.Parameter` 是业务参数，`api.Value` 是弹珠基础价值，三者互不替代。完整业务配置定义见 [DeviceDefinition.cs](../Scripts/Marbles/Rules/DeviceDefinition.cs)。

<a id="compatibility"></a>

## 兼容接口

这些成员仍可供旧包调用；新脚本使用对应的基础能力。

### DeviceContactContext

| 签名 / 属性 | 类型 / 返回值 | 当前语义 |
| --- | --- | --- |
| `MultiplyScore(factor)` | `number → void` | 转发 AddScoreMultiplier，仍只累加 factor - 1。 |
| `Speed` | 只读 `number` | Motion.Speed。 |
| `ScaleVelocity(scale)` | `number → boolean` | Motion.TryScaleSpeed(scale)。 |
| `Exit(direction, speed, angle = 0)` | `string, number, number → boolean` | 从本装置发射；direction 为 "down" 时使用 Down，其余值使用 Forward，再绕 Up 偏转 angle 度。 |
| `ExitPartner(direction, speed)` | `string, number → boolean` | 从 FindPartner 的出口发射；无出口返回 false。 |
| `Consume(refund)` | `boolean → void` | 先 TryUse，再消耗球；新脚本分开调用 TryConsume 和耐久接口。 |
| `TrySettle(score)` | `number → boolean` | 校验金额/球状态并自动 TryUse，再显式金额结算。 |
| `Children(count, replace)` | `integer, boolean → void` | 使用旧固定方向布局生成子球，可替换原球；无成功返回值。保留原生成来源，数量/代数由引擎处理，替换模式读取 BALL_CLONE_VALUE_RETENTION；不是通用等分请求。 |
| `ExitDown` | 读写 `boolean` | 旧方向标志，当前只改位置的传送门不消费它。 |
| `RushChanceAdd` | 只读 `number` | 装置旧概率配置，不参与新 RUSH 触发。 |
| `RushOnSettlement` | 读写 `number` | 旧球概率字段，不参与新 RUSH 触发。 |
| `AddRushChance(amount)` | `number → void` | 写入旧球概率增量，上限 1；新机制不读取。 |

`TryHoldThenLaunch` 已移除；旧包需改用 TryHold 和 OnHoldEnd 中的 TryLaunch。

### DeviceBoardApi 旧概率接口

| 签名 / 属性 | 返回值 / 类型 | 说明 |
| --- | --- | --- |
| `SetRushChanceBonus(key, amount, seconds)` | `boolean` | key 非空且 UTF-8 ≤120 字节；amount 有限 [0,1]；seconds 有限 (0,60]。按来源和 key 新增或替换。 |
| `RemoveRushChanceBonus(key)` | `boolean` | 删除本来源、本 key；成功删除返回 true。 |
| `GetRushChanceBonusEndTime(key)` | `number` | 返回有效截止时间，未知/失效为 0。 |
| `RushChanceBonus` | 只读 `number` | 全盘有效旧概率增量之和，上限 1。 |

保留原存储和生命周期，但累积式 RUSH 不读取这些概率。非法 key、金额或时长抛错；Set 在阶段/来源/视图不允许或达到 256 份上限时返回 false。来源移除/收回、视图丢失、回合结束或到期时失效。

<a id="lifecycle"></a>

## 规则与生命周期

<a id="durability-rules"></a>

### 耐久

耐久按库存实例、回合计算，下一回合恢复至实例有效上限。-1 无限；运行时上限 0 可用于休眠。

即时效果通常用 Trigger 或 Visit + TryUse。储存/充能效果在释放时扣除：接纳、充能、取消、空激活不扣，成功完成一批或一颗由 Lua 用 TryUseOnce 定义。最后一点耐久必须完成已接受的批次或持续时间。

耗尽时停止新作用，已有定时滞留仍可完成；手动存球保留必要的激活和完成事件。持续施力/更新可通过 TryKeepActive 保留有限完成窗口。完成窗口不增加耐久，新批次仍需登记可用次数。

OnHoldEnd 的 TryUse 兼容提前计数的旧效果，不会补扣。延后计数必须使用 TryUseOnce；同批共享 key，下一批换 key。

持续力场每球每次进入扣一次，不逐帧扣。若最后一点耐久后仍要持续整个效果，脚本需明确申请完成窗口；仅按进入计数的力场在耗尽后停止后续施力。

<a id="hold-lifecycle"></a>

### 滞留

- 定时结束在达到请求时长后的固定步末处理；暂停不推进。手动滞留由 ReleaseHeld 结束。
- 完成前先恢复显示、物理、入口线速度和角速度；线速度受释放时当前限速约束。没有完成函数时继续原运动。
- OnHoldEnd 使用新上下文和来源当前姿态；可重新滞留、出射、修改属性或消费球。Trigger / TryPrepareTrigger 在此事件返回 false。
- 来源移除、收回或物理视图丢失时取消，恢复入口运动，不执行完成玩法。
- 球寿命和回合排空计时继续。球消失、寿命到期、排空超时、回合或会话清理不补发完成回调；与到期同一步时清理优先。
- 一颗球完成回调异常不阻止其他球；先前恢复和回调内已成功的操作不回滚。对象池复用不保留旧滞留或隐藏状态。

<a id="effect-history"></a>

### 有效作用历史

HasTriggeredOtherDevice 按实例查询成功效果，不是访问尝试。成功运动、出射、滞留、临时重力、实际属性变化、非中性倍率、整数金币奖励、有效回血和非零场力会记录相应作用。纯 Lua 状态效果用 MarkTriggered。

单纯访问、耐久登记、中性触发、状态文字或失败请求不等于有效作用历史。记录不因装置移除或效果到期撤销；复制/分出继承，独立新球为空。

作用历史与 RUSH 贡献登记是不同机制：TryUse 可以登记本次通用 RUSH 贡献，但不因此把中性效果记为“其他装置的有效作用”。

<a id="rush-rules"></a>

### RUSH 与结算

当前普通球贡献从 2 开始，成功装置触发通常增加 5；同一步重复及同次回调的多个能力去重。滞留完成不再为接纳阶段重复加贡献；持续力场按每次进入登记。OnBallUpdate 的持续操作不会仅因逐步调用反复增加贡献，新的有效访问按引擎规则登记。

底部流失按普通贡献入账；普通/赌博得分区按贡献 ×1.5 或 SetScoreRushContribution 的覆盖值入账。TryConsume、寿命结束、清理及显式 TrySettleAmount 不冒充入区/落底。

RUSH 达到 100 后清零并启动，期间不入账、不刷新；当前基础时长 2 秒，触发球可附加 RushDurationBonus。RUSH 临时价值 +5 通过 EffectiveValue 读取，不写入永久基础价值，也不均分/复制进基础价值。其他 RUSH 与开奖规则见 [Balance.md](Balance.md)。

撞击装置倍率遵循 `1 + Σ(amount - 1)`，基础 1 只算一次。球持有增量、全盘临时增量与其他独立倍率分别维护，到得分区统一合成；直接金额结算只使用 Lua 提供的金额。

### 状态与异常

Lua 实例可跨回调保存普通状态，但引擎不会自动清空所有 Lua 局部变量。使用 RoundId 或 OnRoundStart 重置回合状态；需要模型重建后保留的成长使用命名实例值。出售/删除实例或重开会话释放其状态。

回调结束关闭上下文。滞留恢复、效果到期及来源清理由引擎负责，不依赖 Lua 正常返回；普通效果不提供事务回滚。库存提交、复制/分出及批量生成的原子边界见各自条目。

<a id="examples"></a>

## 最小示例

### 碰撞后累加倍率

```lua
function OnContact(api)
    if not api:Trigger() then return end
    api:AddScoreMultiplier(1.5)
end
```

### 滞留后释放并计数

```lua
function OnContact(api)
    if api.RemainingDurability >= 0
        and api.HeldCount >= api.RemainingDurability then return end
    if not api:TryPrepareTrigger() then return end
    api.Motion:TryHold(api.Parameter)
end

function OnHoldEnd(api)
    local key = "release:" .. api.BallId
    if not api:CanUseOnce(key) then return end
    local motion = api.Motion
    local source = motion.Source
    local released = motion:TryLaunch(source, source.Down, api.Hold.EntrySpeed)
    if not released then
        released = motion:TrySetVelocity(source.Down * api.Hold.EntrySpeed)
    end
    if released then api:TryUseOnce(key) end
end
```

此例按“每球每实例释放一次”命名 key；允许同球反复参与的效果应加入批次编号。Parameter 必须是合法滞留时长。

### 盒形区域连续力

```lua
function OnField(api)
    if not api:OverlapsBox("Field") then return end
    if not api:TryUse() then return end
    api:TryAddForce(api.Forward * api.Strength)
end
```

Field 是该装置制作 Prefab 的节点路径，由脚本选择。

<a id="maintenance"></a>

## 维护与验证入口

引擎提供职责单一、可独立组合的基础能力；装置条件、方向、数值和多阶段流程由 Lua 决定。通用接口不包含特定装置名称、ID 或后续效果。

本文件是装置效果 API 的统一契约。新增或修改接口时更新所属类型、参数、返回值、失败条件、适用事件、生命周期及示例，并同步制作 Lua、受影响 MRes 和行为验证。

| 路径 / 入口 | 用途 |
| --- | --- |
| [MResPacker](../MResPacker/README.md) | 独立制作工具、打包流程和格式限制。 |
| [DeviceScriptSystem.cs](../Scripts/System/DeviceScriptSystem.cs) | 游戏事件分发。 |
| [Scripts/link.xml](../Scripts/link.xml) | Player 裁剪所需 Lua 可访问类型。 |
| [Editor/Validation](../Editor/Validation) | Lua、生命周期、物理及交易行为验证。 |
| Unity 菜单 Marbles ECS / Validate Attribute and Content Runtime | 执行装置运行时行为验证。 |
| [Validation.md](Validation.md) | 验证记录。 |
| [装置实现参考](Device_Lua_Examples.md) | 现有装置组合方式和制作路径。 |

内置制作资源放在 MResPacker，游戏成品放在 Resources/Devices。GameContent.json.Devices 由当前全部 MRes 完整生成；修改文档不会更新包内脚本。
