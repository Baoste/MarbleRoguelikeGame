# 验证记录

验证日期：2026-09-11。实际使用本项目安装的 Unity **2022.3.55f1c1**、Entities **1.0.16**、URP **14.0.11**；不是仅靠 C# 语法扫描判断完成。

| 检查 | 结果 | 证据 |
|---|---|---|
| Unity EditMode | **45 / 45 通过** | `Assets/Verification~/EditMode.xml` |
| Unity PlayMode | **9 / 9 通过** | `Assets/Verification~/PlayMode.xml` |
| Excel 导入独立验证 | **13 / 13 通过** | `python Tools/Balance/verify_importer.py` |
| Windows x64 Player 构建 | **成功** | `Assets/Verification~/LauncherBuild.log`；构建后已复制至项目根目录 `Builds/BloodMarble/BloodMarble.exe` |
| 打包程序自动启动/发射/退出 | **退出码 0，无运行异常或 PhysX 退出警告** | `Assets/Verification~/LauncherPlayer.log` |
| 打包程序盘面渲染 | **已检查** | `Assets/Verification~/Game.png`；离屏相机截图，只含盘面，不含 IMGUI |

EditMode 覆盖原有发射事务、回收、结算顺序和临时效果回归，以及统一开奖、待定分不解锁提现、等于目标通过、越级、跨回合资源继承、商店交易失败不扣款、布局碰撞校验、药物叠加与独立到期、Rush 剩余时间上限。

发射器移动迁移至 ECS 后新增 4 项规则测试，覆盖相对初始位置的边界限制、滑条请求延迟且只消费一次、自动往返、重开清理状态与输入、Build 阶段不移动和非法输入。新增 1 项场景测试验证 ECS 位置同步到发射口 Transform、暂停不移动和重开复位。本轮使用临时项目副本运行，避免占用已打开的原项目；副本与原项目的 Scripts、Tests、Editor 中所有 C# 和程序集定义逐文件比对一致，日志为 `LauncherEditMode.log`、`LauncherPlayMode.log`。

PlayMode 使用真实 Entities 存储和本地 PhysX 场景，验证实体钉实际碰撞、共享材质隔离、对象池弹性重置、出生重叠不扣血、150 m/s 血珠穿过薄触发区仍只计分一次。整局冒烟测试创建实际盘面、布置钉子、使用药物、自动横移连发，直到血量耗尽且场内球排空，验证可以进入最终回合结算并重开。退出回归测试在场内仍有球时先销毁 Entities World，再禁用和销毁控制器，确认清理不再访问失效 World 或重复释放。

Excel 验证直接编译实际规则及导入代码，检查默认配置、工作簿和 JSON 一致，检查深拷贝、共享/内联字符串、阶段目标独立编辑，以及 NaN、未知字段、重复 ID、非法整数、公式和非法区域 ID 均不覆盖旧 JSON。它替换了 Unity JSON/资源刷新适配器，因此另由本次实际 Unity 导入与编译验证编辑器接入。

Windows 自动预览在真实 Direct3D 11 Player 中布置两种钉子、使用药物并发射，检查了盘面、钉子、血珠和终点颜色。退出前先完成本地物理场景卸载，消除了 Entities 全局销毁顺序引起的异常及 PhysX 残留引用警告。隐藏窗口的离屏截图不包含 IMGUI，因此此截图不作为 HUD 字体和全部按钮交互的人工验收证据。

## 重现

在 Unity **Window → General → Test Runner** 分别运行 EditMode 和 PlayMode。命令行也可以使用：

```powershell
$editor = 'C:/Program Files/Unity/Hub/Editor/2022.3.55f1c1/Editor/Unity.exe'
& $editor -batchmode -nographics -projectPath D:/UnityProjects/MarbleTest -runTests -testPlatform EditMode -testResults D:/UnityProjects/MarbleTest/Assets/Verification~/EditMode.xml -logFile D:/UnityProjects/MarbleTest/Assets/Verification~/EditMode.log
```

将 `EditMode` 改为 `PlayMode` 运行物理测试，两种测试应顺序执行。`Verification~` 尾部的 `~` 让 Unity 忽略验证输出。Windows 构建输出在项目根目录的 `Builds/BloodMarble`，因为 Unity 禁止把 Player 构建到 Assets 内。

首次验证修复了当前 NUnit 版本不支持 `Is.AnyOf` 的测试断言；最后一次测试结果均通过。Unity 的原有 Entities 包报告 `Unity.Properties.Internals.asmref` 无目标程序集提示，本次编译和测试未因此失败。

这些检查验证功能闭环和关键边界，尚不代表长期平衡性、任意用户修改后的盘面、高负载性能或跨平台物理确定性已经验证。
