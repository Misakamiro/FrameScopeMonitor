# GameLite / lightweight 边界

## 所有权

GameLite 自动轻量化与 FrameScope Monitor 是两个独立产品。FrameScope 负责采集、诊断和报告；GameLite 负责其自身的进程调度策略。两者不得共享运行时状态、安装步骤或隐式依赖。

## FrameScope 必须保持的边界

- `build.ps1` 只构建 FrameScope 程序、前端和安装包。
- FrameScope 安装器不复制、不安装、不修改 GameLite 脚本。
- FrameScope UI、watcher、monitor-session worker、sampler 和 ReportGenerator 不调用 GameLite。
- FrameScope 的 DataRoot 不保存 GameLite snapshot、日志或 WMI 状态。
- 普通 FrameScope 构建和测试不创建、更新或删除 GameLite WMI filter/consumer。

本仓库的边界门禁是 `tests/lightweight-separation-tests.ps1`。它验证生产源码、构建和 packaging 没有重新引入 GameLite 依赖。

## 安全规则

没有用户明确授权时：

- 不安装或移除 GameLite；
- 不查询或修改真实游戏会话；
- 不创建、删除或重建 GameLite WMI 触发器；
- 不结束用户的 GameLite/游戏进程；
- 不把 FrameScope build 成功解释为 GameLite 已部署。

如果任务明确属于独立 GameLite 项目，应切换到该项目自己的仓库、文档和测试，不在 FrameScope tree 内实现兼容脚本。

## 历史说明

历史 implementation report 可能记录早期 wrapper、WMI consumer 或迁移过程。这些记录继续作为当时证据保存，但不表示当前 FrameScope build 拥有或安装相关脚本。
