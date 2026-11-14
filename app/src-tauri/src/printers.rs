//! Thin, isolated Win32 spooler bindings. All user-controlled strings are validated.
use serde::Serialize;

pub const PREFIX: &str = "VPrint - ";
const DRIVER: &str = "Microsoft Print To PDF";

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Printer {
    pub name: String,
    pub label: String,
    pub driver: String,
    pub port: String,
}

pub fn label(input: &str) -> Result<String, String> {
    let s = input.trim();
    if s.is_empty() || s.chars().count() > 54 {
        return Err("Enter a printer name between 1 and 54 characters.".into());
    }
    if s.ends_with('.') || s.chars().any(|c| c.is_control() || r#"\/:*?"<>|"#.contains(c)) {
        return Err("The printer name contains unsupported Windows characters.".into());
    }
    Ok(s.to_string())
}

pub fn managed_name(input: &str) -> Result<String, String> {
    let Some(rest) = input.strip_prefix(PREFIX) else {
        return Err("Only VPrint-managed printers can be removed.".into());
    };
    if label(rest)? != rest {
        return Err("The printer name is not valid.".into());
    }
    Ok(input.into())
}

#[cfg(windows)]
mod native {
    use super::*;
    use std::{
        ffi::{c_void, OsStr},
        io,
        os::windows::ffi::OsStrExt,
        ptr::{null, null_mut},
    };

    #[repr(C)]
    #[derive(Clone, Copy)]
    struct PrinterInfo2 {
        server: *mut u16,
        name: *mut u16,
        share: *mut u16,
        port: *mut u16,
        driver: *mut u16,
        comment: *mut u16,
        location: *mut u16,
        devmode: *mut c_void,
        separator_file: *mut u16,
        processor: *mut u16,
        datatype: *mut u16,
        parameters: *mut u16,
        security: *mut c_void,
        attributes: u32,
        priority: u32,
        default_priority: u32,
        start_time: u32,
        until_time: u32,
        status: u32,
        jobs: u32,
        average_ppm: u32,
    }

    impl Default for PrinterInfo2 {
        fn default() -> Self {
            // This C POD structure has only nullable pointers and integer fields.
            unsafe { std::mem::zeroed() }
        }
    }

    #[repr(C)]
    struct PrinterDefaults {
        datatype: *mut u16,
        devmode: *mut c_void,
        desired_access: u32,
    }

    #[link(name = "winspool")]
    unsafe extern "system" {
        fn EnumPrintersW(flags: u32, name: *const u16, level: u32,
            buffer: *mut u8, buffer_size: u32, needed: *mut u32, returned: *mut u32) -> i32;
        fn AddPrinterW(server: *const u16, level: u32, printer: *mut u8) -> *mut c_void;
        fn OpenPrinterW(name: *mut u16, handle: *mut *mut c_void,
            defaults: *const PrinterDefaults) -> i32;
        fn GetPrinterW(handle: *mut c_void, level: u32,
            buffer: *mut u8, buffer_size: u32, needed: *mut u32) -> i32;
        fn DeletePrinter(handle: *mut c_void) -> i32;
        fn ClosePrinter(handle: *mut c_void) -> i32;
    }

    fn wide(s: &str) -> Vec<u16> {
        OsStr::new(s).encode_wide().chain(Some(0)).collect()
    }

    // SAFETY: strings returned in PRINTER_INFO_2 are owned by the live EnumPrinters
    // allocation; the caller keeps its allocation alive while reading.
    unsafe fn read_wide(ptr: *const u16) -> String {
        if ptr.is_null() { return String::new(); }
        let mut len = 0;
        while len < 32768 && *ptr.add(len) != 0 { len += 1; }
        String::from_utf16_lossy(std::slice::from_raw_parts(ptr, len))
    }

    pub fn list() -> Result<Vec<Printer>, String> {
        let mut size = 0u32;
        let mut count = 0u32;
        // SAFETY: the first call probes the buffer size; no pointers are retained.
        unsafe { EnumPrintersW(2, null(), 2, null_mut(), 0, &mut size, &mut count) };
        if size == 0 { return Ok(vec![]); }
        if size > 16 * 1024 * 1024 { return Err("Printer enumeration exceeded the safe buffer size.".into()); }
        let mut bytes = vec![0u8; size as usize];
        // SAFETY: Windows writes at most bytes.len() bytes; size was provided by the OS.
        let ok = unsafe { EnumPrintersW(2, null(), 2, bytes.as_mut_ptr(), size, &mut size, &mut count) };
        if ok == 0 { return Err(io::Error::last_os_error().to_string()); }
        let struct_size = std::mem::size_of::<PrinterInfo2>();
        if (count as usize) > bytes.len() / struct_size {
            return Err("Windows returned an invalid printer enumeration size.".into());
        }
        let mut entries = Vec::new();
        for n in 0..count as usize {
            // SAFETY: read_unaligned supports Vec<u8> storage; bounds checked above.
            let p = unsafe { std::ptr::read_unaligned(bytes.as_ptr().add(n * struct_size).cast::<PrinterInfo2>()) };
            let name = unsafe { read_wide(p.name) };
            let driver = unsafe { read_wide(p.driver) };
            if name.starts_with(PREFIX) && driver.eq_ignore_ascii_case(DRIVER) {
                entries.push(Printer {
                    label: name[PREFIX.len()..].to_string(),
                    name,
                    driver,
                    port: unsafe { read_wide(p.port) },
                });
            }
        }
        entries.sort_by(|a,b| a.label.to_lowercase().cmp(&b.label.to_lowercase()));
        Ok(entries)
    }

    pub fn create(input: &str) -> Result<(), String> {
        let name = format!("{}{}", PREFIX, label(input)?);
        if list()?.iter().any(|p| p.name.eq_ignore_ascii_case(&name)) {
            return Err("A VPrint printer with this name already exists.".into());
        }
        let mut name = wide(&name);
        let mut port = wide("PORTPROMPT:");
        let mut driver = wide(DRIVER);
        let mut processor = wide("winprint");
        let mut printer = PrinterInfo2 {
            name: name.as_mut_ptr(), port: port.as_mut_ptr(),
            driver: driver.as_mut_ptr(), processor: processor.as_mut_ptr(),
            attributes: 0x40, // PRINTER_ATTRIBUTE_LOCAL
            ..PrinterInfo2::default()
        };
        // SAFETY: all required PRINTER_INFO_2 fields point to live NUL-terminated UTF-16 arrays.
        let handle = unsafe { AddPrinterW(null(), 2, (&mut printer as *mut PrinterInfo2).cast()) };
        if handle.is_null() {
            let error = io::Error::last_os_error();
            if error.raw_os_error() == Some(5) { return Err("ADMIN_REQUIRED".into()); }
            return Err(format!("Windows could not create the printer: {error}"));
        }
        // SAFETY: AddPrinterW returned an owned printer handle.
        unsafe { ClosePrinter(handle) };
        Ok(())
    }

    pub fn remove(input: &str) -> Result<(), String> {
        let name = managed_name(input)?;
        let mut name_w = wide(&name);
        let mut handle: *mut c_void = null_mut();
        let defaults = PrinterDefaults {
            datatype: null_mut(),
            devmode: null_mut(),
            desired_access: 4, // PRINTER_ACCESS_ADMINISTER
        };
        // SAFETY: UTF-16 name and handle output storage remain valid.
        let ok = unsafe { OpenPrinterW(name_w.as_mut_ptr(), &mut handle, &defaults) };
        if ok == 0 {
            let error = io::Error::last_os_error();
            if error.raw_os_error() == Some(5) { return Err("ADMIN_REQUIRED".into()); }
            return Err(format!("Windows could not open the printer: {error}"));
        }
        // SAFETY: handle is valid while this function executes.
        let result = (|| {
            let mut size = 0u32;
            unsafe { GetPrinterW(handle, 2, null_mut(), 0, &mut size) };
            if size == 0 || size > 65536 { return Err("Cannot verify printer identity.".into()); }
            let mut bytes = vec![0u8; size as usize];
            let ok = unsafe { GetPrinterW(handle, 2, bytes.as_mut_ptr(), size, &mut size) };
            if ok == 0 { return Err(io::Error::last_os_error().to_string()); }
            let p = unsafe { std::ptr::read_unaligned(bytes.as_ptr().cast::<PrinterInfo2>()) };
            let actual_name = unsafe { read_wide(p.name) };
            let driver = unsafe { read_wide(p.driver) };
            if actual_name != name || !driver.eq_ignore_ascii_case(DRIVER) {
                return Err("Printer ownership or driver has changed; refusing removal.".into());
            }
            if unsafe { DeletePrinter(handle) } == 0 {
                return Err(format!("Windows could not delete the printer: {}", io::Error::last_os_error()));
            }
            Ok(())
        })();
        unsafe { ClosePrinter(handle) };
        result
    }
}

#[cfg(windows)]
pub use native::{create, list, remove};

#[cfg(not(windows))]
pub fn list() -> Result<Vec<Printer>, String> { Ok(vec![]) }
#[cfg(not(windows))]
pub fn create(_label: &str) -> Result<(), String> { Err("Windows-only".into()) }
#[cfg(not(windows))]
pub fn remove(_name: &str) -> Result<(), String> { Err("Windows-only".into()) }

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn normalize_names() {
        assert_eq!(label(" Kitchen ").unwrap(), "Kitchen");
        for s in ["", "   ", "A/B", "A|B", "A\nB", "Too long name with more than fifty four ASCII characters in it"] {
            assert!(label(s).is_err(), "accepted invalid name: {s}");
        }
        assert_eq!(managed_name("VPrint - Kitchen").unwrap(), "VPrint - Kitchen");
        assert!(managed_name("Office Laser").is_err());
    }
}
