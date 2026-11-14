//! Windows Event Log ingestion. No PowerShell, polling process, or print driver hook.
use serde::Serialize;

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct PrintJob {
    pub id: String,
    pub printer_name: String,
    pub document_name: String,
    pub user_name: String,
    pub job_number: i64,
    pub pages: i64,
    pub size_bytes: i64,
    pub printed_at: String,
}

pub fn parse_event(xml: &str) -> Option<PrintJob> {
    let doc = roxmltree::Document::parse(xml).ok()?;
    let event = doc.descendants().find(|n| n.has_tag_name("DocumentPrinted"))?;
    let field = |number: u8| -> String {
        let tag = format!("Param{number}");
        event.children().find(|n| n.is_element() && n.tag_name().name() == tag)
            .and_then(|n| n.text()).unwrap_or_default().trim().to_string()
    };
    let printer = field(5);
    if !printer.starts_with(crate::printers::PREFIX) { return None; }
    let timestamp = doc.descendants()
        .find(|n| n.has_tag_name("TimeCreated"))?
        .attribute("SystemTime")?
        .to_string();
    // Reject a malformed time rather than synthesizing a misleading 'now' timestamp.
    chrono::DateTime::parse_from_rfc3339(&timestamp).ok()?;
    let record_id = doc.descendants().find(|n| n.has_tag_name("EventRecordID"))?
        .text()?.trim().parse::<u64>().ok()?;
    let document = field(2);
    let user = field(3);
    Some(PrintJob {
        id: format!("evt:{record_id}:{timestamp}"),
        printer_name: printer,
        document_name: if document.is_empty() { "Print document".into() } else { document },
        user_name: if user.is_empty() { "Unknown".into() } else { user },
        job_number: field(1).parse::<i64>().unwrap_or(0).max(0),
        size_bytes: field(7).parse::<i64>().unwrap_or(0).max(0),
        pages: field(8).parse::<i64>().unwrap_or(0).max(0),
        printed_at: timestamp,
    })
}

#[cfg(windows)]
mod native {
    use std::{ffi::{c_void, OsStr}, io, os::windows::ffi::OsStrExt, ptr::{null, null_mut}};
    use super::*;

    const CHANNEL: &str = "Microsoft-Windows-PrintService/Operational";
    const QUERY: &str = "*[System[(EventID=307)]]";
    const ERROR_NO_MORE_ITEMS: i32 = 259;

    #[link(name = "wevtapi")]
    unsafe extern "system" {
        fn EvtQuery(session: *mut c_void, path: *const u16, query: *const u16, flags: u32) -> *mut c_void;
        fn EvtNext(result: *mut c_void, count: u32, events: *mut *mut c_void,
                   timeout: u32, flags: u32, returned: *mut u32) -> i32;
        fn EvtRender(context: *mut c_void, event: *mut c_void, flags: u32,
                     size: u32, buffer: *mut c_void, used: *mut u32, props: *mut u32) -> i32;
        fn EvtOpenChannelConfig(session: *mut c_void, channel: *const u16, flags: u32) -> *mut c_void;
        fn EvtGetChannelConfigProperty(config: *mut c_void, property: i32, flags: u32,
                        size: u32, buffer: *mut c_void, used: *mut u32) -> i32;
        fn EvtClose(handle: *mut c_void) -> i32;
    }

    fn wide(s: &str) -> Vec<u16> {
        OsStr::new(s).encode_wide().chain(Some(0)).collect()
    }

    struct EvtHandle(*mut c_void);
    impl Drop for EvtHandle {
        fn drop(&mut self) { if !self.0.is_null() { unsafe { EvtClose(self.0); } } }
    }

    pub fn is_history_enabled() -> Result<bool, String> {
        let channel = wide(CHANNEL);
        let handle = EvtHandle(unsafe { EvtOpenChannelConfig(null_mut(), channel.as_ptr(), 0) });
        if handle.0.is_null() { return Err(io::Error::last_os_error().to_string()); }
        // EVT_VARIANT is a 16-byte, naturally aligned union + Count + Type structure.
        // Enabled is EvtChannelConfigEnabled (property id 0) and holds BoolVal.
        let mut data = [0u64; 4];
        let mut needed = 0u32;
        let ok = unsafe { EvtGetChannelConfigProperty(handle.0, 0, 0,
                std::mem::size_of_val(&data) as u32, data.as_mut_ptr().cast(), &mut needed) };
        if ok == 0 { return Err(io::Error::last_os_error().to_string()); }
        Ok((data[0] as u32) != 0)
    }

    fn render(event: *mut c_void) -> Result<String, String> {
        let mut size = 0u32;
        let mut count = 0u32;
        // The initial call obtains the required allocation size.
        unsafe { EvtRender(null_mut(), event, 1, 0, null_mut(), &mut size, &mut count) };
        if size < 2 || size > 4 * 1024 * 1024 {
            return Err("Invalid print event data size.".into());
        }
        let mut buffer = vec![0u16; size.div_ceil(2) as usize + 1];
        let ok = unsafe { EvtRender(null_mut(), event, 1,
                (buffer.len() * 2) as u32, buffer.as_mut_ptr().cast(), &mut size, &mut count) };
        if ok == 0 { return Err(io::Error::last_os_error().to_string()); }
        let len = buffer.iter().position(|unit| *unit == 0).unwrap_or(buffer.len());
        Ok(String::from_utf16_lossy(&buffer[..len]))
    }

    pub fn read_recent(max_events: usize) -> Result<Vec<PrintJob>, String> {
        let path = wide(CHANNEL);
        let query = wide(QUERY);
        let handle = EvtHandle(unsafe { EvtQuery(null_mut(), path.as_ptr(), query.as_ptr(), 0x201) });
        if handle.0.is_null() { return Err(io::Error::last_os_error().to_string()); }
        let mut jobs = Vec::new();
        for _ in 0..max_events.min(2000) {
            let mut event = null_mut();
            let mut returned = 0u32;
            let ok = unsafe { EvtNext(handle.0, 1, &mut event, 0, 0, &mut returned) };
            if ok == 0 {
                let error = io::Error::last_os_error();
                if error.raw_os_error() == Some(ERROR_NO_MORE_ITEMS) { break; }
                return Err(error.to_string());
            }
            if returned != 1 || event.is_null() { break; }
            let owned = EvtHandle(event);
            if let Ok(xml) = render(owned.0) {
                if let Some(job) = parse_event(&xml) { jobs.push(job); }
            }
        }
        Ok(jobs)
    }
}

#[cfg(windows)]
pub use native::{is_history_enabled, read_recent};

#[cfg(not(windows))]
pub fn is_history_enabled() -> Result<bool, String> { Ok(false) }
#[cfg(not(windows))]
pub fn read_recent(_limit: usize) -> Result<Vec<PrintJob>, String> { Ok(vec![]) }

#[cfg(test)]
mod tests {
    use super::*;
    const VALID: &str = r#"
    <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
      <System><EventID>307</EventID><EventRecordID>847</EventRecordID>
        <TimeCreated SystemTime="2026-11-15T10:25:46.0033441Z"/>
      </System><UserData>
        <DocumentPrinted><Param1>27</Param1><Param2>Receipt #27</Param2>
        <Param3>Cashier</Param3><Param5>VPrint - Kitchen</Param5>
        <Param7>2048</Param7><Param8>2</Param8></DocumentPrinted>
      </UserData>
    </Event>"#;
    #[test]
    fn parses_real_event_shape() {
        let job = parse_event(VALID).unwrap();
        assert_eq!(job.printer_name, "VPrint - Kitchen");
        assert_eq!(job.document_name, "Receipt #27");
        assert_eq!(job.pages, 2);
        assert_eq!(job.size_bytes, 2048);
    }
    #[test]
    fn ignores_unrelated_printers_and_bad_xml() {
        assert!(parse_event(&VALID.replace("VPrint - Kitchen", "Other printer")).is_none());
        assert!(parse_event("<bad xml").is_none());
    }
}
