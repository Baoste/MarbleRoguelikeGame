# Combo Risk Report

| 级别 | 组合 | 数学原因 |
| --- | --- | --- |
| Watch | 无限增值 × 多个可触发对象 | 复制链复用 MaxSplitGeneration=2 的独立复制代数上限；单颗原球最多扩展到 4 球，不再无界递归。 |
| Watch | 增值透镜 × 回流路线 | 每次重新进入 ×1.85; BALL_BASE_VALUE 从 1 到 10000.0 约需 15 次，仍需回流盘压测。 |
| Watch | 资本主义 × 增值透镜 | 单次串联倍率为 ×4.995；资本主义每装置每球最多触发 2 次，仍需关注多实例回流。 |
| Watch | Multi Ding × Multi Ding | 机器乘区 ×1.85^n，但 MACHINE_MULT 上限 100.0；约 8 次有效触发触顶。 |
| Watch | 增幅塔 × 回流路线 | 每次过线 ×1.2 且无总次数上限；达到 10,000 约需 51 次。 |
| Watch | 黑色四叶草 × 肾上腺素 | 下一次有效 Rush 概率 65.0% + 23.0% = 88.0%；同家族只检定一次，不再确定点火。 |
| Safe | 分流器 × 分流器 | 每代总价值 2×0.5=1 倍且 MaxSplitGeneration=2，不会指数增值。 |
| Safe | 复活 × 复制 | RefunedFamilies 按 Family 去重，整族只返一次原始血耗，不能用克隆重复返血。 |
| Safe | 赌博四档 | 当前基础 EV=1.40；四档概率合计为1且单局只开奖一次。 |

完整 Device×Device、Medicine×Device、Medicine×Medicine 扫描见 `combo_matrix.csv`。没有证据的组合标为 `UNKNOWN`，不会伪装为 Safe；Device×SpecialBall 与 SpecialBall×SpecialBall 因运行时无目录标为 N/A。

所有分数、球数和金币都有硬上限，因此没有字面意义上的无限数值；`Broken` 指在正常 Run 尺度内会迅速撞上上限或性能容量。
