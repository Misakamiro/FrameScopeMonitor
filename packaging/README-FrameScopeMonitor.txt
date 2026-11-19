FrameScope 安装与使用说明（2026-11-13更新）

## beta.19 使用入口

报告先显示真实摘要与当前图表，其他页签按需加载；检查状态区分排队、检查中、输入变化、损坏和失败。AI位于设置和报告页签，厂商密钥由用户提供，电脑操作须当前会话授权并可停止。硬件价格从FrameScope服务器获取，未确认规格的页面报价不能作为同规格最低价；历史缺失保持空白。首次安装、真实硬件和渠道验证边界按对应发行说明。

<!-- documentation-sync:2026-11-16-beta19-cleanup:start -->
> 当前本机为 **2.1.3-beta.19 full／来源v41**。[主状态](../docs/preview/STATUS-CURRENT.md)、[新窗口交接](../docs/preview/HANDOFF-SESSION-20261116-BETA19-CLEANUP.md)、[交付证据](../artifacts/20261116-beta19/DELIVERY-REPORT.md)、[本次清理](../artifacts/20261116-beta19-docs-cleanup/CLEANUP-REPORT.md)。本机交付与本轮文档／清理完成（删除12.83GiB，压缩另省2.28GiB）；京东／淘宝人工验证、同规格历史与完整目录仍未完成。旧日期、旧容量和旧待办为历史，不重放脚本。NAS已获部署授权；公开发布、发行包上传和Git推送暂停。
<!-- documentation-sync:2026-11-16-beta19-cleanup:end -->

当前交付为 beta.18 full／v39 累计包与首次安装全量包，81 组件复用自 beta.14 完整基础；开发和恢复按 [BUILD](../docs/BUILD.md)。本次清理不改写封存来源与安装身份，不重建已经验收完成的产品。

当前主客户端为desktop-preview的React／Chakra UI／Tauri，正式入口FrameScope.exe。
C#采集与报告工具、Rust硬件服务仍随客户端提供；主界面需要WebView2 Runtime。
本地交付版本为2.1.3-beta.14 full，公开发行版与版型以发行说明为准。

安装：
  首次安装使用FrameScope_<版本>_x64-setup.exe全量包。
  已具备基础组件时使用FrameScope_<版本>_x64-update.exe累计包，缺损／变化组件分别补齐。
  旧客户端先安装全量基础版，再由用户再次检查并安装最新累计版。
  当前本地全量819.22MiB、累计155.26MiB，公开发布暂停。
  当前本机安装目录为C:/Program Files/FrameScope；安装器按选择与权限处理目标目录。
  完整安装器按需处理WebView2及硬件依赖；驱动安装需要用户同意。
  不使用历史FrameScopeMonitor-Setup.exe命名推断本轮包体。

主要程序：
  FrameScope.exe：桌面宿主
  FrameScopeMonitor.exe：采集watcher／会话worker及兼容工具
  FrameScopeHardwareService.exe、FrameScopeHardwareProvider.exe：硬件服务与提供器
  FrameScopeProcessSampler.exe、FrameScopeSystemSampler.exe：进程／系统采样
  FrameScopeReportGenerator.exe：报告生成
  tools/ 与 dependencies/：固定版本工具及依赖，随payload清单校验
  多个同名采集进程可能承担不同角色，不按进程名一律结束。

默认记录：
  %LOCALAPPDATA%/FrameScopeMonitorData/framescope-runs
  配置、原始CSV、报告及历史数据不随升级主动删除。

滤镜限制：
  无畏契约、战地6等阻断正常加载的反作弊游戏目前无法使用滤镜。
  已记录的其他目标只代表各自测试条件，不能据此宣称所有游戏兼容。

基本使用：
  硬件评测和AMD每次进入自动只读检测；有任务先等待，不自动跑分或调校。
  1. 启用目标进程，启动监控，再自行启动游戏。
  2. 按需设置悬浮窗；自定义样式在三栏画布中排版，保存草稿与应用分别操作。
  3. 会话结束后在报告页打开、重新生成或查看目录。
  4. 滤镜及AMD方案广场可浏览、搜索、下载；下载仅保存，需主动应用。
  5. 硬件评测右上始终提供帧数预测入口；无达标游戏时显示暂未开放。
  6. 同一搜索框支持关键词、拼音、首字母及有限错字，无模式切换。

报告：
  full：有效帧且必需辅助采样健康。
  partial：有效帧但至少一个必需辅助采样不健康。
  diagnostic：无有效帧，但进程或系统遥测可用。
  error：没有足够有效数据。
  完整报告必须同时保留framescope-interactive-report.html、
  framescope-interactive-data.js和framescope-interactive-manifest.json。
  显示筛选不修改原始CSV；分享前检查设备、路径、窗口标题和进程信息。

权限与适用范围：
  游戏滤镜目前无法用于无畏契约、战地6等会阻断正常加载的反作弊游戏。
  不保证所有游戏兼容，不提供绕过游戏保护的功能；以各游戏实测范围为准。
  部分采集／传感器需要正常UAC和PawnIO驱动授权；缺失时显示不可用。
  AMD方案载入需严格CPU／拓扑匹配，真实写入仍需软件内开关及二次确认。
  悬浮窗显示受游戏呈现方式、Windows和驱动影响；独占全屏不保证覆盖，
  可尝试无边框模式。不能将自有测试窗口结论写成所有商业游戏已验证。
  预测仅开放数据覆盖与独立精度验证合格的条件，目录自动同步不要求重复升级。

卸载：
  在Windows“已安装的应用”选择FrameScope，或执行安装目录中的
  "FrameScope Installer.exe" --uninstall。
  FrameScopeUninstaller.exe是正常停止辅助程序，不是用户卸载入口。
  是否删除历史数据由卸载流程单独询问；保留需要的用户数据和回滚材料。

诊断：
  先检查对应run的status.json、summary.json和CSV，使用随包tools中的
  diagnose-pubg-capture.ps1生成诊断。反馈前检查并脱敏内容，不直接发送全部用户目录。
  FrameScope不安装或调用独立GameLite脚本，也不操作其WMI触发器。

文档更新时间不代表安装包重建时间。本次说明更新留在源码工作区，
现有v19安装包、签名、payload和本机已安装文件保持既有交付身份。
