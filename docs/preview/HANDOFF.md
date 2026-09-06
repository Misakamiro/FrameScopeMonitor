# FrameScope UI 重建 · 交接文档

> 交接日期：2026-09-07　|　交接人：ZCode（接替 Codex）　|　状态：**前端预览已由用户验收通过**

---

## 一、项目背景与目标

FrameScope Monitor 是面向 Windows 游戏玩家的本地性能监控工具（FPS、CPU/GPU、温度、内存、磁盘、后台进程 → 交互式 HTML 报告）。

此前将 NexBox 界面整合进 FrameScope 的尝试失败（自制外壳、UI 卡顿、大量空实现），决定**全部重做**：

1. **本轮（已完成）**：直接照搬 NexBox 原版前端，做成带模拟数据的独立桌面预览，供用户确认界面与交互。
2. **下一轮（未开始）**：用户验收前端后，规划后端迁移——把 NexBox 的硬件采样、游戏采样能力移植过来，让 FrameScope 实现硬件监控。**以已验收的前端为约束，让后端适配它，不再反过来改界面。**

基线与原则（用户拍板，不可回退）：

- FrameScope 基线：`2c48031`（GitHub `Misakamiro/FrameScopeMonitor` main）
- NexBox 基线：`c800dc5`（`MuLiuSaMa/NexBox` 9.5.3），原版参考冻结在 `reference/nexbox`，**只读不改**
- 直接复用 NexBox 源码，不自制外壳；每处差异必须记录（见 `docs/preview/IMPLEMENTATION.md` 的 Recorded deviations）
- 移除音乐/商店/优化等非目标功能；硬件与游戏监控页面保留
- 独立 HTML 图表报告不改版；旧整合工作树（`C:\Users\misakamiro\.config\superpowers\worktrees\FrameScopeMonitor\project-remediation`）与已安装版保留不动

---

## 二、工作区与运行方式

- 仓库：`C:\Users\misakamiro\Documents\FrameScopeMonitor-Rebuild`，分支 `codex/nexbox-frontend-preview`
- 预览工程：`desktop-preview/`（NexBox 前端整体复制 + `src/preview/` 模拟层 + `src-tauri/` 宿主）
- 产物：`desktop-preview/src-tauri/target/release/framescope-ui-preview.exe`（独立运行，不写正式配置，应用标识 `com.framescope.ui-preview`）

```bash
# Git Bash 下先补 PATH（机器无全局 Node，winget 装的 v24）
export PATH="/c/Program Files/nodejs:$PATH"
cd "C:/Users/misakamiro/Documents/FrameScopeMonitor-Rebuild/desktop-preview"

npx tsc --noEmit          # 类型检查
npx vitest run --exclude "src-framescope-baseline/**"   # 11 项测试
npm run build             # 前端构建 → dist/
npx tauri build --no-bundle   # 宿主构建（内嵌 dist，改前端后必须重跑）
```

**顺序陷阱**：`tauri build` 把 `dist/` 编译进 exe。改前端后必须先 `npm run build` 再 `tauri build`，否则 exe 里还是旧界面（cargo 缓存会让它 1 秒“成功”）。

- 纯浏览器调试：`npx vite preview --port 1440`（预览层有浏览器模式回退）
- WebView2 调试：临时在 `tauri.conf.json`（主窗）或各 `WebviewWindowBuilder` 加 `additional_browser_args("--remote-debugging-port=9333")`，然后 `http://127.0.0.1:9333/json/list` + CDP。已写过的诊断脚本已删，思路记录在 IMPLEMENTATION.md

---

## 三、本次会话完成的内容

### 修复的三个致命/关键问题

1. **CSP 导致整个界面不渲染**（只显示背景图）。`tauri.conf.json` 里自定义 CSP 会让 Tauri 追加 nonce，按规范 `'unsafe-inline'` 随之失效，Emotion/Chakra 全部动态样式被拦截。修复：`csp: null`，与 NexBox 上游一致。
2. **命令处理器内建窗死锁主线程**。WebView2 控制器初始化需要消息泵，而命令正阻塞在泵里：窗口半初始化（白屏卡 about:blank / 无标题不可见）、invoke 永不返回。修复：所有建窗流程（传感器窗、报告样例窗、竖排窗、悬浮窗启停/切换/热键）经 `std::thread::spawn` 到工作线程，命令立即返回。
3. **液态玻璃默认开启**。用户喜欢该效果（NexBox 官方特性，非旧项目残留）；`background-context.tsx` 默认 `true` + 首次启动持久化，边界测试锁定。

### 功能补齐与重设计

- 主页重设计（用户验收）：主区 = 详细硬件配置卡片网格（复用 `LiquidGlassCard` + 硬件页行样式），右侧粘性次要栏 =「当前监控」+「最新报告」两卡，**顶部与处理器/显卡卡片行对齐**（缩进一个标题行高度 48px），高度锁定可视区。
- 补齐交互式报告真实样例（`public/samples/charts/` 三件套，取自旧工作树 2026-09-02 真实生成物），`open_report_sample` 可用。
- 可读性修复：亮壁纸 + 暗色主题下按钮/输入框看不清——统一采用 NexBox「半透明胶囊」方案（与全局搜索同思路）：硬件页顶栏按钮（`floatBtnStyle`）、悬浮框页顶栏按钮、主页「查看报告」、目标/报告页搜索框（`useFloatingFieldStyle`），共享 hook 在 `src/hooks/use-floating-button-style.ts` 与 `src/preview/pages/shared.tsx`。
- 硬件页 GPU 统计卡标题溢出：标题行改占满卡片宽度，跑马灯可正确触发。
- 背景交叉淡化层加 `translateZ(0)` 缓解竖向接缝（见“未决事项”）。

### 实机验证通过（release 构建 + 桌面截图逐项核对）

五个主页面（首页/硬件/目标/报告/悬浮框/设置）；四个独立窗口：传感器监控窗（14 个模拟传感器、分组、搜索、筛选）、竖排悬浮窗（透明置顶、彩色数值、缺 FPS 显示 `--` 不造假）、原生 GDI+ 悬浮条（CPU/GPU/RAM/PING）、报告样例窗（真实交互式报告）。模拟数据场景含正常/失败/空数据等，未知命令会报错（符合“禁止统一返回成功”约束）。

---

## 四、架构铁律（新会话必读，违反即翻车）

1. **`csp` 必须保持 `null`**。任何 style-src 限制都会经 Tauri nonce 化杀死 Emotion，UI 退化成无样式静态流。
2. **不能在 Tauri 命令处理器内直接 `WebviewWindowBuilder::build()`**。一律 `std::thread::spawn` 到工作线程。
3. **改前端必须 `npm run build` + `tauri build` 两连**，只跑后者会内嵌旧 dist。
4. **sticky 元素的祖先链上不能有 `overflow-x: hidden`**（主页根容器踩过：会静默吞掉粘性定位）。
5. **Git Bash 会把 `/x` 形式的命令行参数和环境变量值改写成 Windows 路径**。给 Node 脚本传路由等斜杠开头的值时，传不带前缀的值在脚本内补 `/`，或用 CDP 直接 evaluate。
6. `liquidGlassEnabled` 默认 `true` 是用户明确决定，勿改回；`reference/nexbox` 只读。

---

## 五、未决事项（按优先级）

1. **代码未提交**。全部工作（`desktop-preview/`、`docs/preview/`、`.gitignore`）在 `codex/nexbox-frontend-preview` 分支上未 commit。用户已验收，**接手后第一件事应是提交**（建议拆分：上游复制、模拟层、宿主、主页重设计、可读性修复，或按用户意愿单提交）。
2. **断层观察**：背景接缝做了合成层缓解，属间歇性问题，用户使用中若复发，下一步换掉背景轮播的交叉淡化实现（双图层改单图层淡入）。
3. **逐文件来源清单 TODO**：计划要求“每文件差异注明业务替换/功能裁剪/适配”，目前只有 `IMPLEMENTATION.md` 的逐条偏差记录 + 边界测试，未做全量清单。
4. **竖排悬浮窗默认尺寸偏小**（146×170，按 item_width 自适应），可在悬浮框设置里调，若用户要求可改默认值。
5. 多 GPU 机器上主页首行是“处理器+显卡1”，第二块 GPU 卡会落到下方网格（可接受，必要时把首行固定为处理器+主显卡）。
6. 深度验收项未做：Windows 100/125/150% 缩放布局、长时间运行的内存/句柄观察。

---

## 六、下一阶段路线（后端迁移，待用户发起）

按既定计划，前端验收后单独规划：

1. NexBox 硬件采样链迁移（Rust service → LibreHardwareMonitor/PawnIO，或复用 FrameScope 既有 C# HardwareProvider，需方案对比后由用户拍板）。
2. FrameScope 既有采集报告链接管（watcher → worker → PresentMon/ProcessSampler/SystemSampler → 报告生成 → HTML+data.js+manifest 契约）。
3. 最终宿主架构决策：保留 Tauri 宿主，还是回到 C# WinForms+WebView2（届时以已验收前端为约束）。
4. 模拟层（`src/preview/runtime/`）逐步替换为真实命令；场景开关移除。

---

## 七、关键文件地图

| 路径 | 职责 |
|---|---|
| `desktop-preview/src/preview/runtime/` | 模拟核心：命令拦截（core.ts）、状态机（state.ts）、fixtures、浏览器回退 |
| `desktop-preview/src/preview/pages/` | FrameScope 业务页（目标/报告）+ 共享组件（shared.tsx 含 `useFloatingFieldStyle`） |
| `desktop-preview/src/pages/HomePage.tsx` | 主页（硬件配置主区 + 粘性次要栏） |
| `desktop-preview/src/pages/HardwarePage.tsx` | 硬件页（NexBox 原版 + 胶囊按钮 + 跑马灯修复） |
| `desktop-preview/src-tauri/src/lib.rs` | 宿主命令分发（工作线程建窗模式） |
| `desktop-preview/src-tauri/src/overlay_panel.rs` | 原生 GDI+ 悬浮窗（1638 行） |
| `desktop-preview/tests/source-boundaries.test.ts` | 上游边界守护 + 已批准差异锁定 |
| `docs/preview/IMPLEMENTATION.md` | 执行清单、逐条偏差记录、三会话修复日志 |
| `reference/nexbox/` | NexBox 9.5.3 冻结参考（只读） |

---

## 八、给下一个会话的操作建议

1. 先 `git -C "C:/Users/misakamiro/Documents/FrameScopeMonitor-Rebuild" status` 核对未提交变更，与用户确认提交方式后提交。
2. 任何界面改动走「npm run build → tauri build → 实机截图验证」闭环，不要只看构建通过。
3. 动上游文件（`src/pages`、`src/components`）前先 diff `reference/nexbox`，差异一律记入 IMPLEMENTATION.md。
4. 与用户沟通默认用中文；涉及取舍（视觉、范围）先问再做。
