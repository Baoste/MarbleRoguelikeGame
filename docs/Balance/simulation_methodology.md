# Simulation Methodology

## Hybrid Simulation

第一层由 `BalancePhysicalSampler` 保存当前 Editor Scene Setup，在临时 additive Scene 中克隆 `TestScene/PhysicsRoot`，暂时禁用原根对象并手动推进 Unity Physics；采样结束后恢复原 Scene Setup。采样使用实际 Collider、Rigidbody、PhysicMaterial、Gravity、Launcher 旋转和 `MarblePhysicsBridge.Simulate`。对 Left/Center/Right 三条发射带与低/中/高三档流量分层，共采样 1800 球。

每个样本记录 Seed、发射位置、流量、终点、倍率、Rush 概率、飞行时间、Pin/Device 接触。原始结果见 `physical_samples.csv`，汇总见 `physical_profile.json`。

第二层从经验终点分布有放回抽样，执行战役、Rush、共享赌博池、累计阶段目标、Cashout、金币、商店保底、购买和刷新。每个策略 10,000 Runs，主 Seed=20260930。

商店购买力探针使用独立派生 Seed，不改变主模拟的 Offer 或购买 RNG。Round 1–4 检查“策略最想买的 2 件 + 1 次刷新”，Round 5+ 检查“策略最想买的 3 件 + 3 次刷新”；达标率目标为每种策略、每个有样本节点至少 75%。Stage 6 已无后续商店，仅作为终局假想购买力检查。

## 策略

- Conservative：选择最低 Void 路线，使用 25% 流量档，达到条件即提现，偏好稳定 Rush Chance，最多花 45% 现金，不刷新。
- Balanced：选择最高平均区倍率路线，平时低流量、Rush 高流量，保留 28% 现金，最多刷新一次。
- Aggressive：选择高倍/赌博区暴露最高路线，中流量起步、Rush 高流量，最多花 95% 现金并刷新两次。

## 可复现性

- 经济 PRNG 完全由每行 `seed` 重放。
- PhysX 的 Seed 固定发射选择，但 Unity PhysX 不承诺跨平台位级确定性；报告保存 Unity 版本和场景依赖 Hash。
- Proposal 使用同一物理 Profile 与同一组经济 Seed（common random numbers），降低方案差值噪声。

## Dead Run 判定

从已结算弹珠在线估计单球收益，并结合实测路线的终点均值/方差、剩余可发射球数和单侧 `mean + 1.645σ` 上界。若在血量仍高于 20% 时估计翻盘概率低于约 5%，之后又继续至少 15 球且最终失败，则记为 Dead Run。该指标是可复现近似，不是完整 MDP 求解。

## UNKNOWN / ASSUMPTION

- 装置可自由布置，没有唯一“真实命中率”。模拟使用各尺寸在采样轨迹中最佳观察网格的经验暴露率，并对重复装置使用显式递减系数。
- 改变摩擦、弹性、穿透、磁力、定向和复制的药物尚无独立物理变体采样；其路线收益不作为确定结论。
- 人类选择不是数学最优。三种策略使用带阈值的启发式购买与用药。
- N/A: the current runtime has no SpecialBall catalog or persistent special-ball inventory.
