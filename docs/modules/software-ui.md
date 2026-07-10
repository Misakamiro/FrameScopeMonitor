# 软件 UI 模块

## 当前界面技术

当前生产 UI 是 React + Vite，运行在 C# WebView2 host 中。视觉页面和组件位于 `src/frontend/`；C# 负责窗口生命周期、系统能力和安全的 host adapter，不负责维护另一套原生页面。

## 前端文件

- `src/frontend/src/App.tsx`：应用页面组合。
- `src/frontend/src/pages/OverviewPage.tsx`：总览与 watcher 状态。
- `src/frontend/src/pages/ReportsPage.tsx`：报告列表和报告操作。
- `src/frontend/src/pages/SettingsPage.tsx`：全局设置。
- `src/frontend/src/pages/TargetsPage.tsx`：目标启用与编辑。
- `src/frontend/src/pages/AboutPage.tsx`：版本与环境信息。
- `src/frontend/src/components/`：共享按钮、卡片、状态和图表壳。
- `src/frontend/src/styles/global.css`：全局样式。
- `src/frontend/src/theme/tokens.css`：颜色、间距和视觉 token。
- `src/frontend/src/types.ts`：前端状态类型。

页面数据应来自 bridge contract 或前端派生状态，不直接读取本地文件系统。

## WebView2 host

- `src/app/FrameScopeNativeMonitor.WebHost.cs`：加载前端、处理窗口/托盘生命周期并实现 host adapter。
- `src/app/FrameScopeWebView2Runtime.cs`：检查 WebView2 Runtime。
- `src/app/FrameScopeWebBridge.Contracts.cs`：host context、host result 与 adapter interface。

前端构建输出是 generated build output；源码修改应落在 `src/frontend/`，不要直接编辑生成资源。

## 设计约束

- 页面必须支持浅色、深色与 system theme。
- 动画遵守 reduced-motion。
- 大列表使用 windowing，避免一次渲染全部 DOM。
- report 状态必须区分 full、partial、diagnostic 和 error。
- 没有完整报告时，“打开报告”必须禁用，但“打开目录”或“重试”可按 run 状态保留。
- 只展示 host 已验证的 reportId，不让前端传入任意文件路径。

## 修改入口

- 页面结构或文案：对应 `src/frontend/src/pages/` 文件。
- 共享视觉：`src/frontend/src/components/` 与 `src/frontend/src/theme/tokens.css`。
- bridge client：`src/frontend/src/bridge/webviewBridge.ts`。
- C# request/event contract：`src/app/FrameScopeWebBridge.Contracts.cs` 与 bridge partial 文件。
- host 系统行为：`src/app/FrameScopeNativeMonitor.WebHost.cs`。

修改 contract 时必须同时检查 TypeScript 类型、C# payload 和自动化测试，避免前端 mock 与 host 实际字段分叉。
