# 首版实现约定

以用户更新后的 `GameplayFlow.md` 第 12 节明确答复为优先，工作簿作为架构和数值顺序参考。以下决定已经落实到代码，替代概念稿中仍标注“待定”的相关内容。

## 回合与资源

流程为 `Build → Playing → Draining（Settling）→ Shop → Build`，目标未达成为 `Lost`，通过最后阶段为 `Won`。初次开局先布置赠送的装置。进入 Playing 补满 BloodCapacity；发射档位影响血量成本、尺寸、质量，基础得分独立配置。调大球的收益目前来自物理路径变化，不自动绑定更多分数。

只有 `TotalScore + 本回合即时分 >= 当前目标` 且仍有血量时允许提现。待定分不是确定收入，不能提前兑现提现资格。提现一次性扣完剩余血量并换算金币，之后禁止新发射，已有球继续模拟。剩余血量不足最低档单珠成本也自动进入排空；此时不足发射的尾数不兑换金币。

所有赌博终点共用一个待定分池，排空后只抽一次。此处采用玩法文档的最新规则，取代工作簿中“随机区每球抽奖励表”的建议。总分在结算时只累计一次；等于目标通过。通过多个目标后跳到第一个尚未通过的阶段，发放所跨阶段基础奖励及额外越级奖励。

库存、金币、累计分和布局跨回合保存，重新开局清零并恢复初始配置。暂不实现存档到磁盘。

## 效果和物理

每步先收集全部接触，再应用非终点钉子效果，然后按球身份、终点优先级降序、终点 ID 升序结算。合法得分先于超时/越界兜底。终点和对象池始终使用包含回合和单调递增序号的血珠身份，旧回调不能命中新球。

金钉每球每钉一次 ×2；蓝钉每球每钉一次增加 Rush 概率。不同钉可叠加，永久得分总倍率最高 1000。持续接触不会逐帧产生收益，这也是对工作簿通用触发频率系统的首版收敛。

Rush 只让入区分数 ×2，当前球先结算再触发；同一步后续球可以获得新 Rush。赌博待定分在入区时已经包含 Rush，开奖时不再次乘 Rush。重复触发叠加剩余时间并封顶。旧独立模拟保留刷新时长的默认行为，新战役配置启用叠加时长。

“碰撞系数”定义为 PhysX 材质的恢复系数 Bounciness。药效仅改变药效期间发射的血珠：`e = clamp(BaseRestitution × 所有有效药物倍率, 0, 1)`。剂量独立到期；已经发射的血珠保留自己的 e，不受玩家后续服药或药效结束影响。每个池化球持有独立材质，重用时重设，避免修改共享资产。

盘面采用 Unity 局部 XZ 平面、+Y 为法线，与工作簿的引擎无关坐标约定存在差异；血珠位姿回写使用 PhysX 世界空间，安装位置使用盘面局部坐标并由视图转换。盘面在游玩期间固定。球保持三维自由度，不锁轴、不用第二套积分器。

固体使用 CCD。每个 ECS tick 内按血珠速度和半径细分 PhysX 子步（最多 32），逐子步做带半径的 SphereCast 与端点 OverlapSphere，合并原生触发回调。此实现针对本版静态终点；它不承诺任意极端速度、多次亚子步反弹或高速移动触发体的完整轨迹检测，也不承诺跨平台 PhysX 确定性。

参考：[Unity 2022.3 PhysicsScene API](https://docs.unity3d.com/cn/2022.3/ScriptReference/PhysicsScene.html)、[Physic Material](https://docs.unity.cn/Manual/class-PhysicMaterial.html)。

## 布置与商店

只实现竖直安装、圆形截面的两种钉子。提交位置须在盘面允许安装范围内，避开固定钉和其他已安装装置，并保持配置中的间隙。对本版同高圆柱，圆形截面相交判定就是实际的安装占地判定；圆柱绕法线旋转不会改变占地，因此没有多余旋转操作。后续加入非圆形/悬空装置时应扩展完整三维体积校验。

买下的钉子先进入库存，布置免费。每个商店槽位只能购买一次，刷新消耗金币并重抽全部槽位；相同商品可以出现于多个槽位。出售按购买价格乘出售比例向下取整；库存上限、阶段限制、余额或布置验证失败不消耗资源。首版不实现升级或合成。

## ECS 与扩展接口

`MarbleSimulation(GameBalance)` 创建配置副本和独立 Entities World；`StartSession` 重开整局，`BeginRound` 仅从 Build 开局。`Session`、`Snapshot`、`GetOwnedDevices`、`GetShopOffers`、`GetDrugInventory` 都返回脱离 ECS 存储的快照。

金币、阶段、库存、待定分、布局、单球效果和药效都由组件或 DynamicBuffer 保存。MonoBehaviour 用公开命令入口请求变化，物理回调只上报事实。商店与放置系统在物理步外提交；球的发射创建成功后才扣血，实体和后端对象失败时一起回滚。

发射器移动同样由 ECS 负责：`LauncherMovementConfig` 保存初始局部位置、速度和移动范围；`LauncherMovementInput` 保存 A / D 方向、自动模式和滑条位置请求；`LauncherMovementState` 保存当前位置和往返进度。`LauncherMovementSystem` 处理位移、边界、自动往返及回合复位。控制器每个固定步采集输入并调用移动系统，然后将 ECS 位置同步到 `LaunchPoint`，再执行 `BeforePhysics` 发射，保证新血珠使用本步更新后的发射位置。滑条提交一次性请求，HUD 从 ECS 读取位置；暂停不执行移动系统。初始化时从 Excel 的 Board 配置复制移动参数。

本版保留旧 `MarbleSimulation(MarbleTuning)`、`ApplyScoreDrug` 等回归兼容接口；正式游戏只出售和使用弹性药物。工作簿中的通用 EffectEmitter、球分裂、病理、拾取物、其他发射头、多盘面、通用三维安装形状和完整命令回放不在本版内容范围内。

## 退出与资源释放

Entities 的全局退出清理也会销毁手动创建的 World，因此控制器通过 `IsReady`、模拟通过 `IsAlive` 检查 World 是否仍有效；重复清理不会再次访问已释放的实体。控制器先撤下对象引用，再分别释放桥接器、模拟和自有材质。

Standalone 程序收到退出请求后，先完成本地物理场景的异步卸载，再继续退出；编辑器不注册此退出拦截。程序内退出使用 `MarbleGameController.QuitGame(exitCode)`，保留自动验证的失败退出码。此处使用 Unity 的 [Application.wantsToQuit](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-wantsToQuit.html) 与 [UnloadSceneAsync](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SceneManagement.SceneManager.UnloadSceneAsync.html)；实际 Windows 退出日志已验证无残留引用警告。
