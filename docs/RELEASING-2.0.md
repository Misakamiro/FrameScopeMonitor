# FrameScope 2.x 更新和发布

首个目标版本 `2.0.0-beta.1`；验收后以独立指令发布 `2.0.0`。VERSION 是产品版本源，运行 `tools/Sync-FrameScopeVersion.ps1` 同步生成配置。提交后开发构建显示版本与 Git SHA；测试包递增 beta 序号，正式修复递增补丁号。每项验证通过的修改独立提交并推送 codex/2.0。不要覆盖公开标签或附件；代码回退用 revert，再以更高版本发行。

## 明确区分四种授权

- 提交／推送源码：开发分支，仅审查过的源码、测试和必要文档。
- 打包：本机隔离构建并签名，产物仅保存在本地。
- 发布：创建 GitHub Release 草稿，核对远端附件，再发布。
- 更新本机：单独运行安装，保留当前数据。源码提交与打包都不包含此动作。

旧 1.x 用户首次需要从 GitHub 下载 2.0 安装器手动安装一次。之后才能使用新更新器。初次迁移会在安装目录外的 `.framescope-transaction-*` 中保存 `offline-1x-*.zip`，安装目录的 `FrameScopeLegacyBackup.json` 指向它。1.x 恢复是手动解压程序文件的兜底；2.x 的一键上一版功能从首次 2.x 覆盖更新开始建立。

## 客户端

关于／更新展示原生端返回的完整版本、渠道、Git SHA、更新说明和进度。首次正式版不含测试版，首次测试版包含测试版；之后尊重保存的选择。关闭测试开关不降级；测试用户必须明确选择查找并返回已有正式版。启动只检查和提醒，点击才下载，安装再次点击。没有空闲检查定时器和下载动画。

固定仓库为 `Misakamiro/FrameScopeMonitor`。前端只传发行 ID；原生端构造 GitHub API 和附件路径。清单签名通过后才信任包名、大小、SHA256。下载完成和执行前再次核对；Windows 文件锁覆盖最后校验至启动，避免文件在两步之间被替换。代理默认 `https://gh-proxy.org/`，是第三方公开下载服务，不发送认证令牌；网络、内容或校验异常回退原站，原站也失败则停止。

检查／下载不影响采集。安装／恢复要求正常停止监控并等待报告结束（包括等待游戏的自动 watcher），防止更新交接瞬间启动新的采集。安装器接管正常退出，无法收尾就保留现场，不强杀游戏或采集进程。需要提升时通过正常 UAC；取消不关闭旧程序。

## 安装恢复

更新和恢复都使用现有持久文件事务。更新前验证完整新 payload；成功后保留一份旧程序备份。新版原生初始化及前端启动完成后返回 nonce、版本、SHA、运行路径和 PID，安装器核对实际进程路径及主程序哈希后才提交。未确认时回退；无法正常结束新进程时保留日志和备份供恢复，不能冒险覆盖运行中的程序。

恢复上一版只替换程序，保留最新目标、配置、报告、悬浮窗位置、快捷方式和启动选项；恢复成功后不自动推荐刚回退的版本。主程序无法启动时，运行安装目录里的 `Restore-FrameScope.cmd`。它调用同目录安装器的明确恢复模式；如果发现未完成安装事务，先恢复该事务。上次结果在 `FrameScopeUpdateResult.json`。恢复材料损坏时拒绝执行，不删除现场。

## 密钥

客户端仅包含 `src/updater/release.pub`。Minisign 加密私钥保存在 `%LOCALAPPDATA%\FrameScope-ReleaseSigning`，目录仅当前账户和 SYSTEM 可访问；随机口令另外以当前 Windows 账户 DPAPI 加密。私钥和口令不进入 Git、客户端、Release 或日志。本机加密副本不能替代脱离此 Windows 账户的离线备份。

需要独立离线备份时运行：

```powershell
pwsh -File tools/Backup-FrameScopeSigningKey.ps1 -Destination E:\FrameScope-Signing-Backup
```

该命令为副本设置独立口令，不修改正在使用的密钥。将介质与口令分开保存。恢复到新签名机器时用备份口令重新保护本机密钥；客户端公钥保持一致。

## 本机命令（PowerShell 7）

```powershell
# 只检查干净提交、版本同步和签名工具，不构建
pwsh -File tools/Release-FrameScope.ps1 -Action Check

# 仅在收到“打包”指令后运行，新目录不得复用
pwsh -File tools/Release-FrameScope.ps1 -Action Build -OutputDirectory C:\fs-release\2.0.0-beta.1

# 仅在收到“发布”指令后创建草稿；使用签名清单中的 SHA 建立标签
pwsh -File tools/Release-FrameScope.ps1 -Action Draft -OutputDirectory C:\fs-release\2.0.0-beta.1

# 下载远端三份附件、核对签名／哈希／实际标签提交后公开
pwsh -File tools/Release-FrameScope.ps1 -Action Publish -OutputDirectory C:\fs-release\2.0.0-beta.1
```

Build 不上传或安装；Draft 不公开；Publish 不更新本机。发行附件为版本化安装器、`release-manifest.json`、`release-manifest.json.minisig`。变更说明和本地证据单独生成。构建发生在隔离快照，不用工作区历史产物凑包；完整 payload、版本、SHA、宿主能力标记逐项核对。

## 验证边界

源码实现的回归检查：前端 Vitest／类型检查／构建、宿主 Rust tests、`src/updater` 本地 HTTP 签名／回退／取消测试、安装器 TEMP 文件事务及故障注入、版本和发布工具契约测试、C# 诊断版本检查。

正式发包前另记真实 Windows 验收：旧 2.x 测试版升级新测试版再恢复；UAC 取消后旧版可用；采集中禁止安装；卸载保留／删除数据；运行路径、版本、SHA、全部组件与包一致。单元测试不替代真实安装与 UI 验收。当前代码实施授权不包含生成发行安装包、发布或覆盖本机。
