# 功能开发需求：高性能动态飘字系统（Floating Text System）

## 1. 开发背景

当前项目为基于 ECS 架构的 Unity 柏青哥式弹珠游戏。

需要新增一套动态飘字系统，在以下事件发生时提供视觉反馈：

- 弹珠碰撞钉子时，显示 `+N`、`×N` 等效果。
- 弹珠触发装置时，显示对应的数值变化或倍率变化。
- 弹珠得分时，显示得分数值。
- 为后续特殊装置、连锁反应、暴击等效果预留扩展接口。

游戏后期可能同时存在大量弹珠，并在短时间内产生大量碰撞和装置触发事件，因此该系统必须重点考虑运行性能、内存分配和渲染开销。

## 2. 开发前置要求

在编写代码前，请先完整阅读当前项目中与以下内容相关的代码：

- 现有 ECS 框架及 System 生命周期。
- 弹珠碰撞检测及碰撞事件处理。
- 钉子与装置的效果触发机制。
- 弹珠数值变化与得分结算机制。
- 现有事件系统及表现层架构。
- 现有资源加载与配置管理方式。
- 当前渲染管线及 Shader 实现。

优先复用现有框架、事件接口和代码规范，不要重复搭建已有的基础设施。

下文中的类名、数据结构及接口均为建议设计，可以根据当前项目的实际架构调整，但必须保持设计目标一致。

## 3. 核心技术方案

采用以下方案：

**ECS 事件驱动 + 固定容量数据池 + 字符图集（Glyph Atlas）+ 动态 Mesh 批量渲染。**

不采用以下方案：

- 每次触发时 Instantiate / Destroy TextMeshPro GameObject。
- 为每条飘字创建独立的 GameObject。
- 为每条飘字创建独立的 ECS Entity。
- 使用大量独立的 Canvas 或 UI Text 组件。
- 为每条飘字单独创建材质。
- 为每条飘字创建独立的协程或 Tween。

整个飘字系统应尽可能通过一个 MeshRenderer、一个 Mesh 和一个共享 Material 完成渲染。

在材质、渲染 Pass、相机及排序条件允许的情况下，实现一次 Draw Call 完成全部飘字绘制。

## 4. 系统架构

建议将系统划分为以下模块：

```text
现有游戏逻辑层
├── 弹珠碰撞系统
├── 钉子效果系统
├── 装置效果系统
└── 得分结算系统
         │
         │ 发布表现事件
         ▼
FloatingTextEvent
         │
         ▼
FloatingTextSystem
├── 消费事件
├── 事件合并
├── 优先级管理
├── 生命周期管理
└── 数据池管理
         │
         ▼
FloatingTextBatchRenderer
├── 数值转字符
├── 字符 UV 查询
├── 顶点数据构建
└── Mesh 缓冲区更新
         │
         ▼
MeshRenderer + Material + Shader
```

游戏逻辑层只负责发布事件，不负责管理飘字对象、生命周期、动画及渲染。

飘字系统属于纯表现层系统，不得反向影响游戏逻辑。

即使关闭飘字系统，或者部分飘字由于性能限制被丢弃，游戏的碰撞、装置效果及计分结果也必须保持完全一致。

## 5. 事件数据设计

建议设计统一的 FloatingTextEvent 数据结构。

```csharp
public enum FloatingTextType : byte
{
    Additive,
    Multiplier,
    Score,
    Critical,
    Custom
}

public struct FloatingTextEvent
{
    public Vector3 Position;
    public int SourceId;
    public double Value;
    public FloatingTextType Type;
    public int StyleId;
    public byte Priority;
}
```

具体数值类型应优先与项目现有计分、倍率及数值系统保持一致，避免大数溢出或精度丢失。

| 字段 | 作用 |
|---|---|
| Position | 飘字生成的世界坐标 |
| SourceId | 触发来源的稳定标识，用于事件合并 |
| Value | 实际显示的数值 |
| Type | 加算、倍率、得分等类型 |
| StyleId | 飘字样式配置索引 |
| Priority | 事件显示优先级 |

业务系统不应直接拼接字符串，例如：

```csharp
string text = "+" + score.ToString();
```

而是传递原始数值和类型，由飘字系统负责格式化。

建议提供统一的事件发布接口，供钉子、装置和得分系统调用。例如：

```csharp
FloatingText.Emit(
    position,
    sourceId,
    value,
    FloatingTextType.Additive
);
```

以上仅为接口示例，实际实现应遵循项目现有 ECS 事件机制。

如果当前碰撞事件由多线程 Job 产生，应使用现有的线程安全事件传递机制，不允许从工作线程直接调用 Unity 渲染 API。

## 6. 字符图集设计

创建一张独立的字符图集，包含所有基础数值字符：

```text
0 1 2 3 4 5 6 7 8 9
+ - × . % ! K M B T e
```

支持以下显示形式：

```text
+10
-5
×2
×1.5
+1000
+1.5K
×100
```

每个字符对应一个 Quad，通过 UV 坐标访问字符图集中对应的区域。例如 `+128` 由四个 Quad 组成，共计 16 个顶点、8 个三角形。

要求：

- 所有基础字符共用同一张纹理。
- 所有飘字尽可能共用同一个材质。
- 字符之间应具有足够的 Padding，防止纹理采样串色。
- 支持字符间距配置。
- 支持整体水平居中对齐。
- 支持不同字符宽度或等宽字符。
- 支持小数、负数及大数缩写。
- 数值格式化应避免高频托管字符串分配。
- 为后续扩展字符预留接口。

字体、描边、字符图集和数值格式化规则不应硬编码在业务系统中。

如果项目目前没有可使用的字符图集，请提供可直接运行的临时图集或编辑器生成工具，方便后续由美术替换。

## 7. 数据池与生命周期管理

飘字系统使用预分配的固定容量数据池，而不是不断创建和销毁对象。

建议数据结构：

```csharp
public struct FloatingTextData
{
    public Vector3 Position;
    public Vector2 Velocity;
    public float SpawnTime;
    public float Lifetime;
    public float Scale;
    public Color32 Color;
    public double Value;
    public FloatingTextType Type;
    public int SourceId;
    public int StyleId;
    public byte Priority;
}
```

根据具体实现，可以额外保存字符索引、字符数量、缓冲区偏移等渲染数据。

要求：

- 初始化时完成主要内存分配。
- 运行过程中复用已有数据槽位。
- 飘字生命周期结束后自动回收。
- 避免频繁创建临时集合。
- 避免高频 LINQ、装箱、闭包及字符串分配。
- 避免逐条飘字执行协程。
- 数据池容量达到上限时，不得自动无限扩容。
- 常态运行及压力测试时，尽可能实现零 GC Alloc。

可以根据现有项目架构使用预分配数组、NativeArray、NativeList 或其他适合的数据容器。

不强制引入 Unity DOTS、Burst 或新的 ECS 框架。

## 8. 批量 Mesh 渲染

实现 FloatingTextBatchRenderer，负责将所有存活飘字转换为统一的 Mesh。

### 8.1 渲染要求

每个字符 Quad 包含：

- Position
- UV
- Vertex Color
- 必要的扩展动画数据

所有飘字共享顶点缓冲区和索引缓冲区。

优先考虑使用：

```csharp
Mesh.MarkDynamic();
Mesh.SetVertexBufferData();
Mesh.SetIndexBufferData();
```

可以根据当前 Unity 版本和项目架构选择合适的 Mesh 更新 API。以上为 API 名称示意，并非可直接编译的调用代码。

### 8.2 性能要求

- 顶点和索引缓冲区提前分配。
- 尽可能复用索引数据。
- 不要在每帧创建新的 Mesh。
- 不要在每帧创建新的顶点数组。
- 不要为每条飘字单独提交渲染请求。
- 正确处理 Mesh Bounds，防止飘字移动时被错误剔除。
- 正确处理透明混合、渲染层级和 Sorting Layer。
- 确保文字能够显示在弹珠和装置上方。
- 根据最大字符容量选择正确的 IndexFormat，避免超过 16 位索引范围。

第一版可以采用 CPU 更新动态 Mesh 的方式，但应将数据管理、网格构建和动画计算解耦，为后续 GPU 动画优化预留空间。

## 9. 飘字动画

基础飘字动画包括：

1. 生成时快速放大。
2. 短暂回弹至正常大小。
3. 持续向上移动。
4. 生命周期后半段逐渐淡出。
5. 生命周期结束后回收。

支持通过配置调整以下属性：

- 生命周期。
- 初始缩放。
- 最大缩放。
- 上浮速度。
- 水平随机偏移。
- 缩放曲线。
- 透明度曲线。
- 颜色。
- 字符间距。
- 生成位置偏移。

不同事件可以使用不同样式，例如：

| 类型 | 建议表现 |
|---|---|
| 普通钉子 | 较小、快速消失 |
| 加算装置 | 中等大小、向上漂浮 |
| 倍率装置 | 明显放大、回弹 |
| 最终得分 | 较大、停留时间较长 |
| 特殊高倍率 | 高亮颜色、强化缩放动画 |

所有表现参数应支持后续策划及美术调整，不要在游戏逻辑代码中硬编码。

## 10. GPU 动画优化预留

后续如果 CPU 更新 Mesh 成为性能瓶颈，可将动画计算迁移至 Shader。

每个字符的顶点可以额外存储：

```text
OriginPosition
LocalOffset
SpawnTime
Lifetime
Velocity
Scale
Color
```

Shader 根据统一时间参数计算：

```hlsl
float age = _CurrentTime - spawnTime;
float t = saturate(age / lifetime);
float scale = CalculateScale(t);
float2 offset = velocity * age;
float alpha = CalculateAlpha(t);
```

通过 Shader 完成：

- 上浮。
- 水平漂移。
- 缩放回弹。
- 透明度变化。
- 生命周期内的颜色变化。

需要以整条文字的中心作为缩放基准，而不是分别缩放每个字符。

CPU 仅负责事件处理、数据写入、槽位管理和生命周期回收。

如果采用固定网格槽位，应尽可能只更新发生变化的缓冲区区域，并确保已回收槽位不再产生可见几何体。

当前阶段不强制实现 GPU 动画，也不要求引入 Compute Shader、GPU Instancing 或 VFX Graph。

优先保证基础系统结构正确，再根据实际性能测试决定是否优化。

## 11. 高频事件合并

由于游戏后期可能出现大量弹珠同时触发同一个装置，需要提供可配置的事件合并机制。

例如，同一个钉子在短时间内产生：

```text
+10
+10
+10
```

可以在表现层合并显示为：

```text
+30
```

建议事件合并条件：

- SourceId 相同。
- FloatingTextType 相同。
- StyleId 相同。
- 满足该类型的数值合并规则。
- 两次事件间隔小于配置的合并时间窗口。

默认合并时间窗口可以设定为 0.1 秒。

注意：

- 加算事件可以按实际含义累加。
- 倍率事件不能直接采用加算事件的合并方式。
- 不同弹珠的倍率变化不应随意合并为一个倍率。
- 不明确合并语义的事件默认不合并。
- 事件合并应根据实际效果作用对象判断是否需要额外的目标标识。
- 合并仅发生在表现层，不改变实际数值。
- 事件合并应支持按类型、来源或样式关闭。
- 不应为了等待合并而明显延迟首次飘字反馈。

## 12. 优先级与容量限制

为避免高频触发导致性能和视觉效果失控，需要设置飘字数量上限。

默认优先级建议如下：

```text
最终得分
    >
特殊高倍率
    >
普通装置触发
    >
普通钉子碰撞
```

当数据池已满时：

1. 优先尝试合并符合条件的事件。
2. 若无法合并，比较新事件与已有事件的优先级。
3. 高优先级事件可以替换最旧的低优先级事件。
4. 低优先级事件不得覆盖更高优先级的事件。
5. 无法分配时，允许直接丢弃该表现事件。

该过程不得影响游戏逻辑。

优先级及回收策略应可以扩展，并避免在高频事件下反复遍历整个数据池寻找回收对象。

## 13. 配置系统

建议使用项目现有配置系统。如果项目使用 ScriptableObject，可以建立 FloatingTextConfig 和 FloatingTextStyleConfig。

全局配置建议：

| 参数 | 默认值 |
|---|---:|
| 最大存活飘字数量 | 1024 |
| 单条飘字最大字符数 | 12 |
| 普通飘字生命周期 | 0.5 秒 |
| 得分飘字生命周期 | 0.8 秒 |
| 高倍率飘字生命周期 | 1.2 秒 |
| 事件合并时间窗口 | 0.1 秒 |
| 字符图集尺寸 | 512 × 512 |
| 事件合并开关 | 开启 |
| 飘字系统总开关 | 开启 |

以上参数仅作为初始配置，不代表实际性能上限。

每种飘字样式应具有独立配置，方便后续调整大小、颜色、动画和显示优先级。

## 14. 与现有项目的接入要求

请根据现有代码找到合适的事件发布位置，实现以下接入：

### 钉子碰撞

当弹珠碰撞钉子并实际产生数值变化时，在碰撞位置生成对应飘字。

单纯发生碰撞但没有产生数值变化时，是否显示文字由配置决定，默认不显示。

### 装置触发

当装置实际完成一次数值效果时，根据效果类型生成 `+N`、`×N` 等飘字。

不应仅因为弹珠进入碰撞范围就生成飘字，必须与实际生效逻辑对应。

### 得分结算

当弹珠得分成功结算后，在对应得分位置生成得分飘字。

应确保显示数值与实际结算数值一致；若采用缩写，应按照配置的精度格式化。

### 场景及日程切换

结合项目现有场景管理和每日流程状态机，提供：

```csharp
ClearAll();
```

或等价接口。

在离开生产阶段、重新开始游戏、切换场景或重置弹珠机等需要清理表现数据的节点，由现有生命周期机制调用。

具体清理时机应避免打断必要的结算反馈。

同时确保 Mesh、Native 容器和其他运行时资源能够正确释放，不发生内存泄漏或跨场景重复初始化。

## 15. 调试与压力测试

提供独立的飘字测试工具，可以在不依赖实际弹珠碰撞的情况下批量生成飘字。

测试规模至少包括：

```text
100 条同屏飘字
500 条同屏飘字
1000 条同屏飘字
2000 条同屏飘字
```

测试时允许临时调整数据池容量。

同时支持连续高频事件注入测试，而不仅仅是一次性生成大量飘字。

建议提供以下调试指标：

- 当前存活飘字数量。
- 当前字符 Quad 数量。
- 每秒接收事件数量。
- 每秒生成飘字数量。
- 每秒合并事件数量。
- 每秒丢弃事件数量。
- 数据池使用率。
- Mesh 更新耗时。

通过 Unity Profiler 和 Frame Debugger 验证：

- GC Alloc。
- Main Thread 耗时。
- Mesh 数据上传耗时。
- Draw Call 数量。
- Batches 数量。
- 内存是否持续增长。
- GPU 渲染开销。

调试功能本身不应在正式运行时造成明显额外开销。

## 16. 验收标准

完成开发后，应满足以下要求：

- [ ] 弹珠撞击钉子时能够显示正确的数值飘字。
- [ ] 装置触发时能够显示正确的倍率和加算飘字。
- [ ] 得分结算时能够显示正确的得分数值。
- [ ] 支持文字上浮、缩放、回弹和淡出动画。
- [ ] 所有飘字采用批量 Mesh 渲染。
- [ ] 不为单条飘字创建 GameObject 或 ECS Entity。
- [ ] 不为单条飘字创建独立 Material。
- [ ] 不发生频繁的 Instantiate / Destroy。
- [ ] 数据池和 Mesh 缓冲区可以复用。
- [ ] 稳态运行尽可能达到零 GC Alloc。
- [ ] 支持高频事件合并。
- [ ] 支持容量限制和优先级淘汰。
- [ ] 支持独立配置不同飘字样式。
- [ ] 支持清理、重置及资源释放。
- [ ] 支持关闭整个飘字系统。
- [ ] 关闭飘字系统后，不影响任何游戏逻辑。
- [ ] 不破坏项目现有 ECS 架构及装置系统。
- [ ] 提供可运行的压力测试工具。

## 17. 开发原则与交付要求

本次开发遵循以下原则：

1. 优先保证系统解耦和可维护性。
2. 优先使用批量渲染和预分配内存。
3. 游戏逻辑与视觉表现严格分离。
4. 不对现有业务系统进行无关重构。
5. 不引入不必要的第三方依赖。
6. 不进行没有性能数据支持的过度优化。
7. 所有动画与样式参数尽可能数据驱动。
8. 新增装置只需要发布统一事件，不需要了解飘字系统的内部实现。

请直接在当前项目中完成代码、必要资源及配置的实现，而不是仅提供示例代码。

开发完成后，请说明：

- 新增及修改了哪些文件。
- 飘字系统的实际运行流程。
- 如何在新的装置代码中触发飘字。
- 如何新增和修改飘字样式。
- 如何配置字符图集。
- 如何执行压力测试。
- 当前实现采用的性能优化手段。
- 已知问题及尚未实现的优化。
- 实际执行了哪些测试，以及哪些性能指标尚未经过验证。

如果上述建议与当前项目架构存在冲突，请优先遵循现有架构，并说明调整原因。

## 18. 实际接入与使用

`MarbleSimulation` 在主线程驱动自己的 Entities World，PhysX 回调传入接触数据。接触去重、Lua 效果和结算提交后，才把原始数值写入该 World 的有界表现队列。`MarbleGameController` 在 `LateUpdate` 消费队列，使用一套槽位、一个动态 Mesh、一个 MeshRenderer 和共享材质绘制。默认相机为 `MainCamera`。控制器 Inspector 的 `Floating Text` 栏可以指定配置与相机，现有场景会自动使用默认资源。配置沿用项目的 Resources JSON 和 `ConfigJson`，在控制器启动时读取。

现有实体钉的数值字段是固定倍率和旧 RUSH 概率，普通钉的倍率为 `1`。倍率文字取自装置倍率实际接受的变化，沿用 `1 + Σ(amount − 1)`。钉子的无数值接触默认静默，可用 `showNeutralPins` 开启。装置 Lua 的基础价值、倍率、金币和血量反馈在 API 实际修改数值时产生。结算按回合整数账本的实际入账差额显示，包含回合封顶。赌博区在开奖后显示带符号的差额，位置沿用最后一个入池接触点。

开新回合、重开和恢复日程检查点都会清空队列并使上一批飘字失效。离开生产阶段时移除装置和钉子提示，已产生的得分继续自然淡出。控制器禁用时清空，场景卸载时销毁批量 Mesh 与渲染节点。动画用非缩放时间，沿相机朝上的方向移动，机器俯视镜头下仍可见上浮。

### 装置调用

`AddScoreMultiplier`、`AddBase("BALL_BASE_VALUE", amount)`、`TrySettleAmount` 等现有接口直接产生反馈。自定义效果可在成功分支调用 `api:EmitFloatingText("critical", amount, styleId)`。C# 发布侧可调用 `MarbleGameController.EmitFloatingText`，传入 `FloatingTextEvent`。整数金额使用 `FloatingTextEvent.FromInteger`，保留源值精度。[装置 Lua API](Device_Lua_API.md#emitfloatingtext)

### 配置样式

- 编辑配置的 `styles`，给新样式分配唯一正整数 `id`。在 `defaultStyles` 指定默认样式，或在自定义事件中传入 `styleId`。`HighMultiplier` 使用 `highMultiplierThreshold` 阈值，显式 `styleId` 优先。`priority` 越大越晚绘制，且可替换最低优先级中最旧的条目。默认优先级依次为得分、高倍率、普通装置、钉子。[Config.json](../Resources/FloatingText/Config.json)
- `height` 用世界单位。`offset` 的三个数依次是沿相机向右、向上和朝相机的距离。`riseSpeed` 与 `horizontalSpeed` 用世界单位每秒。`spacing` 是相对于字高的间距，`monospace` 用图集中最宽字的宽度作为步进。
- `scaleCurve`、`alphaCurve` 是 `[归一化时间, 值]` 的有序关键点，首尾时间固定为 `0` 和 `1`。缩放值 `-1`、`0`、`1` 分别表示 `initialScale`、正常大小和 `peakScale`。透明度范围为 `0–1`。`color` 使用十六进制颜色。
- `decimals` 和 `compactDecimals` 分别控制普通和缩写格式的最大小数位，`abbreviateAt` 控制主动缩写阈值。设为 `0` 时优先尝试完整数字。字符预算不足时依次使用可容纳的精度、`K/M/B/T` 或科学计数法。`prefix` 和 `suffix` 可以引用图集字符。`showValue: false` 仅绘制此前后缀。
- 合并由 `mergeEnabled`、`mergeTypes`、`mergeSeconds`、`unmergedSources` 和样式的 `merge` 共同控制。`Target` 将稳定球 ID 纳入合并键，`Source` 可用于共享钱包等同单位增量，`None` 独立显示。`unmergedSources` 的条目可写成 `{"source":"Device","id":42}`，装置用 `api.InstanceId`，钉子用 `TargetId`。倍率、得分和自定义类型各自保留。首次事件当帧显示，后续合并更新数字并沿用原动画寿命。`enabled: false` 关闭整个系统。

### 替换图集

默认图集以项目已有的 Liberation Sans 栅格化，覆盖 `0–9 + - × . % ! K M B T e`。纹理 R 通道是字面覆盖率，G 通道是包含描边的覆盖率。保留线性采样、Bilinear、Clamp、关闭 Mipmap 和无压缩导入设置。`Glyphs.json` 的 `x`、`y` 以贴图左上角为原点，`width`、`height` 用像素。推荐所有字形统一基线和矩形高度，每格在描边外留至少 3 像素空白。修改贴图尺寸时同步修改元数据和导入的 `Max Size`，为新字符增加对应条目，并在 `Numbers.mat` 绑定贴图和调整描边颜色。[图集](../Resources/FloatingText/Atlas.png) · [字形定义](../Resources/FloatingText/Glyphs.json) · [共享材质](../Resources/FloatingText/Numbers.mat)

### 压力测试

1. 进入 Play Mode，打开 **Marbles ECS → Floating Text → Pressure Test**。空场景会自动建测试相机，也可以选择现有相机。
2. 点击 **100 / 500 / 1000 / 2000** 预设。预设会把临时容量提高到至少对应数量。勾选 **Freeze batch** 后点预设，可保留一批同屏文字供检查。取消勾选以观察动画和回收。
3. 用 **Start continuous** 按设定速率持续注入。**Repeat mergeable events** 生成重复的加算键。窗口显示存活、字符 Quad、池占用、收取、生成、合并、丢弃速率及 CPU、上传、分配和渲染计数。帧级计数包含当前 Editor 场景及其他渲染工作。[压力测试窗口](../Editor/FloatingText/FloatingTextStressWindow.cs)
4. 在 Profiler 的 CPU 模块查看 `FloatingText.ProcessEvents`、`FloatingText.BuildMesh`、`FloatingText.UploadMesh`，在 Rendering 模块比较 Draw Calls 与 Batches。Frame Debugger 中定位 `Floating Text Batch` 的透明绘制。运行时网格每帧只上传活动顶点区域，索引在初始化时一次上传，容量较大时自动使用 `UInt32`。GPU 动画留有独立的曲线采样、池和网格构建边界。[Rendering Profiler](https://docs.unity3d.com/2022.3/Documentation/Manual/ProfilerRendering.html) · [Frame Debugger](https://docs.unity3d.com/2022.3/Documentation/Manual/frame-debugger-window.html)

`FloatingTextTests` 验证格式化、事件合并、过载、生命周期、网格和连续压力。`GameContentValidation` 中的飘字验证覆盖现有 Lua 装置、记分去重、账本封顶与赌博区开奖。[自动化测试](../Tests/Day/EditMode/FloatingTextTests.cs) · [玩法验证](../Editor/Validation/GameContentValidation.FloatingText.cs)

### 实现文件

| 作用 | 文件 |
| --- | --- |
| 数值事件、队列、池、格式化和批量绘制 | `Scripts/Marbles/Contracts/FloatingTextEvent.cs`、`Scripts/Presentation/FloatingText/` |
| 钉子和装置生效点 | `MarbleContact.cs`、`MarblePin.cs`、`MarbleBody.cs`、`ContactResolutionSystem.cs`、`DeviceContactContext.cs`、`DeviceEventContext.cs`、`DeviceBoardApi.cs`、`FloatingTextFeedback.cs` |
| 得分和流程接入 | `SettlementSystem.cs`、`ContentSettlementSystem.cs`、`GamblingWheelSystem.cs`、`SimulationContext.cs`、`MarbleSimulation.cs`、`MarbleGameController` 的飘字及生命周期分部、`DayCheckpoint.cs` |
| 渲染和配置资源 | `Rendering/FloatingText.shader`、`Resources/FloatingText/` |
| 旧表现迁移 | `Scenes/TestScene.unity`、`Prefabs/Marble.prefab` 移除旧组件引用。移除 `MarblePinHitText.cs`、`WorldTextEffectPool.cs`、`MultiplierText.prefab` |
| 工具和回归 | `Editor/FloatingText/FloatingTextStressWindow.cs`、`Editor/Validation/GameContentValidation*.cs`、`Tests/Day/EditMode/FloatingTextTests.cs` |

### 本次验证结果

Unity `2022.3.62f3c1` 的 EditMode **24/24**、PlayMode **7/7** 通过，现有 Lua/内容验证通过 **11,772** 条断言。D3D11 离屏读回验证了文字覆盖前方不透明几何体、淡出及到期回收。连续压力按 60 Hz 模拟每帧 160 条，预热后采样 600 帧共 96,000 条，注入与更新合计平均 **3.35 ms/帧**，该段主线程托管分配为 **0 B**。

隔离 Win64 Development Player 使用 i5-14600KF、RTX 4070 SUPER、D3D11、1280 × 720。每档预热后采样 120 帧，CPU 更新包含事件消费、顶点构建和提交，上传列来自 `FloatingText.UploadMesh`。

| 同屏条数 | 字符 Quad | CPU 更新均值 | Mesh 上传均值 |
| ---: | ---: | ---: | ---: |
| 100 | 500 | 0.219 ms | 0.013 ms |
| 500 | 2,500 | 0.860 ms | 0.020 ms |
| 1,000 | 5,000 | 1.407 ms | 0.029 ms |
| 2,000 | 10,000 | 2.013 ms | 0.042 ms |

四档采样的飘字更新托管分配均为 **0 B**，同一个预分配 Mesh 在该轮采样中的报告内存大小为 2,881,144 B。已验证每批一个子网格、一个材质和一个 Shader Pass。后台验证环境的 Frame Debugger、整帧 Draw Calls、Batches 和 GPU 时间没有有效样本，长期内存趋势也有待实测。当前由 CPU 更新动画。日志及测试报告保存在项目根目录的 `.utmp/floating-text/`。
