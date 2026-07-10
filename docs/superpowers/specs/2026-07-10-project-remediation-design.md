# FrameScope Monitor 全项目治理设计

**日期：** 2026-07-10
**工作分支：** `codex/project-remediation`
**本地目标版本：** `1.2.1`
**发布边界：** 只修改、构建和验证本地项目；不 push GitHub，不更新远端 v1.2 Release。

## 1. 背景与目标

FrameScope Monitor 当前的主链路是 React/Vite 前端、WinForms WebView2 宿主、C# Bridge、原生 watcher/monitor-session、多进程采样器和原生 HTML 报告生成器。现有测试基线是绿色的，但代码审阅和依赖审计确认仍有未被现有测试覆盖的问题：PresentMon 会话清理可能跨监测任务互相影响，报告恢复和产物完整性判断不严谨，子采样器失败可能被掩盖，诊断字段与当前产物结构漂移，版本与默认配置存在多套口径，前端构建依赖含 high/critical 漏洞，项目文档仍描述已经删除的旧 WinForms UI。

本设计的目标是修复所有在当前本地代码、测试、构建、依赖、文档和打包链路中能够复现或由明确代码证据确认的问题，并为每项修复建立可重复的验证证据。完成后，项目应能从干净工作区重建，所有自动化与模拟测试通过，本地安装包来自同一份已验证 payload，且代码、UI、诊断、文档和版本口径一致。

## 2. 非目标与边界

- 不推送任何 commit、tag 或分支到 GitHub。
- 不修改远端 v1.2 Release 或替换远端资产。
- 不安装、删除或重建 GameLite WMI trigger。
- 不启动真实游戏，也不把真实游戏未验收伪装成已验收；游戏链路使用 FakePresentMon、PUBG 模拟器和合成 run 验证。
- 不删除原工作区中的未跟踪诊断、测试证据、`liv-*` 或 `smoke-temp` 目录。
- 不为了重构而重构。只有能消除已确认风险、统一契约或提高可验证性的结构调整才进入本次治理。

## 3. 设计原则

1. **先测试后修复。** 每个行为问题先建立能失败的回归测试，再做最小且完整的生产修复。
2. **状态不能撒谎。** `done`、`full`、`hasFrameData`、采样器健康和报告完整性必须由真实产物证明。
3. **单一事实来源。** 版本、默认配置、报告字段和 Bridge 契约不得由多个手写副本独立维护。
4. **会话隔离。** 每个 monitor-session 只能停止和清理自己创建的 PresentMon/采样器资源。
5. **原始数据完整。** 报告优化只作用于渲染与抽样，不删除诊断原始数据，不用 Vcore 冒充 VID，不为空帧生成假 FPS。
6. **失败可恢复。** 中断、超时、半写文件和子进程异常必须留下明确状态，并允许 watcher 在重启后恢复。
7. **本地产物可追溯。** 所有安装包必须从同一构建 payload 生成，并能通过逐文件哈希证明一致。

## 4. 目标架构与数据流

```text
React UI
  -> WebView2 Bridge contract
  -> C# FrameScopeWebBridge
  -> watcher (one process)
  -> monitor-session (one per active target)
       -> PresentMon session owned by this monitor-session
       -> ProcessSampler child
       -> SystemSampler child
  -> run artifacts written safely
  -> ReportGenerator with bounded execution
  -> complete artifact set: data.js + HTML + manifest
  -> status/history/diagnostics consume one normalized schema
```

### 4.1 会话资源所有权

每个 monitor-session 生成唯一 session name，并只终止该名称对应的 PresentMon ETW session。启动前的遗留清理只处理能够证明属于已死亡 FrameScope owner 的 session；正常启动与正常退出不得调用“终止所有 FrameScope session”的全局逻辑。

Watcher 可以同时跟踪多个目标，但同一个规范化目标别名集合只能有一个 active monitor。多别名目标的存活条件由整个别名集合决定，而不是只绑定首次选中的单个 PID。

### 4.2 子采样器健康模型

monitor-session 为 ProcessSampler、SystemSampler 和 PresentMon 分别记录：

- 是否成功启动；
- PID、启动时间和退出时间；
- 退出码；
- 是否提前退出；
- CSV 是否存在、字节数和有效数据行数；
- stderr 尾部或启动错误。

最终状态按数据完整度分类：

- `full`：有效帧数据、进程数据和系统数据均满足最低完整性要求；
- `partial`：存在有效帧数据，但一个或多个辅助采样器失败或无有效样本；
- `diagnostic`：无有效帧数据，仅保留诊断/辅助采样；
- `error`：监测或报告生成失败且无法形成可用诊断产物。

UI、历史和诊断模块必须展示该分类，不得把 `partial` 当作 `full`。

### 4.3 报告产物事务

报告生成器先在临时目录生成 data.js、HTML 和 manifest，全部写入并验证后再替换正式 `charts` 产物。Watcher 只有在三个文件都存在、manifest 可解析、manifest 路径与 run 一致时才认定报告完整。

报告生成进程具有明确总超时；stdout/stderr 从启动后异步排空，避免管道阻塞。超时后终止进程树、写入可重试进度和 status，并允许下一次 watcher 启动恢复。

恢复扫描覆盖 `capturing`、`finalizing`、`done`、`error` 和缺失 phase 的 run。只要报告产物不完整且存在可用监测 CSV，就进入恢复队列。

### 4.4 JSON 与契约安全写入

状态类 JSON 使用“同目录临时文件 -> flush/close -> 原子替换”策略。读取端保留旧文件兼容，但写入端不再直接覆盖正式文件。

以下字段由一个规范化契约定义并被 watcher、报告生成器、Bridge 和诊断共同使用：

- 帧数与 `hasFrameData`；
- 报告种类与报告路径；
- PresentMon 捕获状态；
- ProcessSampler/SystemSampler 健康状态；
- CPU Voltage/Vcore 与 CPU VID 可用性；
- 报告生成退出码、错误、开始/结束时间。

诊断模块必须读取当前字段，并对旧 run 的旧字段做显式兼容映射。

## 5. 分阶段实施

本文件是全项目治理的总设计。由于各阶段涉及相对独立的子系统，实施时分别生成监测可靠性、报告/诊断、版本/依赖、文档/打包和最终验证计划；每个计划都必须交付可独立运行、可独立回退的本地软件状态，而不是等到最后才首次集成。

### 阶段 A：监测生命周期与并发隔离

- 为并发 monitor-session 建立 PresentMon session 隔离测试。
- 将全局清理改为 owner/session 定向清理。
- 让多别名目标以整个别名集合判断存活。
- 完整记录三个子进程的健康状态和有效样本数。
- 增加 `partial` 报告语义并贯穿 status、history、Bridge 和 UI。

### 阶段 B：报告恢复、完整性和超时

- 补充 `done` 但报告缺失的恢复测试。
- 补充仅 HTML 存在、manifest 损坏、data.js 缺失等半成品测试。
- 使用临时目录完成报告事务写入。
- 为报告子进程增加异步输出读取和总超时。
- 将恢复扫描和报告完整性判断抽取为可测试的纯逻辑。

### 阶段 C：诊断、状态和保留策略

- 修正 `monitor-error.txt`、帧数、报告路径和 manifest 字段读取。
- 为旧 run 与当前 run 建立诊断兼容 fixtures。
- 统一 JSON 安全写入助手。
- 对 run 目录与 history 增加由现有保留设置驱动的安全清理；当前 active run、最近报告和用户数据根目录外路径绝不删除。

### 阶段 D：版本、配置和依赖

- 新增单一产品版本源 `1.2.1`，生成程序集、安装器、诊断和 React UI 使用的版本。
- 由同一默认配置模型生成首次运行配置与安装器默认配置，example 文件作为生成结果校验。
- 升级 Vite/Vitest/Babel 相关依赖，要求 `npm audit` 的 high 和 critical 均为 0。
- 固定 WebView2/LibreHardwareMonitor 构建依赖版本，构建时输出并验证依赖清单。
- 保留现有 PowerShell 构建入口，避免在同一治理窗口进行无必要的全量项目系统迁移。

### 阶段 E：真实文档和本地打包

- 更新 `AGENTS.md`、项目总览和模块文档，使其只引用当前存在的 React/WebView2/C# 文件。
- 删除旧实时 WinForms 页面、`src/ui`、DataGridView 和每目标独立采样率等错误描述。
- 记录当前全局采样间隔语义、run 状态判定和 GameLite 独立边界。
- 生成 Setup、Full Setup、Installer.zip 和 LegacyCleanup，并验证四者来自同一构建代次。

### 阶段 F：端到端验证

- 前端：clean install、typecheck、Vitest、production build、npm audit。
- 原生：完整 build、测试重编译、所有测试执行。
- 报告：chart sampling、manifest、布局、进程交互和大数据探针。
- 监测：FakePresentMon、PUBG 模拟、并发 session、辅助采样器失败和中断恢复。
- UI：WebView2 bridge、页面切换、目标增删改、设置持久化、报告操作、托盘和 reduced-motion smoke。
- 打包：payload 提取、逐文件哈希、安装器资源、ZIP 内容和卸载安全边界。
- 清理：无 FrameScope/PresentMon/FakePresentMon 残留进程，`git diff --check` 通过。

## 6. 测试策略

每个生产修复采用 red-green-refactor：

1. 写入能精确描述旧错误的失败测试；
2. 运行目标测试并确认失败原因与预期一致；
3. 实现最小完整修复；
4. 运行目标测试确认通过；
5. 运行相关模块测试；
6. 每个阶段结束运行全套测试和静态检查。

不得通过删除断言、放宽错误语义、增加任意 sleep 或把错误状态改名来制造绿灯。并发与超时测试使用可控 fake 进程和事件同步，不依赖真实游戏或不稳定等待。

## 7. 完成标准

只有同时满足以下条件，才可以声明本地项目治理完成：

- 所有已确认问题都有生产修复和回归测试；
- 全新工作区可完成前端依赖恢复、前端构建、原生构建和测试重编译；
- 所有原生、前端、图表、模拟、WebView2 和报告探针通过；
- npm audit 的 high/critical 为 0；
- status/summary/manifest/diagnostics 对同一 run 给出一致结论；
- 并发 monitor-session 不会互相终止 PresentMon；
- 半成品报告可恢复，报告生成超时不会阻塞 watcher；
- 子采样器失败会产生 `partial` 或 `error`，不会产生虚假的 `full`；
- UI、诊断、安装器和程序集版本统一为 `1.2.1`；
- 默认配置在运行时、安装器和 example 中一致；
- 本地四种发布资产来自同一 payload，哈希验证无差异；
- 文档不存在指向已删除生产文件的维护入口；
- 原工作区未跟踪资料未被修改或删除；
- 没有进行任何 GitHub push、tag 或 Release 更新。

## 8. 回退与交付

所有工作在 `codex/project-remediation` 本地分支的隔离 worktree 中进行，并按可独立验证的阶段提交。原始主工作区保持在用户现有状态。若某阶段出现不可接受回归，可以回退该阶段的本地 commit，而不影响已验证阶段或用户原有证据。

最终交付包含：修复文件清单、每个问题的根因与修复、全部测试命令和结果、本地安装包及 SHA256、未执行的真实游戏/远端发布事项，以及原工作区保持不变的证明。
