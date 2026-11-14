//! Windows UAC elevation without PowerShell or a flashing console.
use crate::printers;

fn encode(value: &str) -> String {
    let mut result = String::with_capacity(value.len() * 2);
    for byte in value.bytes() { result.push_str(&format!("{byte:02x}")); }
    result
}

fn decode(hex: &str) -> Result<String, String> {
    if hex.len() > 256 || !hex.len().is_multiple_of(2) { return Err("Invalid argument".into()); }
    let bytes = (0..hex.len()).step_by(2)
        .map(|n| u8::from_str_radix(&hex[n..n+2], 16).map_err(|_| "Invalid argument".to_string()))
        .collect::<Result<Vec<_>, _>>()?;
    String::from_utf8(bytes).map_err(|_| "Invalid argument".into())
}

pub fn admin_command_if_requested() -> Option<i32> {
    let arg = std::env::args().nth(1)?;
    let (operation, data) = if let Some(hex) = arg.strip_prefix("--vprint-admin-create=") {
        ("create", hex)
    } else if let Some(hex) = arg.strip_prefix("--vprint-admin-delete=") {
        ("delete", hex)
    } else if arg == "--vprint-admin-history" {
        return Some(if crate::events::enable_history().is_ok() { 0 } else { 1 });
    } else {
        return None;
    };
    let code = match decode(data) {
        Ok(value) => {
            let result = match operation {
                "create" => printers::create(&value),
                _ => printers::remove(&value),
            };
            if result.is_ok() { 0 } else { 1 }
        }
        Err(_) => 2,
    };
    Some(code)
}

#[cfg(windows)]
mod native {
    use super::*;
    use std::{ffi::{c_void, OsStr}, io, os::windows::ffi::OsStrExt, ptr::{null, null_mut}};

    #[repr(C)]
    #[allow(non_snake_case)]
    struct ShellExecuteInfo {
        cbSize: u32,
        fMask: u32,
        hwnd: *mut c_void,
        lpVerb: *const u16,
        lpFile: *const u16,
        lpParameters: *const u16,
        lpDirectory: *const u16,
        nShow: i32,
        hInstApp: *mut c_void,
        lpIDList: *mut c_void,
        lpClass: *const u16,
        hkeyClass: *mut c_void,
        dwHotKey: u32,
        hIcon: *mut c_void,
        hProcess: *mut c_void,
    }

    #[link(name = "shell32")]
    unsafe extern "system" {
        fn ShellExecuteExW(info: *mut ShellExecuteInfo) -> i32;
    }
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn WaitForSingleObject(handle: *mut c_void, timeout: u32) -> u32;
        fn GetExitCodeProcess(handle: *mut c_void, exit_code: *mut u32) -> i32;
        fn CloseHandle(handle: *mut c_void) -> i32;
    }
    fn wide(s: &str) -> Vec<u16> {
        OsStr::new(s).encode_wide().chain(Some(0)).collect()
    }

    pub fn run_elevated(operation: &str, value: &str) -> Result<(), String> {
        let exe = std::env::current_exe().map_err(|err| err.to_string())?;
        let exe = wide(&exe.to_string_lossy());
        let verb = wide("runas");
        let params = if operation == "history" {
            wide("--vprint-admin-history")
        } else {
            wide(&format!("--vprint-admin-{}={}", operation, encode(value)))
        };
        let mut info = ShellExecuteInfo {
            cbSize: std::mem::size_of::<ShellExecuteInfo>() as u32,
            fMask: 0x40, // SEE_MASK_NOCLOSEPROCESS
            hwnd: null_mut(),
            lpVerb: verb.as_ptr(),
            lpFile: exe.as_ptr(),
            lpParameters: params.as_ptr(),
            lpDirectory: null(),
            nShow: 0, // SW_HIDE; no terminal window
            hInstApp: null_mut(),
            lpIDList: null_mut(),
            lpClass: null(),
            hkeyClass: null_mut(),
            dwHotKey: 0,
            hIcon: null_mut(),
            hProcess: null_mut(),
        };
        if unsafe { ShellExecuteExW(&mut info) } == 0 {
            let error = io::Error::last_os_error();
            if error.raw_os_error() == Some(1223) {
                return Err("Windows administrator approval was cancelled.".into());
            }
            return Err(format!("Windows denied the administrator request: {error}"));
        }
        if info.hProcess.is_null() { return Err("Windows did not return the elevation process.".into()); }
        // SAFETY: ShellExecuteExW returned a live process handle.
        let result = unsafe {
            let wait = WaitForSingleObject(info.hProcess, 120_000);
            let mut code = u32::MAX;
            let success = wait == 0 && GetExitCodeProcess(info.hProcess, &mut code) != 0;
            CloseHandle(info.hProcess);
            if success && code == 0 {
                Ok(())
            } else if wait == 0x102 {
                Err("Administrator operation timed out.".into())
            } else {
                Err("Windows could not complete the printer operation with administrator rights.".into())
            }
        };
        result
    }
}

#[cfg(windows)]
pub use native::run_elevated;

#[cfg(not(windows))]
pub fn run_elevated(_op: &str, _value: &str) -> Result<(), String> {
    Err("Windows-only".into())
}

pub fn enable_history() -> Result<(), String> {
    match crate::events::enable_history() {
        Err(error) if error == "ADMIN_REQUIRED" => run_elevated("history", ""),
        other => other,
    }
}

pub fn create(label: &str) -> Result<(), String> {
    match printers::create(label) {
        Err(error) if error == "ADMIN_REQUIRED" => run_elevated("create", label),
        other => other,
    }
}

pub fn remove(name: &str) -> Result<(), String> {
    printers::managed_name(name)?;
    match printers::remove(name) {
        Err(error) if error == "ADMIN_REQUIRED" => run_elevated("delete", name),
        other => other,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn elevation_arguments_roundtrip() {
        for label in ["Kitchen", "Mobile Receipt", "L'été", "🌿 Printer"] {
            assert_eq!(decode(&encode(label)).unwrap(), label);
        }
        assert!(decode("abc").is_err());
        assert!(decode("zz").is_err());
    }
}
