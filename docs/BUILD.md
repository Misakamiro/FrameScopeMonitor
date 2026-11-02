# 从源码构建离线客户端

公开树固定 `EDITION=offline`。改动版型文件不会补齐未包含的完整版能力。

## 环境

Windows 10 / 11 x64、PowerShell、.NET Framework 4.x C# 编译器、Node.js / npm、Rust MSVC、Visual Studio C++ Build Tools 与 Windows SDK。使用独立源码目录，至少预留 30 GiB；保留全部锁文件。

## 恢复依赖与构建

在仓库根目录运行：

```powershell
./tools/Restore-BuildDependencies.ps1
npm ci --prefix src/frontend
npm run build --prefix src/frontend
npm ci --prefix desktop-preview
$env:CARGO_BUILD_JOBS = '1'
./tools/Package-Installer.ps1 -StageOnly
```

恢复脚本从已公开的 2.1.2 源码 Release 附件提取原始 WebView2 与 PawnIO 安装依赖，验证整个附件及每个文件的 SHA256，只写构建目录，不运行安装器。Git 保留锁定的小型 PresentMon；其他大型安装依赖和成品不入库。

`-StageOnly` 构建 C# 工具、硬件服务、前端和生产 Tauri 宿主，将后端暂存到 `desktop-preview/src-tauri/resources/`，不安装或发布。宿主产物位于 `desktop-preview/src-tauri/target/release/`。

制作安装包前阅读 `desktop-preview/installer/build-installer.ps1`，恢复该目录 npm 依赖，使用自己的隔离构建与签名材料，不替换官方附件。

## 验证

```powershell
npm run typecheck --prefix desktop-preview
npm test --prefix desktop-preview
npm run lint:delivery --prefix desktop-preview
python -m unittest discover -s tests -p test_source_projection.py
$env:CARGO_BUILD_JOBS = '1'
cargo test --manifest-path desktop-preview/src-tauri/Cargo.toml --locked --lib -- --test-threads=1
./tests/Build-FrameScopeTests.ps1 -OutputDirectory ./obj/local-tests -TestNames FrameScopeReportManifestTests.exe
./obj/local-tests/FrameScopeReportManifestTests.exe
git diff --check
```

Rust 测试前需要已暂存后端资源。夹具只使用临时数据，真实硬件、原生窗口与游戏需单独验收。

## 浏览器预览

```powershell
npm run dev --prefix desktop-preview
```

只绑定回环地址，使用模拟数据，不启动真实游戏或采集链。当前主界面在 `desktop-preview/`，不是 `src/frontend/`。
