# MarbleRoguelikeGame 接手文档

本目录归档 2026-10-02 版本的玩法、数值和模拟结果。现行双货币与每日玩法见 [游戏文档](../Assets/Docs/README.md)。 基础数值直接编辑 [GameBalance.json](../Assets/Resources/GameBalance.json)。

2026-10-02 已整合 Balance V6：当前项目的合并范围、验证结果及 Unity 菜单入口见 [Unity 整合记录](Balance/integration_2026-10-02.md)。

## 建议阅读顺序

1. [项目结构分析](项目结构分析.md)：先理解运行链路、目录职责、数据所有权和已知缺口。
2. [数值策划上手指引](数值策划上手指引.md)：再了解参数入口、实际公式、当前基线和安全调参流程。
3. [Balance 总览](Balance/balance_overview.md)：查看统一数值、正式阶段表和 Source of Truth。
4. [Baseline](Balance/baseline_report.md)、[方案对比](Balance/balance_proposal.md)与[变更记录](Balance/balance_changelog.md)：查看实测概率、120,000 局模拟和正式调整依据。
5. [历史 Rush 专项](数值平衡测试报告.md)与 `Assets/Docs/`：仅用于追溯玩法决策和旧验证记录。

## 分析基线

- Git 提交：`d09d74a29ce82b95f6663330fe0695bf79c9fe0e`（`feat: update pan`）
- Unity：`2022.3.62f3c1`
- Entities：`1.0.11`
- URP：`14.0.12`
- 唯一启用的构建场景：`Assets/Scenes/TestScene.unity`

## 历史调优基线

该版本发布时，工作簿与运行时 JSON 已同步。该版本每回合 100 血，正式阶段目标为 `120/380/815/1410/2200/3145`，商店刷新基础价为 `2`。阶段目标采用 Recommended V5：首回合降至 120，并以该点为锚将后续曲线斜率降低约 10%；商品价格沿用 Recommended V3（约下调 15%）。

该版本的数值报告、物理采样结果和配置快照可在此查阅。

