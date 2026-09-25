# 装置 prefab

`Scenes/TestScene.unity` 的 `DevicePlacementUI.DevicePrefabs` 已绑定以下全部 15 种装置。
文件名前缀是 `Resources/GameContent.json` 中的装置定义 ID，不是运行时实例 ID。

| ID | prefab | 装置 / 外观 |
|---|---|---|
| 100 | `100_Bank.prefab` | 银行：金库、投币口与金币 |
| 101 | `101_Amplifier.prefab` | 增幅塔：线圈塔与发射端 |
| 102 | `102_Centrifuge.prefab` | 离心机：三叶转子与输出箭头 |
| 103 | `103_Portal.prefab` | 传送门：紫色拱环与出口箭头 |
| 104 | `104_BlackClover.prefab` | 黑色四叶草：四叶轮廓 |
| 105 | `105_Splitter.prefab` | 分流器：Y 形双通道 |
| 106 | `106_Paddle.prefab` | 弹板：铰链与蓝色板面 |
| 107 | `107_ValueLens.prefab` | 增值透镜：青色镜片与金属镜框 |
| 108 | `108_Capital.prefab` | 资本主义：金色立柱与金币堆 |
| 109 | `109_BloodCannon.prefab` | 血穿炮：X 形炮管与红色储罐 |
| 110 | `110_SlowBelt.prefab` | 减速带：橙色刹车条纹 |
| 111 | `111_Revive.prefab` | 复活装置：粉色底座与白色十字 |
| 112 | `112_Magnet.prefab` | 磁磁磁：红蓝双极 U 形磁铁 |
| 113 | `113_MultiDing.prefab` | 倍分钉：金色钉体与双环 |
| 114 | `114_RushDing.prefab` | Rush 钉：紫色钉体与闪电标记 |

这些是可直接编辑的几何体模型，材质在 `Materials/`，使用项目现有的 URP Lit shader。
根节点坐标和旋转归零、缩放为 1；局部 XZ 为盘面，+Y 为法线，输出朝向为 -Z。
所有视觉网格都位于子节点。替换美术时保留根节点及其行为组件和碰撞体。

- 旧 `Prefabs/Ding.prefab` 连同 meta 迁为 `113_MultiDing.prefab`，保留原 GUID 和根对象 fileID。Rush 钉使用独立的 `114_RushDing.prefab`。全部装置定义 ID 连续使用 100～114。
- 两种旧装置使用实体 `CapsuleCollider`、原物理材质和 `MarblePin`，在放置时设置各自实例编号及倍率 / RUSH 概率。
- 13 种新装置使用 `SphereCollider` 触发器与 `MarbleDevice`，默认尺寸、类型和磁场参数对应内容配置。增幅塔还包含 `AmplifierLinkView`。
- `InstanceId`、`PairId` 是运行时数据，prefab 中保持 0，由放置系统注入。两扇传送门共用同一个 prefab，由配对 ID 区分。
- 预览会配置装置数据并关闭碰撞与玩法组件；正式放置后才参与物理和装置系统。按 R 可旋转 45°。
- 当前放置系统会同步新装置根球形触发器的半径；修改美术比例或旧装置胶囊尺寸时，仍须与内容配置的占地半径保持一致。
