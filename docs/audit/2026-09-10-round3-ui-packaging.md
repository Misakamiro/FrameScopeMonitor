# FrameScope 全项目审计 · Round 3（UI 品牌化 / 设置链 / 托盘 / 打包 / OSD 分核）

> 日期：2026-09-10　范围：378ec3c..本报告提交（批次 1-7 + 审计收尾修复）
> 方法：逐批次变更面复审 + 跨语言契约核对 + 静默安装/卸载实测 + 全量测试套件

## 一、变更面与结论

| 批次 | 提交 | 结论 |
|---|---|---|
| 下拉控件统一 | 378ec3c | 通过（见 R3-1 残留说明） |
| 设置页修复 + 扫描间隔生效 | f53a968 | 通过 |
| 启动屏重做 + 图标品牌 | 9ef0691 | 通过 |
| 托盘与关闭行为 | 883e71f | 通过（见 R3-2 边界说明） |
| OSD 分核频率/电压 | 0006428 | 通过 |
| NSIS 安装包 | f28a3a2 | 通过（静默装/卸实测） |

## 二、逐项核查

### 1. 命令白名单与三处同步（铁律）
- `config_read`：lib.rs COMMANDS ✓ + core.ts 浏览器侧不发（backendOnlyCommands ✓）+ source-boundaries 测试 14/14 ✓。
- `closeWindowBehavior` 进 config_save 映射，取值白名单 ask|minimize-to-tray|exit，未知键忽略 ✓，与 C# NormalizeChoice 允许集一致（新增 "ask"）✓。

### 2. 跨语言 JSON 契约
- config_save 全部整数写 `json!(value.round() as i64)`（浮点炸 JavaScriptSerializer 的 M6 坑未复发）✓。
- config_read 从文件读取带 clamp + fallback，文件损坏回默认（fail-closed，与 C# Load 语义一致）✓。
- OverlayData 新增 `cpu_core_clocks/cpu_core_voltages: Vec<Option<f64>>`：服务 Serialize（snake_case）→ 宿主 emit 透传 → overlay_panel `OverlayHardwareData`（`deny_unknown_fields`）补字段 ✓，`empty_overlay_value` 补字段 ✓，`sanitize_for_gdi` 数组原样透传（GDI 不消费）✓。缺失任一环都会让 1Hz poller 解码炸——已全链核对。

### 3. 设置页数据流
- 桌面初值经 config_read 真实回读（重试闩锁模仿 targets：仅成功读取解除防覆盖守卫）✓。
- config.update 改为 await 落盘后才提示成功，失败抛真实错误到 ActionError ✓。
- draft 脏标记修复了"每秒状态克隆重置编辑中表单"的根因（浏览器模式 tick 每秒 mutate）✓。
- captureDurationSeconds/outputDirectory 桌面隐藏（无后端对应物，诚实展示）✓。
- PollIntervalMs：C# Normalize 从强制回写 1000 改为 ≤0→1000、钳位 250-10000、区间内保留；watcher 循环睡眠用配置值；策略测试同步改写 ✓。

### 4. 托盘 / 关闭行为
- 单一事实源 = framescope-config.json CloseWindowBehavior；前端 persistCloseBehavior 同步写 localStorage+store+config 并广播事件；桌面 title-bar 与设置页从 config 读取 ✓。
- CloseRequested：minimize-to-tray 仅 hide 主窗（**不调 stop_windows**——GDI 悬浮框与硬件链继续运行，这正是托盘化语义）；ask prevent_close + emit main-close-requested 交前端弹窗；exit/未知值走退出。宿主侧 fail-closed 为 exit（配置损坏时用户必须仍能退出）✓。
- 托盘左键恢复（unminimize+show+focus）、菜单改名、真实图标 ✓。

### 5. 打包与安装布局（实测）
- 静默安装实测（/S /D=临时目录）：FrameScope.exe + 6 C# exe + 15 运行库 DLL 平铺于安装根、tools\PresentMon、Fonts\MiSansVF.ttf，共 23 文件 ✓。
- backend_paths()/backend_root() 均在 exe 同目录命中（平铺布局零路径风险，与 dev 布局同构）✓。
- 静默卸载：0 残留 ✓。
- exe 时间戳核验纳入 Package-Installer（静默链接失败陷阱）✓。
- GPL 合规：LICENSE / LICENSE.EXTRA 随安装包落入安装根（审计后补）+ 安装器许可说明文本 ✓。
- identifier 保持 com.framescope.ui-preview 不变——应用数据目录（悬浮框设置/拖动位置）不受改名影响 ✓。

### 6. 品牌残留清查（可达 UI）
- 已换：exe/托盘/标题栏/favicon/启动屏/安装界面位图。
- 保留（合规要求）：AboutSettings 的 NexBox 来源署名（GPL-3.0 衍生必须保留署名与许可）。
- 不可达：NexBox 广告体系、桌面歌词水印、EQ 导出名等不可达页面残留未动（无用户可见影响，动上游文件收益为负）。

## 三、发现并当场修复

| 编号 | 级别 | 问题 | 处置 |
|---|---|---|---|
| R3-F1 | 低 | GDI 样式下开启分核项会为未知 id 预留空白宽度（渲染层 `_ => {}` 跳过但宽度累加） | OverlayPanelPage 在非竖排样式隐藏分核开关（防误配置） |
| R3-F2 | 低 | 安装包未附带 LICENSE 文本（GPL 分发合规） | Package-Installer 暂存 LICENSE/LICENSE.EXTRA 进安装根 |
| R3-F3 | 低 | mainBinaryName 未配时安装后的宿主 exe 仍是 crate 名 | 已配 mainBinaryName=FrameScope，实测安装根为 FrameScope.exe |

## 四、已知边界（记录，不修）

1. **ask 行为依赖 webview 存活**：webview 假死时 Alt+F4 会挂起关闭请求；托盘菜单"退出 FrameScope"与 taskkill 仍可退出。作为热键兜底可接受。
2. **配置文件在安装目录**（Program Files）：应用本身 requireAdministrator 运行可写；多用户共享同一配置属工具定位的既有取舍。运行数据（DataRoot 默认 %LOCALAPPDATA%\FrameScopeMonitorData）不受影响。
3. **分核心电压可用性依赖平台**：Intel 经 LHM 的 "Core #N" VID 可得；部分 AMD 平台无分核电压传感器 → 整组显示 "--"（fail-closed 语义，非 bug）。
4. **CustomSelect 键盘可达性**沿用上游设计（无键盘导航），与既有设置页下拉一致，不在本轮范围。
5. **分核频率显示粒度**：1Hz 快照值，与悬浮框同源；报告内的分核数据仍走采集链 CpuCoreTelemetry（更细），两处口径不同属设计。

## 五、测试基线（本报告时点）

- cargo：服务 19/19、宿主 5/5
- vitest 14/14（source-boundaries 含 config_read 命令表）
- C# 28/28（新增 PollInterval 钳位用例，改写 3 处旧断言）
- 静默安装/卸载实测通过（见 §5）
