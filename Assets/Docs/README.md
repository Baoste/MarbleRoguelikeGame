# 血珠回路 / Blood Marble

Unity **2022.3.62f3c1 + Entities 1.0.11 + URP 14.0.12** 的三维弹珠游戏，实现每日挑战、生产回合、达标结算、统一开奖、权限商店、布置、Rush、Prefab 与 Lua 装置。

药物状态：2026-10-22：旧药物系统已移除，后续将用 Lua 重新实现。当前没有药物商品、初始库存、服药入口或药效计时；本次未接入新的 Lua 药物系统。

## 开始游戏

1. 使用 Unity 2022.3 打开本项目，等待导入完成。
2. 运行 `Scenes/TestScene.unity` 从 Day0 开始。屏幕变亮后用 WASD 行走，按住鼠标右键转视角。到弹珠机右侧平台按 E 读纸条，在旁边的终端确认今日背包，再到弹珠机正面按 E 进入机台。
3. 生产时按空格发射，用 A / D 或滑条移动发射口。Day0 的目标是 200 分。达到目标后可操作结算开关，也可继续发射直到血量不足。
4. 当天生产成功后按 E 完成当天结算，再按 E 结束休息。当天失败会重新开始 Morning Prep。制作入口见 [每日内容制作指南](DayFramework_Content_Guide.md)。
5. Day1 和 Day2 的目标分别是 500 和 1000 分。Day2 开放工票权限抽取，已选装置可用血肉残料安装。


本地 Windows 试玩程序在项目根目录 **`Builds/BloodMarble/BloodMarble.exe`**。复制给其他人时需要复制整个 `BloodMarble` 文件夹。菜单 **Marbles ECS → Build Windows Player** 可更新程序；修改 Excel 后需要重新构建才能更新试玩程序。

| 操作 | 作用 |
|---|---|
| 空格 / 持续发射按钮 | 连发；单发按钮只发一颗 |
| 抽血档位滑条 | 调整每珠耗血、尺寸和质量；基础分独立配置 |
| A / D、方向键或位置滑条 | 横移发射口，也可启用自动往返 |
| 按住左键拖动，松手 | 在盘面空位安装装置。库存模式可依其配置移动、收回或出售 |
| 拖动时按 R、右键或 Esc | R 旋转预览，右键或 Esc 取消拖动 |
| 结算开关 | 当前回合确认分达到目标后，停止供血并等待场内弹珠排空 |
| 重新开始整局 | 独立机台重置总分、血肉残料、库存和布局 |

## 商店界面

在晨间靠近 `Shop Terminal`，按 E 打开装置选择。终端显示装置说明、耐久、安装价和当天的工票权限货架。每个列表可滚动。点「返回房间」或按 E / Esc 可以关闭终端。

终端使用 `Art/Fonts/ShopChineseSDF.asset` 和动态中文后备字库 `ShopChineseFallback.asset`。Unity 菜单 **Marbles ECS → UI → 修复装置终端字体** 更新终端字体引用。

## 本版规则

- 普通得分区即时计分。启用转盘的关卡可累计奖池并统一开奖，具体赔率见 [数值表说明](Balance.md)。
- 公司工票来自通关奖励，用于在开放商店的日期购买装置使用权限。永久权限和余额可跨日使用。
- 血肉残料来自日初配置和装置效果，用于盘面安装。日结清理当日残料和设备，失败重试恢复日初盘面和残料。
- 确认分达标后可以主动结算。排空后按最终分数判定目标，再发放当日工票。

## 策划调数值

编辑 **[Config/GameBalance.xlsx](../Config/GameBalance.xlsx)**。Unity 导入后生成 **`Resources/GameBalance.json`**；也可用 **Marbles ECS → Import Game Balance Excel** 手动导入。校验失败保留上一次有效配置。

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
| `Tests/Day/EditMode/`、`Tests/Day/PlayMode/` | 真实 Entities 规则测试、真实 PhysX 和场景集成测试 |

ECS 组件和 Buffer 持有玩法状态。MonoBehaviour 采集输入、展示快照和桥接 PhysX，物理姿态由 PhysX 更新。系统由独立 World 的编排器显式调度，以确保物理前后事务顺序。

## 验证

Unity 菜单 **Window → General → Test Runner**，分别运行 EditMode 和 PlayMode。具体结果见 [验证记录](Validation.md)。

这是使用基础几何和程序界面的功能首版；正式美术、音效、其他发布平台和工作簿里的扩展机制仍可在此基础上继续制作。
