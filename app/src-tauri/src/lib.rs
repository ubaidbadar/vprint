mod elevate;
mod events;
mod printers;
mod store;

use serde::Serialize;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Snapshot {
    printers: Vec<printers::Printer>,
    jobs: Vec<events::PrintJob>,
    total_count: i64,
    today_count: i64,
    history_enabled: bool,
    history_warning: Option<String>,
}

#[tauri::command]
async fn get_snapshot() -> Result<Snapshot, String> {
    tauri::async_runtime::spawn_blocking(|| {
        let printers = printers::list()?;
        let history_enabled = events::is_history_enabled().unwrap_or(false);
        let history_warning = if !history_enabled {
            Some("Windows PrintService history is disabled. Enable its Operational log once in Event Viewer to record completed jobs.".into())
        } else {
            match store::sync_events(500) {
                Ok(_) => None,
                Err(error) => Some(format!("Windows print history could not be read: {error}")),
            }
        };
        let history = store::load_history(500)?;
        Ok(Snapshot {
            printers, jobs: history.jobs,
            total_count: history.total_count,
            today_count: history.today_count,
            history_enabled,
            history_warning,
        })
    })
    .await
    .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn add_printer(label: String) -> Result<(), String> {
    let label = printers::label(&label)?;
    tauri::async_runtime::spawn_blocking(move || elevate::create(&label))
        .await
        .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn delete_printer(name: String) -> Result<(), String> {
    let name = printers::managed_name(&name)?;
    tauri::async_runtime::spawn_blocking(move || elevate::remove(&name))
        .await
        .map_err(|error| error.to_string())?
}

pub fn admin_command_if_requested() -> Option<i32> {
    elevate::admin_command_if_requested()
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![get_snapshot, add_printer, delete_printer])
        .run(tauri::generate_context!())
        .expect("Failed to start VPrint desktop application");
}
