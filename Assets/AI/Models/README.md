# AI 模型

此目录用于保留经过独立评估后选定的正式模型。训练中的检查点位于项目 `.utmp/game-ai/runs/`。

当前实现由 Python 加载 `.pt` 模型，通过本机协议控制 Unity。把 `.pt` 文件放进 Assets 不会自动启用 Unity 内嵌推理；运行方式见 `docs/AI/使用说明.md`。

正式模型尚未选定；`.utmp/game-ai/runs/validation-search/latest.pt` 是经过真实游戏短训练的流程验证模型，不代表已具备可靠策略强度。
