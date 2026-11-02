"""Export reviewed client inputs, never the mixed repository or its Git history."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT_FILES={'build.ps1','VERSION','EDITION','dependencies.lock.json','build-dependencies.lock.json','native-packages.lock.json',
            'framescope-config.example.json','THIRD-PARTY-NOTICES.md','.gitattributes','.gitignore'}
PREFIXES=('src/','assets/','tools/','tests/','packaging/','desktop-preview/')
GENERATED={'node_modules','target','dist','bin','obj','.cache','.vite','.git','coverage','__pycache__'}
ONLINE_TESTS={'account-form.test.ts','accounts.browser.cjs','community-client.test.ts','community-form.test.ts',
              'community-navigation.test.ts','community-tabs.test.tsx','community.browser.cjs','growth.browser.cjs',
              'growth.test.ts','release-boundaries.test.ts','benchmark.test.ts','benchmark-ui.test.tsx','benchmark.browser.cjs','benchmark-batch.browser.cjs'}


def digest(data):return hashlib.sha256(data).hexdigest().upper()


def allowed(name,edition):
    parts=Path(name).parts
    if any(p in GENERATED for p in parts):return False
    if not (name in ROOT_FILES or name.startswith(PREFIXES)):return False
    if name.startswith(('desktop-preview/src-tauri/resources/','desktop-preview/src-framescope-baseline/')):return False
    if name.endswith(('.exe','.dll','.pdb','.zip','.log','.pyc','.sqlite3','.db','.key','.pem','.map')):return False
    if name in {'packaging/release-notes.md','desktop-preview/AGENTS.md'}:return False
    if edition=='offline':
        if name.startswith(('desktop-preview/src/community/','desktop-preview/src/components/community-tools/')):return False
        if name=='desktop-preview/src/hooks/use-community-tools.ts':return False
        if name in {'desktop-preview/src-tauri/src/account.rs','desktop-preview/src-tauri/src/account_store.rs',
                    'desktop-preview/src-tauri/src/community.rs','desktop-preview/src-tauri/src/benchmark.rs','desktop-preview/src/preview/settings/AccountSettings.tsx',
                    'desktop-preview/src/preview/settings/account-client.ts','desktop-preview/src/preview/settings/account-form.ts'}:return False
        if name.startswith('desktop-preview/tests/') and Path(name).name in ONLINE_TESTS:return False
    return True


def remove_section(text, begin, end):
    if begin not in text:return text
    start=text.index(begin)
    finish=text.index(end,start)
    return text[:start]+text[finish:]


def project(name,text):
    if name=='desktop-preview/src/pages/ToolsPage.tsx':
        text=re.sub(r'^.*(?:import \{ CommunityToolSection|<CommunityToolSection).*$','',text,flags=re.M)
    if name=='desktop-preview/src-tauri/src/backend.rs':
        text=remove_section(text,'pub fn report_benchmark_export(', 'pub fn report_open_directory(')
    if name=='src/reporting/FrameScopeReportGenerator.Cli.cs':
        text=remove_section(text,'    internal static void ExportBenchmark(', '    private static string GetArgValue(')
    if name=='src/reporting/FrameScopeReportGenerator.cs':
        text=remove_section(text,'        if (args != null && args.Contains("--export-benchmark"))',
                            '        if (args != null && args.Any(a => a.Equals("--inspect-reports"')
    if name=='tests/FrameScopeReportManifestTests.cs':
        text=text.replace('        BenchmarkExportRejectsPrivateReportFields();\n','')
        text=remove_section(text,'    private static void BenchmarkExportRejectsPrivateReportFields()',
                            '    private static void BatchInspectionIsReadOnlyAndReportsCapabilities()')
        text=text.replace('MissingInventoryNeverReadsTodaysMachineAndTimeoutDoesNotBlockExport', 'MissingInventoryNeverReadsTodaysMachine')
        text=text.replace('HardwareClockAndRecordedMemorySurviveReportAndPublicExport', 'HardwareClockAndRecordedMemorySurviveReport')
        text=re.sub(r'^.*FrameScopeReportGenerator.ExportBenchmark\(dir,Path.Combine\(dir,"timeout.gz"\)\);\n',
                    '            AssertTrue(FrameScopeReportArtifacts.Inspect(dir).IsComplete,"local report retains timeout diagnostics");\n',text,flags=re.M)
        text=re.sub(r'^.*bool blocked=false;try\{FrameScopeReportGenerator.ExportBenchmark.*\n.*AssertTrue\(blocked,.*\n',
                    '            AssertTrue(FrameScopeReportArtifacts.Inspect(dir).IsComplete,"local report retains recorded hardware errors");\n',text,flags=re.M)
        start='            string path=Path.Combine(dir,"export.gz");'
        if start in text:
            begin=text.index(start);finish=text.index('        } finally {Directory.Delete(dir,true);}',begin)
            text=text[:begin]+'            AssertTrue(Convert.ToString(GetMap(data,"hardware")["MemorySummary"]).Contains("Kingston CL30"),"local report retains capture-time memory");\n'+text[finish:]
    if name=='desktop-preview/src/App.tsx':
        text=re.sub(r'^import .* from "\./community/.*\n','',text,flags=re.M)
        text=re.sub(r'^\s*<Route path="/(?:user|account|donations|feedback|benchmarks)"[^\n]*\n','',text,flags=re.M)
    if name in {'desktop-preview/src/components/ui/sidebar.tsx','desktop-preview/src/config/search-index.ts'}:
        text=text.replace(', UserRound, HeartHandshake, MessageSquare, Trophy','')
        if name.endswith('sidebar.tsx'):
            text=re.sub(r'^\s*\{ path: "/(?:user|donations|feedback|benchmarks)"[^\n]*\n','',text,flags=re.M)
        else:
            text=re.sub(r'function communityFeatures\(.*?\n}\n','',text,flags=re.S)
            text=remove_section(text,'  { id: "benchmarks",','  { id: "home",')
    if name=='desktop-preview/src/preview/settings/navigation.ts':
        text=text.replace(', "/benchmarks"','').replace(', "/user", "/donations", "/feedback"','')
    if name=='desktop-preview/src/pages/SettingsPage.tsx':
        text=text.replace('Navigate, ', '').replace(', Navigate', '')
        text=re.sub(r'^\s*"/(?:user|donations|feedback|benchmarks)": [^\n]*\n','',text,flags=re.M)
        text=re.sub(r'^\s*if \(section === "account"\)[^\n]*\n','',text,flags=re.M)
    if name=='desktop-preview/src/preview/pages/ReportsPage.tsx':
        text=text.replace('[params, setParams]', '[params]')
        text=re.sub(r'^[^\n]*framescope-online-upload:start[^\n]*\n.*?^[^\n]*framescope-online-upload:end[^\n]*\n','',text,flags=re.M|re.S)
        text=re.sub(r'^.*(?:BenchmarkUpload|canUpload|setUpload|upload,).*$','',text,flags=re.M)
    if name=='desktop-preview/src-tauri/src/lib.rs':
        text=text.replace('mod account;\n','')
        text=re.sub(r'^\s*"(?:account_|community_)[^\n]*\n','',text,flags=re.M)
        text=re.sub(r'    if command.starts_with\("account_"\).*?\n    }\n','',text,flags=re.S)
    return text


def export(root,destination,edition):
    root=root.resolve();destination=destination.resolve()
    if destination.exists():raise ValueError('Export destination must be new; existing source is preserved')
    names=subprocess.check_output(['git','-C',str(root),'ls-files','-z','--cached','--others','--exclude-standard']).decode().split('\0')
    files={}
    for name in sorted(set(names)):
        source=root/name
        if not name or not source.is_file() or not allowed(name,edition):continue
        if source.is_symlink() or not source.resolve().is_relative_to(root):raise ValueError('Unsafe export path')
        raw=source.read_bytes()
        try:
            text=raw.decode('utf-8-sig').replace('\r\n','\n')
            if edition=='offline':text=project(name,text)
            raw=text.encode()
        except UnicodeDecodeError:pass
        files[name]=raw
    lock=json.loads((root/'dependencies.lock.json').read_text(encoding='utf-8-sig'))
    for entry in [lock['presentMon'],lock['webView2StandaloneInstaller']]:
        data=(root/entry['file']).read_bytes()
        if digest(data)!=entry['sha256'].upper():raise ValueError('Dependency hash mismatch')
        files[entry['file']]=data
    pawn=root/'packaging/PawnIO_setup.exe'
    if not pawn.exists():pawn=root/'reference/nexbox/src-tauri/resources/binaries/PawnIO_setup.exe'
    files['packaging/PawnIO_setup.exe']=pawn.read_bytes()
    files['EDITION']=(edition+'\n').encode()
    files['README.md']=(
        '# FrameScope '+('公开离线版' if edition=='offline' else '完整版对应客户端源码')+'\n\n'
        'Windows 游戏性能监控、硬件采集、报告、OSD 和本地设置。\n\n'
        +('此公开版不包含账号、捐助、反馈、签到、榜单或 NAS 通信代码。保留 GitHub 签名更新。\n\n' if edition=='offline' else
          '客户端源码不包含独立后端、生产配置或凭据。正式连接地址通过构建环境配置。\n'
          '`$env:FRAMESCOPE_ACCOUNT_ORIGIN="https://accounts.example.invalid"`\n\n')+
        '构建需要 Windows、PowerShell、Node.js、Rust MSVC 工具链和 Visual Studio C++ 构建工具。\n'
        '先在 desktop-preview 及 desktop-preview/installer 执行 npm ci；首次构建会按锁文件恢复依赖。\n'
        '源码包不包含 Git 历史。设置 FRAMESCOPE_BUILD_SHA 为 SOURCE_MANIFEST.json 中的 sourceHead；'
        '初始化一个本地 git 仓库并 git add .，供载荷溯源枚举使用。\n'
        '执行 desktop-preview/installer/build-installer.ps1。不要在已安装程序目录构建。\n\n'
        '许可证见 desktop-preview/LICENSE、LICENSE.EXTRA 及 THIRD-PARTY-NOTICES.md。'
        '对应源码接收者保留许可证赋予的再分发权。\n').encode()
    # Public documentation uses the last offline baseline, not private work notes.
    for name in ['PRIVACY.md','CHANGELOG.md']:
        files[name]=((root/name).read_bytes() if edition=='full' else subprocess.check_output(['git','-C',str(root),'show','HEAD:'+name]))
    if edition=='offline':
        text=files['CHANGELOG.md'].decode('utf-8-sig')
        text=text.replace('## Unreleased','## Unreleased\n\n## 2.0.1-beta.8\n\n- 发布独立离线版，保留性能采集、硬件、报告、OSD、本地设置和 GitHub 签名更新。\n- 增加版型校验，关闭正式版 DevTools，排除调试符号和 source map，收紧网页资源策略。',1)
        files['CHANGELOG.md']=text.encode()
    head=subprocess.check_output(['git','-C',str(root),'rev-parse','HEAD']).decode().strip()
    manifest={'schema':1,'edition':edition,'sourceHead':head,
              'files':[{'path':n,'sha256':digest(d)} for n,d in sorted(files.items())]}
    files['SOURCE_MANIFEST.json']=json.dumps(manifest,ensure_ascii=False,indent=2).encode()
    destination.mkdir(parents=True)
    for name,data in files.items():
        path=destination/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
    archive=destination.with_suffix('.zip')
    if archive.exists():raise ValueError('Archive already exists')
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as output:
        for name,data in sorted(files.items()):output.writestr(name,data)
    print(json.dumps({'edition':edition,'files':len(files),'archive':str(archive),'sha256':digest(archive.read_bytes())}))


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    parser.add_argument('--destination',type=Path,required=True);parser.add_argument('--edition',choices=['offline','full'],required=True)
    args=parser.parse_args();export(args.root,args.destination,args.edition)
