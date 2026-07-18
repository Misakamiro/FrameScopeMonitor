# GameLite 边界

## 所有权

GameLite 自动轻量化与 FrameScope Monitor 是两个独立项目。FrameScope 负责性能采集、诊断和报告；GameLite 负责自己的进程调度策略。两者不共享运行时状态、安装步骤或隐式依赖。

GameLite 核心实现位于独立项目。FrameScope 仓库不再包含 GameLite 执行入口、兼容包装器或独立项目路径。

## FrameScope 必须保持的边界

- `build.ps1` 只构建 FrameScope 程序、前端和安装包。
- FrameScope 安装器不复制、安装或修改 GameLite 文件。
- FrameScope UI、watcher、monitor-session、sampler 和 ReportGenerator 不调用 GameLite。
- FrameScope DataRoot 不保存 GameLite snapshot、日志或 WMI 状态。
- FrameScope 构建和测试不创建、更新或删除 GameLite WMI filter/consumer。

## 分离测试

`tests/lightweight-separation-tests.ps1` 只做静态边界检查：

- FrameScope 旧 GameLite 入口不存在。
- `scripts/lightweight` 不包含旧核心脚本。
- build、测试编译脚本、生产 C# 和 packaging 源码不包含 GameLite 自动触发实现。
- 测试不要求独立 GameLite 项目存在，也不执行独立项目脚本。

## 安全规则

没有用户明确授权时，FrameScope 工作流不得：

- 安装或移除 GameLite；
- 查询或修改真实游戏会话；
- 创建、删除或重建 GameLite WMI 触发器；
- 结束用户的 GameLite 或游戏进程；
- 把 FrameScope 构建结果解释为 GameLite 已部署。

GameLite 相关任务应在独立 GameLite 项目中完成。FrameScope 仓库只维护自身代码、文档和测试边界。

## 历史说明

旧 implementation report 和计划可能记录早期包装器、WMI consumer 或迁移过程。这些文字是历史证据，不代表当前 FrameScope 构建包含或安装相关脚本。
