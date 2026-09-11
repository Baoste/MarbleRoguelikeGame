# 血珠回路 / Blood Marble

Unity **2022.3.55f1c1 + Entities 1.0.16 + URP 14** 的三维弹珠游戏首版。参考 `PachinkoECSDesign_3D.xlsx`、更新后的 `Docs/GameplayFlow.md` 和原有代码，实现回合、提现、统一开奖、阶段成长、商店、布置、Rush、两类钉子和弹性药物的可玩闭环。

## 开始游戏

1. 使用 Unity 2022.3 打开本项目，等待导入完成。
2. 打开 **`Assets/Demo/Game/MarbleGame.unity`**，点击 Play。也可通过 **Marbles ECS → Create Game Scene** 创建新场景。
3. 在库存选中赠送的钉子，左键点击盘面放置；开始回合后按住空格发射，用 A / D 或滑条移动发射口。
4. 达到累计目标后可提现；或者继续发射直到血量不足以发射。已有血珠全部回收后，统一开奖并进入商店或结束整局。


本地 Windows 试玩程序在项目根目录 **`Builds/BloodMarble/BloodMarble.exe`**。复制给其他人时需要复制整个 `BloodMarble` 文件夹。菜单 **Marbles ECS → Build Windows Player** 可更新程序；修改 Excel 后需要重新构建才能更新试玩程序。

| 操作 | 作用 |
|---|---|
| 空格 / 持续发射按钮 | 连发；单发按钮只发一颗 |
| 抽血档位滑条 | 调整每珠耗血、尺寸和质量；基础分独立配置 |
| A / D、方向键或位置滑条 | 横移发射口，也可启用自动往返 |
| 库存选择 + 左键盘面 | 回合前自由放置、移动；按住 Shift 辅助对齐网格 |
| 右键 / 收回 / 出售按钮 | 取消选择、收回装置、出售装置 |
| 使用药物 | 消耗库存，对药效期间新发射血珠生效 |
| 提现 | 已确认累计分达到目标时，将剩余血量换为金币 |
| 重新开始整局 | 重置总分、金币、库存和布局 |

## 本版规则

- 普通得分区即时计分，赌博分区汇入唯一待定分池；场内血珠排空后统一抽一次，默认 50% 获得待定分 ×3，否则清零。
- 提现门槛使用“此前累计分 + 本回合即时分”，待定分不参与。累计分 **等于目标也通过**；跨过多个目标直接进入下一个未通过阶段，并结算越级奖励。
- 金钉使该血珠永久得分倍率 ×2；蓝钉使该血珠入得分区的 Rush 概率增加 8 个百分点。同一颗血珠对同一枚钉子只生效一次，不同钉子可叠加。
- 得分区结算前已有 Rush 时，本次分数 ×2；当前球结算后再抽取 Rush。每次触发增加 5 秒，剩余时间最多 20 秒。
- 弹性剂、缓弹剂分别让碰撞恢复系数乘 1.5、0.65，默认持续 12 秒。剂量独立计时并乘算，最终系数限制在 0..1；已发射血珠保留出生时属性。暂停不推进药效。
- 金币、总分、库存、布局跨回合保留；药效和 Rush 在回合结束清除。重新开局恢复初始资源，本版没有跨应用存档。
- 商店可付费刷新；装置可购买、布置、收回和出售。本版没有装置升级、合成、其他发射头、病理、器官交易或大型随机事件。

数值边界和与设计表的取舍见 [实现说明](Docs/Implementation.md)。

## 策划调数值

编辑 **[Config/GameBalance.xlsx](Config/GameBalance.xlsx)**。Unity 导入后生成 **`Resources/GameBalance.json`**；也可用 **Marbles ECS → Import Game Balance Excel** 手动导入。校验失败保留上一次有效配置。

配置包含发射参数、血量、弹性、Rush、阶段目标与奖励、商店价格、药物、钉子、盘面几何和终点分区。运行时读取 JSON，不依赖 Excel、Python 或第三方解析插件。重新进入 Play 应用新配置，运行中的模拟持有独立配置副本。

字段说明见 [数值表说明](Docs/Balance.md)。原设计工作簿保留为架构参考，运行数值集中在新的 `GameBalance.xlsx`。

## 代码结构

| 路径 | 职责 |
|---|---|
| `Scripts/Marbles/Rules/` | 独立于 Unity 的数值规则、配置定义与校验 |
| `Scripts/Marbles/Component/`、`Launcher/Component/`、`Player/Component/`、`Session/Component/` | ECS 数据；每个组件独立文件 |
| `Scripts/System/`、`Scripts/Session/*System.cs` | 发射、效果、计分、回收、阶段、商店、布置规则 |
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
