//! Minimal read-only PawnIO adapter. No write/tuning function is exposed.
//! ABI/locking adapted from cpuzeez 26d9f0e (MIT OR Apache-2.0).
#[cfg(windows)]
mod win {
    use std::{ffi::{c_void,CString},path::PathBuf,sync::OnceLock};
    type Handle=*mut c_void;
    type Open=unsafe extern "system" fn(*mut Handle)->i32;
    type Load=unsafe extern "system" fn(Handle,*const u8,usize)->i32;
    type Exec=unsafe extern "system" fn(Handle,*const u8,*const u64,usize,*mut u64,usize,*mut usize)->i32;
    type Close=unsafe extern "system" fn(Handle)->i32;
    #[link(name="kernel32")]
    extern "system" {
        fn LoadLibraryExW(name:*const u16,file:Handle,flags:u32)->Handle;
        fn GetProcAddress(module:Handle,name:*const u8)->Handle;
        fn CreateMutexW(attr:Handle,owner:i32,name:*const u16)->Handle;
        fn OpenMutexW(access:u32,inherit:i32,name:*const u16)->Handle;
        fn WaitForSingleObject(handle:Handle,timeout:u32)->u32;
        fn ReleaseMutex(handle:Handle)->i32;
        fn CloseHandle(handle:Handle)->i32;
    }
    #[link(name="advapi32")]
    extern "system" {fn RegGetValueW(key:Handle,sub:*const u16,value:*const u16,flags:u32,kind:*mut u32,data:*mut c_void,size:*mut u32)->i32;}
    fn wide(s:&str)->Vec<u16>{s.encode_utf16().chain(Some(0)).collect()}
    struct Lib {open:Open,load:Load,exec:Exec,close:Close}
    fn lib()->Option<&'static Lib> {
        static LIB:OnceLock<Option<Lib>>=OnceLock::new();
        LIB.get_or_init(||unsafe {
            let key=wide("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\PawnIO");let value=wide("InstallLocation");let mut data=[0u16;32768];let mut size=(data.len()*2) as u32;
            if RegGetValueW(-2147483646isize as Handle,key.as_ptr(),value.as_ptr(),0x00010002,std::ptr::null_mut(),data.as_mut_ptr().cast(),&mut size)!=0{return None;}
            let end=data.iter().position(|v|*v==0)?;let path=PathBuf::from(String::from_utf16(&data[..end]).ok()?).join("PawnIOLib.dll");
            use std::os::windows::ffi::OsStrExt;let file:Vec<u16>=path.as_os_str().encode_wide().chain(Some(0)).collect();
            let dll=LoadLibraryExW(file.as_ptr(),std::ptr::null_mut(),0x100|0x800);if dll.is_null(){return None;}
            let open=GetProcAddress(dll,b"pawnio_open\0".as_ptr());let load=GetProcAddress(dll,b"pawnio_load\0".as_ptr());let exec=GetProcAddress(dll,b"pawnio_execute\0".as_ptr());let close=GetProcAddress(dll,b"pawnio_close\0".as_ptr());
            if [open,load,exec,close].iter().any(|p|p.is_null()){return None;}
            Some(Lib{open:std::mem::transmute::<Handle,Open>(open),load:std::mem::transmute::<Handle,Load>(load),exec:std::mem::transmute::<Handle,Exec>(exec),close:std::mem::transmute::<Handle,Close>(close)})
        }).as_ref()
    }
    pub struct Module {lib:&'static Lib,handle:Handle}
    impl Module {
        pub fn open(name:&str)->Option<Self> {
            let bytes:&[u8]=match name {
                "AMDFamily17"=>include_bytes!("../../resources/pawnio/AMDFamily17.bin"),
                "IntelMCHBAR"=>include_bytes!("../../resources/pawnio/IntelMCHBAR.bin"),
                _=>return None,
            };
            let lib=lib()?;let mut handle=std::ptr::null_mut();
            unsafe {if (lib.open)(&mut handle)<0||handle.is_null(){return None;}if (lib.load)(handle,bytes.as_ptr(),bytes.len())<0{(lib.close)(handle);return None;}}
            Some(Self{lib,handle})
        }
        pub fn read(&self,operation:&str,address:u32)->Option<u32> {
            if !matches!(operation,"ioctl_read_smn"|"ioctl_read_dword"){return None;}
            let function=CString::new(operation).ok()?;
            let name=wide("Global\\Access_PCI");
            unsafe {
                let mut lock=CreateMutexW(std::ptr::null_mut(),0,name.as_ptr());if lock.is_null(){lock=OpenMutexW(0x100001,0,name.as_ptr());}if lock.is_null(){return None;}
                let acquired=WaitForSingleObject(lock,200);if acquired!=0&&acquired!=0x80{CloseHandle(lock);return None;}
                let input=address as u64;let mut output=0u64;let mut returned=0;
                let hr=(self.lib.exec)(self.handle,function.as_ptr().cast(),&input,1,&mut output,1,&mut returned);
                ReleaseMutex(lock);CloseHandle(lock);
                (hr>=0&&returned==1&&output!=u32::MAX as u64).then_some(output as u32)
            }
        }
    }
    impl Drop for Module {fn drop(&mut self){unsafe{(self.lib.close)(self.handle);}}}
}
#[cfg(windows)] pub use win::Module;
#[cfg(not(windows))]
pub struct Module;
#[cfg(not(windows))]
impl Module {pub fn open(_: &str)->Option<Self>{None}pub fn read(&self,_:&str,_:u32)->Option<u32>{None}}
