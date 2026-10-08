# Balance Overview

## Source of Truth

1. `Assets/Config/GameBalance.xlsx` → 自动导入 `Assets/Resources/GameBalance.json`：血量、发射、容量、战役和阶段。
2. `Assets/Resources/GameContent.json`：34 个属性、15 个装置、12 个药物的运行时唯一目录。
3. `Assets/Scenes/TestScene.unity`：盘面几何、Collider、PhysicMaterial、Launcher、Zone Inspector 值。
4. 代码：结算顺序、叠加、上限、商店保底、Cashout 条件和失败条件。

## 实际 Gameplay Loop

`Build（必须放完未布置装置） → Playing（每回合补满 BloodCapacity） → Draining → Shop → Build`。确认总分达到当前累计目标且仍有血量时可 Cashout；提现把剩余血量清零并换金币，场上球继续结算。未达到目标则 Lost，跨过最后阶段则 Won。

当前没有独立 SpecialBall 目录。药物只影响生效窗口内新发射的球；装置、库存、金币、累计分和布局跨回合保留。

## 统一核心数值

- 初始/每回合血量：100.0
- 抽血档位：1.0–5.0
- 单球基础价值：1.0
- 同时活动球上限：256
- Rush：阈值 8.0，倍率 ×2.5，持续 5.0 秒，上限 8.0 秒
- 赌博：0/1/2/4 倍，权重分别为 30.0% / 30.0% / 25.0% / 15.0%
- 初始金币：30；刷新：2；商品槽：4
- 经验物理样本：1800；场景依赖指纹：e0836128665a29db70ba78b9dbfa4232

## Round Progression

| Round | 累计目标 | 基础奖励 | 越级奖励 | Cashout Rate |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 120 | 22 | 12 | 0.7 |
| 2 | 380 | 28 | 15 | 0.75 |
| 3 | 815 | 36 | 20 | 0.8 |
| 4 | 1410 | 46 | 25 | 0.9 |
| 5 | 2200 | 58 | 32 | 1.0 |
| 6 | 3145 | 75 | 40 | 1.1 |

## Hard-coded / 分散参数

- `AttributeRuntime.RadiusToWorld = 0.16`。
- 稀血剂穿透 30%、传送锁 0.5 秒、分流角度/保留率等行为值仍在代码中；四叶草概率已迁移到 `DeviceDefinition.Value`。
- `ContentSettlementSystem` 与 `DeviceEffectSystem` 包含若干行为阈值（资本主义价值 6、分流价值 2）。
- 这些值已列入冲突/债务报告；本次不为统一配置而重写玩法系统。
