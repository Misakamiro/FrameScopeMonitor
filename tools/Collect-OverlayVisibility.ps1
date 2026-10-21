# Read-only, bounded window diagnostics. No ETW sessions, input, elevation,
# application launches, configuration writes, or changes to window styles.
param(
    [ValidateRange(1,30)][int]$Samples=10,
    [ValidateRange(1,5)][int]$IntervalSeconds=2,
    [ValidateRange(0,15)][int]$StartDelaySeconds=5,
    [string]$OutputPath=(Join-Path ([Environment]::GetFolderPath('Desktop')) ('FrameScope-overlay-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.json'))
)
$ErrorActionPreference='Stop'
if(-not ('FrameScopeOverlayVisibility' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
public static class FrameScopeOverlayVisibility {
    public struct Rect { public int left,top,right,bottom; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    public struct Monitor { public int size; public Rect bounds,work; public uint flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string device; }
    delegate bool WindowCallback(IntPtr window,IntPtr data);
    delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,IntPtr rect,IntPtr data);
    [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr data);
    [DllImport("user32.dll")]static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref Monitor info);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
    [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll",SetLastError=true)]static extern bool GetWindowBand(IntPtr window,out uint band);
    [DllImport("user32.dll")]static extern bool GetLayeredWindowAttributes(IntPtr window,out uint key,out byte alpha,out uint flags);
    [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out uint value,int size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int length);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int kind,out uint value,int size,out int required);
    static bool Relevant(string name) {return name.Equals("FrameScope",StringComparison.OrdinalIgnoreCase)||name.Equals("TslGame",StringComparison.OrdinalIgnoreCase)||name.Equals("TslGame-Win64-Shipping",StringComparison.OrdinalIgnoreCase);}
    static string ProcessName(uint pid) {try {using(var p=Process.GetProcessById((int)pid))return p.ProcessName;}catch{return "unavailable";}}
    static object ProcessInfo(uint pid,string name) {
        var result=new Dictionary<string,object>{{"pid",pid},{"name",name}};
        var process=OpenProcess(0x1000,false,pid);
        if(process==IntPtr.Zero){result["queryError"]=Marshal.GetLastWin32Error();return result;}
        try {
            var path=new StringBuilder(32768);int length=path.Capacity;
            if(QueryFullProcessImageName(process,0,path,ref length))result["image"]=path.ToString();
            IntPtr token;
            if(OpenProcessToken(process,8,out token)) {
                try { foreach(var field in new[]{new[]{"uiaccess","26"},new[]{"elevated","20"}}) {
                    uint value;int required;
                    if(GetTokenInformation(token,int.Parse(field[1]),out value,4,out required))result[field[0]]=value!=0;
                    else result[field[0]+"Error"]=Marshal.GetLastWin32Error();
                }}finally{CloseHandle(token);}
            }else result["tokenQueryError"]=Marshal.GetLastWin32Error();
        }finally{CloseHandle(process);}
        return result;
    }
    public static object Capture() {
        IntPtr previous=IntPtr.Zero;
        try {previous=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
        try {
            var processes=new Dictionary<uint,string>();
            foreach(var name in new[]{"FrameScope","TslGame","TslGame-Win64-Shipping"})foreach(var p in Process.GetProcessesByName(name))using(p)processes[(uint)p.Id]=p.ProcessName;
            uint foregroundPid;var foreground=GetForegroundWindow();GetWindowThreadProcessId(foreground,out foregroundPid);
            var windows=new List<object>();
            EnumWindows(delegate(IntPtr hwnd,IntPtr unused) {
                uint pid;GetWindowThreadProcessId(hwnd,out pid);string name;
                if(!processes.TryGetValue(pid,out name))return true;
                var title=new StringBuilder(256);GetWindowText(hwnd,title,title.Capacity);
                Rect rect;bool rectOk=GetWindowRect(hwnd,out rect);uint band,cloaked,key,flags;byte alpha;
                bool bandOk=false;band=0;try{bandOk=GetWindowBand(hwnd,out band);}catch(EntryPointNotFoundException){}
                bool alphaOk=GetLayeredWindowAttributes(hwnd,out key,out alpha,out flags);
                int cloakResult=DwmGetWindowAttribute(hwnd,14,out cloaked,4);
                windows.Add(new Dictionary<string,object>{{"hwnd",hwnd.ToInt64()},{"pid",pid},{"process",name},{"title",title.ToString()},
                    {"foreground",hwnd==foreground},{"visible",IsWindowVisible(hwnd)},{"exStyle",unchecked((uint)GetWindowLong(hwnd,-20)).ToString("X8")},
                    {"rect",rectOk?(object)new[]{rect.left,rect.top,rect.right,rect.bottom}:null},{"band",bandOk?(object)band:null},
                    {"cloaked",cloakResult==0?(object)cloaked:null},{"layerAlpha",alphaOk?(object)alpha:null},{"layerFlags",alphaOk?(object)flags:null}});
                return true;
            },IntPtr.Zero);
            var monitors=new List<object>();
            EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr monitor,IntPtr dc,IntPtr rect,IntPtr data) {
                var info=new Monitor();info.size=Marshal.SizeOf(typeof(Monitor));
                if(GetMonitorInfo(monitor,ref info))monitors.Add(new{device=info.device,bounds=info.bounds,work=info.work,primary=(info.flags&1)!=0});return true;
            },IntPtr.Zero);
            var images=new List<object>();foreach(var p in processes)images.Add(ProcessInfo(p.Key,p.Value));
            return new Dictionary<string,object>{{"utc",DateTime.UtcNow.ToString("o")},{"foregroundPid",foregroundPid},
                {"foregroundProcess",ProcessName(foregroundPid)},{"foregroundIsRelevant",Relevant(ProcessName(foregroundPid))},
                {"processes",images},{"windows",windows},{"monitors",monitors}};
        }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
    }
}
'@
}
Write-Output "请在 $StartDelaySeconds 秒内切回 PUBG 全屏并保持悬浮窗开启；采样期间不操作窗口。"
if($StartDelaySeconds){Start-Sleep -Seconds $StartDelaySeconds}
$frames=New-Object 'Collections.Generic.List[object]'
for($index=0;$index -lt $Samples;$index++) {
    $frames.Add([FrameScopeOverlayVisibility]::Capture())
    if($index+1 -lt $Samples){Start-Sleep -Seconds $IntervalSeconds}
}
$configurationReadErrors=New-Object 'Collections.Generic.List[object]'
function Read-DiagnosticJson([string]$Path) {
    try {
        if(Test-Path -LiteralPath $Path -PathType Leaf) {
            Get-Content -LiteralPath $Path -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        }
    } catch {
        $configurationReadErrors.Add([pscustomobject]@{path=$Path;error=$_.Exception.Message})
        return $null
    }
}
$builds=@()
foreach($image in @($frames[0].processes)) {
    if($image.name -eq 'FrameScope' -and $image.image) {
        $file=Join-Path (Split-Path -Parent $image.image) 'FrameScopeBuildInfo.json'
        $build=Read-DiagnosticJson $file
        if($null -ne $build){$builds+=@{image=$image.image;build=$build}}
    }
}
$settingsFile=Join-Path $env:APPDATA 'com.framescope.ui-preview/preview-native-settings.json'
$settings=$null
$savedSettings=Read-DiagnosticJson $settingsFile
if($null -ne $savedSettings){$settings=$savedSettings.'overlay-settings' | Select-Object style,opacity,position_x,position_y,vertical_position_x,vertical_position_y,position_reference,vertical_position_reference}
[ordered]@{schema=1;collectorVersion=2;readOnly=$true;builds=$builds;overlaySettings=$settings;configurationReadErrors=@($configurationReadErrors.ToArray());frames=@($frames.ToArray())} | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output "诊断记录已保存：$OutputPath"
