# 血珠回路 / Blood Marble

Unity **2022.3.62f3c1 + Entities 1.0.11 + URP 14.0.12** 的三维弹珠游戏，实现每日挑战、生产回合、提现、统一开奖、商店、布置、Rush、Prefab 与 Lua 装置。

药物状态：2026-10-22：旧药物系统已移除，后续将用 Lua 重新实现。当前没有药物商品、初始库存、服药入口或药效计时；本次未接入新的 Lua 药物系统。

## 开始游戏

1. 使用 Unity 2022.3 打开本项目，等待导入完成。
2. 运行 `Scenes/TestScene.unity` 从 Day0 开始。屏幕变亮后用 WASD 行走，按住鼠标右键转视角。到弹珠机右侧平台按 E 读纸条，关闭阅读窗口后，到弹珠机正面按 E 开始生产。
3. 生产时按空格发射，用 A / D 或滑条移动发射口。Day0 的目标是 50 分。达到目标后可操作结算开关，也可继续发射直到血量不足。
4. 当天生产成功后按 E 完成当天结算，再按 E 结束休息。当天失败会重新开始 Morning Prep。制作入口见 [每日内容制作指南](DayFramework_Content_Guide.md)。
5. Day1 的目标是 200 分。Morning Prep 期间可在机台旁使用商店终端，买到的装置可在开始发射前布置。


本地 Windows 试玩程序在项目根目录 **`Builds/BloodMarble/BloodMarble.exe`**。复制给其他人时需要复制整个 `BloodMarble` 文件夹。菜单 **Marbles ECS → Build Windows Player** 可更新程序；修改 Excel 后需要重新构建才能更新试玩程序。

| 操作 | 作用 |
|---|---|
| 空格 / 持续发射按钮 | 连发；单发按钮只发一颗 |
| 抽血档位滑条 | 调整每珠耗血、尺寸和质量；基础分独立配置 |
| A / D、方向键或位置滑条 | 横移发射口，也可启用自动往返 |
| 按住左键拖动，松手 | 在盘面空位放置或移动，拖回左侧背包收回，拖到红色售出区出售 |
| 拖动时按 R、右键或 Esc | R 旋转预览，右键或 Esc 取消拖动 |
| 提现 | 当天回合已确认分达到目标时，将剩余血量换为金币 |
| 重新开始整局 | 重置总分、金币、库存和布局 |

## 商店界面

当天启用 `Shop` 时，在 Morning Prep 靠近机台旁平台上的 `Shop Terminal` 按 E 打开商店。点「返回房间」或按 E / Esc 关店并继续行走。商店使用 1920×1080 参考分辨率缩放。商品卡显示名称、稀有度、耐久、出售价格、效果说明和购买价格。较长的说明可在卡片内用滚轮或拖动阅读。

商店使用 `Art/Fonts/ShopChineseSDF.asset`，预烘焙当前内容的中文字符；`ShopChineseFallback.asset` 使用同一字体和采样设置，按需补充新增装置的字符。原有盘面字体独立保留。维护入口为 **Marbles ECS → UI → 修复商店布局与字体**，会更新现有商店场景、商品 Prefab 和商店字库。

## 本版规则

- 普通得分区即时计分，赌博分区汇入唯一待定分池；场内血珠排空后统一抽一次，当前按 30% 全损、30% 返还、25% 双倍、15% 四倍结算。
- 本回合确认分达到当天目标后，可以将剩余血量兑换成金币。回合排空后达到目标即可领取当天奖励，超出目标的分数可增加金币。
- 金钉使该血珠永久得分倍率 ×2；蓝钉使该血珠入得分区的 Rush 概率增加 8 个百分点。同一颗血珠对同一枚钉子只生效一次，不同钉子可叠加。
- 得分区结算前已有 Rush 时，本次分数 ×2；当前球结算后再抽取 Rush。每次触发增加 5 秒，剩余时间最多 20 秒。
- 金币和库存随每日挑战延续。每天休息结束时，盘面上的装置自动收回背包。每次生产从零分开始。Rush 在回合结束清除。重新开局恢复初始资源。
- 商店可付费刷新；装置可购买、布置、收回和出售。本版没有装置升级、合成、其他发射头、病理、器官交易或大型随机事件。

数值边界和与设计表的取舍见 [实现说明](Docs/Implementation.md)。

## 策划调数值

编辑 **[Config/GameBalance.xlsx](Config/GameBalance.xlsx)**。Unity 导入后生成 **`Resources/GameBalance.json`**；也可用 **Marbles ECS → Import Game Balance Excel** 手动导入。校验失败保留上一次有效配置。

Excel 配置发射参数、血量、库存和战役基础规则。每日目标分、通关奖励和盘面在 `Resources/GameDays.json` 配置。重新进入 Play 应用新配置，运行中的模拟持有独立配置副本。

字段说明见 [数值表说明](Balance.md) 和 [每日内容制作指南](DayFramework_Content_Guide.md)。

## 代码结构

| 路径 | 职责 |
|---|---|
| `Scripts/Marbles/Rules/` | 独立于 Unity 的数值规则、配置定义与校验 |
| `Scripts/Marbles/Component/`、`Launcher/Component/`、`Player/Component/`、`Session/Component/` | ECS 数据；每个组件独立文件 |
| `Scripts/System/`、`Scripts/Session/*System.cs` | 发射、效果、计分、回收、生产结算、商店、布置规则 |
| `Scripts/Simulation/MarbleSimulation.*` | 独立 Entities World、固定步编排、输入和只读快照 |
| `Scripts/Physics/PhysX/` | Rigidbody、对象池、碰撞上报、触发区扫掠补检 |
| `Scripts/Presentation/` | 场景构造、盘面投影、相机、分文件 HUD 和交互 |
| `Scripts/Simulation/MarbleGameController.*` | Unity 生命周期和唯一物理驱动 |
| `Editor/Balance/` | `.xlsx` 解析、验证、导入和自动更新 |
| `Tests/EditMode/`、`Tests/PlayMode/` | 真实 Entities 规则测试、真实 PhysX 和场景集成测试 |

玩法权威在 ECS 的组件和 Buffer。MonoBehaviour 只采集输入、展示快照和桥接 PhysX；不会自己扣血、改金币或判定通关。PhysX 是运动唯一权威，不另写 ECS 积分器。系统由独立 World 的编排器显式调度，以确保物理前后事务顺序。

## 验证

Unity 菜单 **Window → General → Test Runner**，分别运行 EditMode 和 PlayMode。具体结果见 [验证记录](Docs/Validation.md)。

这是使用基础几何和程序界面的功能首版；正式美术、音效、其他发布平台和工作簿里的扩展机制仍可在此基础上继续制作。
