# 竖排 WebView2：创建前透明背景候选

日期：2026-10-21，Asia/Hong_Kong。

## 结论边界

已补齐 band 2 合成控制器的创建前背景设置。无窗口测试验证的是创建请求的参数、顺序和错误分支；没有实际启动本轮宿主或测量桌面像素，因此尚不能认定 beta.3 持续白／灰底已经修复。

本轮依据用户“开始定位并修复”“继续”修改源码。保留交接的 WebView2 路线及不开新测试窗口、不更新本机、不打包发布边界；没有子智能体、提交、push 或用户配置改动。

## 定位与改动

`desktop-preview/src-tauri/vendor/wry/src/webview2/mod.rs` 的普通 controller 已在创建前通过 Options3 传背景色；band 2 的 CompositionController 原先直接使用 Environment3 创建，完成后才设透明背景，随后初始化又设置同一颜色。这是确定的初始化顺序差异，尚非持续白底的根因证明。

现有绑定 webview2-com 0.38.2 支持 Environment10 的 `CreateCoreWebView2CompositionControllerWithOptions`。本轮只调整合成创建块：

- 有显式背景色时先创建 options，通过 Options3 设置颜色，再调用 composition-with-options API。沿用原先 alpha 为 0 或 255 的归一化。
- Environment10 或 Options3 查询仅在 E_NOINTERFACE 时回退 Environment3，向 stderr 记录 HRESULT 与回退原因。
- 创建 options、设色、其他查询错误或 controller 创建错误直接返回；不在真实失败后重复创建。
- 保留原创建完成回调、创建后的透明色设置、ParentWindow、DComp target、visual、bounds、输入与释放代码。普通 band 1 路径不变。

微软说明 options 可用于 visual hosting；Options3 用于提前初始化背景，官方描述的直接作用是避免加载时白闪，不能据此推导持续白底必然消失：

- [Environment10](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2environment10)
- [ControllerOptions3](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2controlleroptions3)

## 测试方法与结果

新增 `tests/vertical-controller-background-tests.ps1` 和 `tests/fixtures/vertical-controller-background.rs`。运行器提取当前生产代码的同步创建块，编译执行带 API 边界记录器的 Rust 测试。未替换或复制生产分支逻辑，不依赖浏览器、HWND、COM 初始化或安装目录。

旧实现：10 项中 2 通过、8 失败，核心失败为透明背景创建时只调用 Legacy，而没有 Options、透明色、WithOptions 序列。修正后：10 通过、0 失败。覆盖透明色、非零 alpha、无显式背景、两类缺接口回退及五类失败传播。

宿主 release 无窗口回归：97 通过、0 失败、12 忽略；未执行忽略项。新创建块与 rustfmt 输出一致，周围旧格式未重排。`git diff --check` 通过。

真实 Windows Rust release 构建验证了 COM 绑定类型；生产宿主另由 Tauri CLI `build --no-bundle --features uiaccess-acceptance,tauri/custom-protocol` 构建，使用 `--locked --offline`。本项目未声明同名根 feature，因此显式启用依赖的 tauri/custom-protocol。直接 Cargo build 和未显式启用该 feature 的 CLI 中间程序只作为类型／编译验证，不能当成包含发行页面的最终宿主。使用相同两个 feature 的宿主无窗口回归也为 97 通过、0 失败、12 忽略。

## 产物与来源

当前源码前端经 TypeScript/Vite 构建到 `C:\fs-build\b203\controller-background-frontend`，78 个文件与 b203 的 beta.3 前端快照逐文件哈希相同。主仓库旧 dist 有 5 个路径差异，因此候选用显式 frontendDist 覆盖嵌入新组件输出；未覆盖旧源码快照、旧 dist 或安装包。

候选配置曾错误使用 Windows 绝对路径：Tauri 的 FrontendDist 是先尝试解析 URL 的 untagged enum，该字符串进入 Url 分支，代码生成跳过嵌入资源。已改为相对原 src-tauri 的 `../../../../../../fs-build/b203/controller-background-frontend` 并先核实解析后的绝对目录。最终构建需核对 EXE 中的页面资源键及编译依赖，不能只看编译退出码或“Built application”输出。这个候选构建问题不是安装版白底根因；产品默认配置未改。

候选存放 `C:\fs-build\b203\controller-background-candidate\FrameScope.exe`。最终大小、SHA256、构建配置与状态以 `artifacts/20261021-composition-background-options/candidate-result.json` 为准。原目标目录的宿主已保存为该证据目录的 `before-framescope-ui-preview.exe`。

最终 CLI 构建通过。候选大小 57,464,320 bytes，SHA256 `39457CE918362047B6BACC22F53D0B9540CB8DB87ECF2FF779C301743D760504`。EXE 中已核到 `index-BsirfnrM.js`、`index-BYishf7u.css` 资源键及本轮创建选项诊断字符串；编译依赖文件引用了全部 78 个独立前端输出资源。候选与本轮构建输出复制后哈希相同。

候选未签名为 UIAccess 发行宿主，未安装、未启动、未打包、未发布。安装版仍为原 beta.3，SHA256 应为 `4F43C8DFACC10D57F013DFF6829FE26D6D32B99E532B2320588D6BC4C530A4C7`。

日志目录包含 red.log、green-final.log、frontend-build.log、tauri-release-embedded-final.log、tauri-host-tests.log、rust-inputs-final.json、frontend-hashes.json、format-check.log 和 storage-usage.json。编译原有 unused/dead-code 等警告及前端分包警告未在本轮扩大处理。

## 未验证项与回退

未验证最终 GPU/DComp 像素、透明度 0／中间／最大值、文字、拖动穿透、关闭重开、重启定位或游戏全屏。测试替身不模拟 WebView2 内部的颜色缓存、实际表面或异步创建完成结果。若持续底色仍在，应继续沿 DWM 调用结果与合成表面传播调查，不叠加未经证实的绘制方案变更。

安装版及用户数据未变，无需安装回滚。源码回退只撤销本轮 mod.rs 创建块、两份新增测试及本轮文档；不要整文件恢复或清理其他工作。现有 b202/b203 两份构建存储继续保留，本轮未删除历史缓存。
