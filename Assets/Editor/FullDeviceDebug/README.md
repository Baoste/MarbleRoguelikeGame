# 全装置调试入口

在编辑模式打开 `Assets/Scenes/TestScene.unity`，勾选 `Marbles ECS → Debug → Full Device Startup` 后进入 Play。菜单对应的 `EnableRandomStartingDevices` 记录在当前 Unity Editor 会话，退出 Play 后可再次点击关闭。

`FullDeviceStartup.json` 设置起始日期、盘面、残料金额、可用装置 ID 和晨间准备。空的 `availableDeviceIds` 使用当前内容目录的全部装置。默认选 Day0 和 `Day0.json`，跳过晨间准备并发放 99,999 血肉残料。

入口从指定日期继承目标、血珠升值、生产功能、供给规则、日结和重试。装置通过正式的类型安装流程扣费，可重复放置。成套装置按整套扣费，之后按相同类型继续放置剩余成员。失败后按 Day 检查点重试，达标后进入单日结算。

在 Unity Project 面板删除 `Assets/Editor/FullDeviceDebug` 文件夹，即可移除菜单、配置和编辑器启动逻辑。运行时脚本和场景通过正式每日入口启动。
