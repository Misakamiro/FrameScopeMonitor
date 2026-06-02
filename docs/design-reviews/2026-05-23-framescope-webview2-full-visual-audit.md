# FrameScope Monitor WebView2 全应用视觉审查

日期：2026-05-23

范围：只做 UI 视觉审查、真实浏览器截图、问题清单和修复建议。不改源码、不打包、不提交。

源码根目录：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d`

证据目录：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit`

## 当前总体评分

| 维度 | 评分 | 结论 |
| --- | ---: | --- |
| 视觉质量 | 6.2 / 10 | 页面不是不能用，但还像“组件拼装完成”，不是产品级桌面工具。主要问题集中在紧凑布局、状态层级、列表密度、控件比例和重复边框。 |
| 易用性 | 5.8 / 10 | 主流程能走通，但很多关键状态在折叠区或页面下方，空状态按钮看起来能点但实际禁用，900 宽下列表信息丢失上下文。 |
| 一致性 | 5.5 / 10 | sidebar、按钮、pill、卡片、菜单、表格响应式规则各自成立，但缺少统一设计系统约束。 |
| 动效体感 | 7.4 / 10 | 路由切换和 reduced motion 表现稳定，未发现旧页残留、主体低透明或横向溢出。剩余问题主要是菜单和状态反馈的细节。 |

是否建议进入实现修复：是。

这不是单个 bug，而是全应用 UI 细节债。建议进入一个受控的 UI-only 修复窗口，先修设计系统和响应式列表，再修具体页面。

## 审查覆盖

| 项目 | 已检查证据 |
| --- | --- |
| Overview / 当前监控 | `1280x720-01-overview-default.png`, `900x760-01-overview-compact.png`, `1280x720-08-overview-monitor-starting.png`, `1280x720-09-overview-monitor-running.png`, `1280x720-39-overview-no-targets-empty-reports-fixture.png` |
| Targets / 监控目标 | `1280x720-02-targets-default.png`, `900x760-02-targets-compact.png`, `1280x720-16-targets-inline-edit.png`, `1280x720-17-targets-process-search-input.png`, `1280x720-18-targets-process-search-loading.png`, `1280x720-19-targets-process-one-result.png`, `1280x720-20-targets-process-empty-result.png`, `1280x720-22-targets-saving.png`, `1280x720-23-targets-saved.png`, `1280x720-35-targets-save-failed.png`, `1280x720-42-targets-process-250-results-fixture.png`, `900x760-08-targets-process-250-results-fixture.png` |
| Reports / 报告 | `1280x720-03-reports-default.png`, `900x760-03-reports-compact.png`, `1280x720-25-reports-more-menu-open.png`, `1280x720-26-reports-selected-missing-report.png`, `1280x720-27-reports-regenerate-in-progress.png`, `1280x720-28-reports-regenerate-complete.png`, `1280x720-41-reports-empty-list-fixture.png`, `900x760-06-reports-menu-open-compact.png` |
| Settings / 应用设置 | `1280x720-04-settings-default.png`, `900x760-04-settings-compact.png`, `1280x720-29-settings-clean.png`, `1280x720-30-settings-dirty-long-path.png`, `1280x720-31-settings-saving.png`, `1280x720-32-settings-saved.png`, `1280x720-37-settings-save-failed.png`, `900x760-07-settings-long-path-after-save-compact.png` |
| About / 关于与帮助 | `1280x720-05-about-default.png`, `900x760-05-about-compact.png`, `1280x720-33-about-normal-user-info.png`, `1280x720-34-about-advanced-expanded.png` |
| Sidebar 状态 | `1280x720-06-sidebar-hover-reports-while-overview-active.png`, `1280x720-07-sidebar-focus-active-overview.png`, `900x760-01-overview-compact.png` |
| 150% 高 DPI 等效 | `1280x720-dsf1p5-01-overview-high-dpi-equivalent.png`, `1280x720-dsf1p5-02-reports-high-dpi-equivalent.png` |
| Reduced motion | `1280x720-reduced-motion-01-overview.png`, `1280x720-reduced-motion-02-reports-after-route.png`, `reduced-motion-observations.json` |

说明：文件名包含 `fixture` 的截图使用浏览器内临时 WebView2 响应夹具，只用于覆盖当前 mock 预览无法自然达到的状态，比如无目标、空报告和 250 个进程结果。夹具没有写入源码。

## P0 问题清单

本轮未发现 P0。

没有白屏、主流程不可读、菜单完全跑出视口、路由旧页残留、主体低透明等阻塞级视觉问题。

## P1 问题清单

### P1-01 compact sidebar 丢失导航语义

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\900x760-01-overview-compact.png`

发生位置：Sidebar，`900x760`，compact active 状态。

用户会看到什么：左侧导航变成纯图标。当前页只靠浅蓝方块和左侧蓝线表达，底部状态卡也只剩一个图标。

为什么不美观或不好用：用户必须记住每个图标含义。active rail、选中背景、图标按钮外框同时出现，显得像调试态，不像成熟桌面产品。

应该怎么修：保留 compact 宽度，但给 hover/focus 增加标签浮层或短文本提示；active 状态只保留一种主视觉，例如 rail 或选中底色二选一；底部状态卡在 compact 下显示状态点或可悬停说明。

### P1-02 Sidebar active / hover / focus-visible 状态叠加过重

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-06-sidebar-hover-reports-while-overview-active.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-07-sidebar-focus-active-overview.png`

发生位置：Sidebar，`1280x720`，hover、focus-visible、active 与 focus 同时存在。

用户会看到什么：选中项有背景、边框、左 rail、焦点圈；hover 项也有接近 active 的浅背景。

为什么不美观或不好用：active 应该告诉用户“我在哪”，focus 应该告诉用户“键盘将操作谁”，hover 应该只是轻提示。现在三种状态互相抢视觉权重。

应该怎么修：建立 sidebar 状态 token：idle、hover、active、focus-visible、active-focus。active 用稳定底色或 rail，focus 只用外轮廓，hover 只改变背景透明度，不额外加边框。

### P1-03 Targets 在 900 宽下从表格退化成无标签信息块

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\900x760-02-targets-compact.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\900x760-08-targets-process-250-results-fixture.png`

发生位置：Targets，`900x760`，目标列表和 250 结果状态。

用户会看到什么：表头隐藏后，每行变成两列信息块。启用状态 pill 变成一条很宽的横条，进程名、采样、报告和操作散在不同位置。

为什么不美观或不好用：它看起来像桌面表格被硬压进窄屏，而不是为 compact 模式设计的列表。用户无法快速判断每个值对应什么字段。

应该怎么修：做专门的 compact target row：第一行显示游戏名和小状态 pill；第二行显示进程名；第三行显示采样和报告方式 label/value；操作按钮放到行尾或底部。不要让状态 pill 横向铺满。

### P1-04 Targets 进程查找区域位置太低，且大结果集不可控

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-19-targets-process-one-result.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-42-targets-process-250-results-fixture.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\900x760-08-targets-process-250-results-fixture.png`

发生位置：Targets，进程查找，1 个结果、空结果、250 个结果。

用户会看到什么：查找进程在目标列表和同步面板下面。250 结果时，页面变成很长的纵向滚动，用户会卡在页面中段，看不到查找上下文。

为什么不美观或不好用：进程查找是一个明确任务，但现在像辅助说明。大结果集没有局部滚动、截断、虚拟列表或“显示前 N 项”的提示。

应该怎么修：桌面端把进程查找固定成右侧任务面板，或放在当前编辑目标旁边。结果列表设置 max-height 和内部滚动，250 项时显示结果总数、截断说明和可筛选输入，不要让整页无限增长。

### P1-05 Reports 主操作重复，列表和详情抢层级

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-03-reports-default.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-24-reports-list-selected.png`

发生位置：Reports，`1280x720`，报告列表和选中报告。

用户会看到什么：顶部有“打开最新报告”，行内有“打开报告”，详情面板也有“打开报告”。详情面板和列表面板视觉重量几乎一样。

为什么不美观或不好用：主任务不唯一。用户会犹豫应该从页头、列表行还是详情面板执行操作。

应该怎么修：让报告列表成为主任务区。页头主按钮降级为 secondary 或只保留刷新；详情面板改为低权重 inspector，内部只保留一组清晰的操作按钮。

### P1-06 Reports compact 状态丢失字段标签，菜单与行关系不清

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\900x760-03-reports-compact.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\900x760-06-reports-menu-open-compact.png`

发生位置：Reports，`900x760`，选中报告和更多菜单打开。

用户会看到什么：报告行内出现 `240 帧`、`822.4 KB`、`-` 等裸值，但没有字段标签。详情区在折叠下方。菜单浮在列表中间，看起来像另一个卡片。

为什么不美观或不好用：表头隐藏后没有补偿标签，信息上下文丢失。菜单与触发按钮的空间关系不够明确。

应该怎么修：compact 下改成报告卡片：标题、时间、状态、帧数、大小全部带标签。详情区改成展开行、抽屉或折叠 inspector。菜单使用更轻的 contextual menu 样式，并做 viewport-aware 定位。

### P1-07 空状态按钮看起来可操作，但实际不可用

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-40-targets-empty-list-fixture.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-41-reports-empty-list-fixture.png`

发生位置：Targets 空目标，Reports 空列表。

用户会看到什么：空状态里有“添加目标”“先添加目标”这种按钮样式元素，但它们是禁用状态。

为什么不美观或不好用：空状态应该引导下一步。如果按钮不能点，用户会觉得功能坏了或视觉欺骗。

应该怎么修：如果能执行，就把空状态 CTA 接到真实操作或导航；如果不能执行，就不要用按钮样式，改成说明文字加明确的可用入口。

### P1-08 保存失败和查找失败反馈离触发点太远

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-35-targets-save-failed.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-36-targets-process-search-failed.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-37-settings-save-failed.png`

发生位置：Targets 保存失败、Targets 查找失败、Settings 保存失败。

用户会看到什么：失败状态存在，但 Targets 保存失败出现在下方状态面板里，离正在编辑的行和页头保存按钮都较远。

为什么不美观或不好用：错误恢复需要靠近触发点。现在用户需要扫多个面板才能确认动作是否失败。

应该怎么修：保留侧边状态卡，但在页头保存按钮附近增加一行紧凑失败提示；编辑区域下方增加字段/表单级失败提示；失败时保留 dirty badge，并把“重试”放在错误提示旁边。

### P1-09 Settings 长路径处理不像产品级路径控件

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-30-settings-dirty-long-path.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\900x760-07-settings-long-path-after-save-compact.png`

发生位置：Settings，长路径，dirty/saved，`1280x720` 和 `900x760`。

用户会看到什么：输入框聚焦后只看到路径中后段，辅助文本只显示尾部，右侧摘要又再次截断。

为什么不美观或不好用：路径是重要操作信息，不能当普通文本处理。用户需要复制、打开、确认完整路径。

应该怎么修：做路径控件：显示 root + tail，聚焦时允许横向滚动，提供复制按钮和打开文件夹按钮；摘要区使用中间省略或两行路径，不要多处重复截断。

### P1-10 当前预览缺少完整视觉状态夹具

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-39-overview-no-targets-empty-reports-fixture.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-42-targets-process-250-results-fixture.png`

发生位置：Overview 无目标/无报告，Targets 250 进程结果。

用户会看到什么：这些状态可以渲染，但本轮需要临时浏览器夹具才能覆盖，因为当前普通 mock preview 不提供这些视觉状态入口。

为什么不美观或不好用：没有状态夹具，后续自动化 PASS 仍然可能漏掉肉眼明显问题。

应该怎么修：增加 frontend-only visual fixture / query mode，明确标记为测试预览，覆盖 empty、loading、success、failure、dirty、saving、saved、many results、long strings。

## P2 问题清单

### P2-01 字重偏重，桌面工具显得过吵

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-01-overview-default.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-04-settings-default.png`

发生位置：Overview、Settings，桌面默认状态。

用户会看到什么：页面标题、行标题、按钮文字、状态值经常同时加粗。

为什么不美观或不好用：操作型工具需要安静、可扫描；过多粗字会让所有信息都像重点。

应该怎么修：标题保留强字重；正文、label、按钮降一档；中文正文行高保持 1.45 到 1.55；数字和英文进程名使用更稳定的 mixed font 策略。

### P2-02 status pill 尺寸和角色不一致

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-02-targets-default.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\900x760-02-targets-compact.png`

发生位置：Targets，桌面和 compact 状态。

用户会看到什么：同样是状态 pill，有的像小标签，有的在 compact 下变成宽条。

为什么不美观或不好用：状态组件的语义不稳定，会破坏用户对状态颜色和大小的预期。

应该怎么修：定义 topbar chip、row pill、section badge 三种规格。row pill 在任何宽度下都保持紧凑，不横向铺满。

### P2-03 卡片和边框太密，层级被抹平

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-03-reports-default.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-04-settings-default.png`

发生位置：Reports、Settings，桌面默认状态。

用户会看到什么：外层卡片、内层行、输入框、状态提示、详情列表都带边框。

为什么不美观或不好用：所有模块都像同级，主流程不突出。

应该怎么修：页面模块用卡片，卡片内部用轻分隔线或无框 row。减少内层阴影和边框对比，把主任务区留给强边界。

### P2-04 Settings checkbox / input / icon / button 基线不统一

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-29-settings-clean.png`

发生位置：Settings，clean 状态。

用户会看到什么：checkbox 靠右、文件夹图标靠右、输入框高度、单位文本和按钮高度没有统一视觉基线。

为什么不美观或不好用：表单会显得拼凑，用户扫设置时视线跳动。

应该怎么修：定义统一 control height、label baseline、unit slot 和 action slot。可点击图标用 icon button，不可点击图标降为装饰。

### P2-05 About 高级信息入口太弱

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-05-about-default.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-34-about-advanced-expanded.png`

发生位置：About，普通用户信息、高级信息展开。

用户会看到什么：“高级信息”在页面底部像普通文字，不像一个和帮助页一致的可展开模块。

为什么不美观或不好用：高级信息是低频内容，但入口也要清楚；现在它既不明显，也不够像可交互控件。

应该怎么修：改成 accordion card，带 chevron、摘要说明和展开状态。技术信息保留低权重，不要抢普通帮助区。

### P2-06 Reports 更多菜单太像卡片

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-25-reports-more-menu-open.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\900x760-06-reports-menu-open-compact.png`

发生位置：Reports，更多菜单打开。

用户会看到什么：菜单可读，但阴影、宽度和圆角让它更像一个浮动卡片。

为什么不美观或不好用：context menu 应该轻、短、贴近触发器；现在层级偏重。

应该怎么修：降低阴影深度，收紧 item 高度，菜单宽度按内容控制，使用 menu 专用 radius/padding。

### P2-07 Overview 次要卡片重复，主决策区不够突出

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-01-overview-default.png`

发生位置：Overview，未启动、有目标、有最新报告。

用户会看到什么：目标摘要、最新报告、数据保存三个卡片结构很像，和主监控卡之间的层级差不够。

为什么不美观或不好用：当前监控页应该是决策页，用户要马上知道下一步做什么。重复卡片让页面像 dashboard，而不是清晰流程。

应该怎么修：保留主监控卡和下一步卡；次要信息改成更低权重 summary strip 或紧凑信息区。

## 动效体感结论

截图路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-reduced-motion-01-overview.png`

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\1280x720-reduced-motion-02-reports-after-route.png`

数据路径：

`C:\Users\misakamiro\Documents\Codex\2026-05-02\files-mentioned-by-the-user-69eea97ef2a174a3524ae99fc416034d\artifacts\design-reviews\2026-05-23-framescope-webview2-full-visual-audit\reduced-motion-observations.json`

结论：reduced motion 下 route swap 稳定。观测到 `reduced=true`、`pageTransform=none`、`pageOpacity=1`，切到 Reports 后当前页面为 `reports`，未发现旧页残留、主体低透明或横向溢出。

建议：本轮实现修复不应优先改路由动效。优先修布局、层级、compact 列表、状态反馈和控件系统。

## 最高优先级 10 个必须修复项

1. 重做 compact sidebar 的 active / hover / focus-visible 规则，并增加可见标签反馈。
2. Targets compact 行改成真正的 labeled compact card，不再把表格硬压成两列。
3. Targets 进程查找移到更靠近编辑任务的位置，并让输入和结果始终视觉相连。
4. 进程结果增加 max-height、内部滚动或虚拟列表，250 项不能撑长整页。
5. Reports 删除重复主操作，明确列表是主任务，详情是 inspector。
6. Reports compact 行补字段标签，详情改成展开行、抽屉或折叠面板。
7. 空状态 CTA 必须可执行；不可执行时不要画成按钮。
8. 保存失败、查找失败要贴近触发按钮和编辑区域，不只放在下方状态卡。
9. Settings 长路径改成专门路径控件，支持复制、打开、可读截断。
10. 建立状态 pill、按钮、输入框、卡片、菜单、列表行的统一 token 和响应式规格。

## 设计系统层面问题

这些不是单点 bug，而是设计系统问题：

- Sidebar 状态模型：active、hover、focus-visible、active-focus、compact label、底部状态卡需要一套统一规则。
- 响应式列表模型：Targets 和 Reports 的表格在窄宽度下都缺少专门 compact row/card 组件。
- 状态语言：topbar chip、row pill、section badge、inline status、错误提示缺少统一语义和尺寸层级。
- 控件尺寸：button、input、checkbox、icon button、menu item 没有一条稳定 baseline。
- 卡片层级：页面卡片和内部 row/card 边框太多，主流程和次要信息被抹平。
- 长字符串处理：路径、进程名、报告路径缺少通用 ellipsis、copy、tooltip、reveal 模式。
- 视觉状态夹具：缺少可复现的 empty、loading、error、many results、dirty、saving、saved、failed 状态预览。

## 建议给 UI 实现窗口的文件范围

保持 UI-only。不要改 C# bridge 语义、后端监控逻辑、打包脚本、发布文件或 release 流程。

建议文件范围：

- `src/frontend/src/theme/tokens.css`
- `src/frontend/src/styles/global.css`
- `src/frontend/src/layout/layout.css`
- `src/frontend/src/layout/SidebarNav.tsx`
- `src/frontend/src/layout/TopStatusBar.tsx`
- `src/frontend/src/components/Button.tsx`
- `src/frontend/src/components/EmptyState.tsx`
- `src/frontend/src/components/InlineStatus.tsx`
- `src/frontend/src/components/StatusPill.tsx`
- `src/frontend/src/components/components.css`
- `src/frontend/src/pages/pages.css`
- `src/frontend/src/pages/TargetsPage.tsx`
- `src/frontend/src/pages/ReportsPage.tsx`
- `src/frontend/src/pages/SettingsPage.tsx`
- `src/frontend/src/pages/AboutPage.tsx`
- 可选：`src/frontend/src` 下新增 frontend-only 视觉状态 fixture / test 文件，用来覆盖空、错、长文本和大结果集状态。

## 最终结论

是否建议进入实现修复：是。

建议做一轮完整 UI-only 修复，不要拆成零散小补丁。先修设计系统和 compact 响应式规则，再落到 Targets、Reports、Settings、About、Overview。修完以后必须重新按本报告截图矩阵复拍，不要用自动化 PASS 代替肉眼视觉验收。
