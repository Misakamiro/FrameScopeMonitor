# Registers window classes only. Never creates an HWND, shows UI, or elevates.
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$TargetDirectory,
    [string]$RepoRoot=(Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference='Stop'
$source=Get-Content -LiteralPath (Join-Path $RepoRoot 'desktop-preview/src-tauri/vendor/tao/src/platform_impl/windows/window.rs') -Raw
$function=[regex]::Match($source,'(?s)unsafe fn register_window_class\(.*?(?=\r?\nunsafe extern "system" fn window_proc)')
if(-not $function.Success){throw 'Production window class registration function not found'}
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'src') -Force | Out-Null
$manifest=@'
[package]
name = "framescope-window-class-contract"
version = "0.0.0"
edition = "2021"
[workspace]
[dependencies]
windows = { version = "=0.61.3", features = ["Win32_Foundation", "Win32_Graphics_Gdi", "Win32_System_LibraryLoader", "Win32_UI_WindowsAndMessaging"] }
'@
$harness=@'
use std::mem;
use windows::{core::PCWSTR,Win32::{Foundation::*,Graphics::Gdi::HBRUSH,System::LibraryLoader::GetModuleHandleW,UI::WindowsAndMessaging::*}};
mod util {pub fn encode_wide(s:&str)->Vec<u16>{s.encode_utf16().chain(Some(0)).collect()}}
unsafe extern "system" fn window_proc(h:HWND,m:u32,w:WPARAM,l:LPARAM)->LRESULT{DefWindowProcW(h,m,w,l)}
fn class_style(name:&[u16])->WNDCLASS_STYLES {
    unsafe {
        let mut class=WNDCLASSEXW{cbSize:mem::size_of::<WNDCLASSEXW>() as u32,..Default::default()};
        GetClassInfoExW(Some(HINSTANCE(GetModuleHandleW(PCWSTR::null()).unwrap().0)),PCWSTR(name.as_ptr()),&mut class).unwrap();
        class.style
    }
}
#[test] fn ordinary_class_preserves_own_dc() {
    let class=unsafe{register_window_class("FrameScope.Test.Ordinary",false)};
    assert_ne!(class_style(&class).0 & CS_OWNDC.0,0);
}
#[test] fn layered_overlay_has_no_incompatible_class_style() {
    let class=unsafe{register_window_class("FrameScope.Test.Layered",true)};
    assert_eq!(class_style(&class).0 & (CS_OWNDC|CS_CLASSDC).0,0,"WS_EX_LAYERED forbids CS_OWNDC / CS_CLASSDC");
}
#[test] fn overlay_does_not_reuse_an_already_registered_ordinary_class() {
    let ordinary=unsafe{register_window_class("FrameScope.Test.SharedName",false)};
    let overlay=unsafe{register_window_class("FrameScope.Test.SharedName",true)};
    assert_ne!(ordinary,overlay,"A second RegisterClassExW cannot replace the existing class style");
    assert_eq!(class_style(&overlay).0 & (CS_OWNDC|CS_CLASSDC).0,0);
    assert_ne!(class_style(&ordinary).0 & CS_OWNDC.0,0);
}
#[test] fn overlay_class_can_be_registered_repeatedly() {
    let first=unsafe{register_window_class("FrameScope.Test.Repeated",true)};
    let second=unsafe{register_window_class("FrameScope.Test.Repeated",true)};
    assert_eq!(first,second);
    assert_eq!(class_style(&second).0 & (CS_OWNDC|CS_CLASSDC).0,0);
}
'@
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'Cargo.toml'),$manifest,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'src/lib.rs'),($harness+"`n"+$function.Value),[Text.UTF8Encoding]::new($false))
& cargo test --manifest-path (Join-Path $OutputDirectory 'Cargo.toml') --target-dir $TargetDirectory --release --offline -- --test-threads=1
if($LASTEXITCODE -ne 0){throw 'Native window class compatibility regression failed'}
