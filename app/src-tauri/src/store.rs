//! Compatible with the local SQLite metadata database used in VPrint 0.1.
use crate::events::PrintJob;
use rusqlite::{params, Connection};
use serde::Serialize;
use std::{fs, path::PathBuf, time::Duration};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct History {
    pub jobs: Vec<PrintJob>,
    pub total_count: i64,
    pub today_count: i64,
}

fn path() -> Result<PathBuf, String> {
    let base = std::env::var_os("LOCALAPPDATA")
        .ok_or("Windows LOCALAPPDATA path is not available.")?;
    let folder = PathBuf::from(base).join("VPrint");
    fs::create_dir_all(&folder).map_err(|err| err.to_string())?;
    Ok(folder.join("jobs.sqlite3"))
}

fn open() -> Result<Connection, String> {
    let mut db = Connection::open(path()?).map_err(|err| err.to_string())?;
    db.busy_timeout(Duration::from_secs(3)).map_err(|err| err.to_string())?;
    db.execute_batch(
        "CREATE TABLE IF NOT EXISTS jobs(
          id TEXT PRIMARY KEY NOT NULL,
          printer TEXT NOT NULL,
          document TEXT NOT NULL,
          user_name TEXT NOT NULL,
          job_number INTEGER NOT NULL,
          pages INTEGER NOT NULL,
          size_bytes INTEGER NOT NULL,
          printed_at TEXT NOT NULL);
         CREATE INDEX IF NOT EXISTS ix_jobs_printed_at ON jobs(printed_at DESC);"
    ).map_err(|err| err.to_string())?;
    Ok(db)
}

pub fn sync_events(limit: usize) -> Result<usize, String> {
    let events = crate::events::read_recent(limit)?;
    if events.is_empty() { return Ok(0); }
    let mut db = open()?;
    let transaction = db.transaction().map_err(|err| err.to_string())?;
    let mut inserted = 0;
    {
        let mut statement = transaction.prepare_cached(
            "INSERT OR IGNORE INTO jobs
              (id, printer, document, user_name, job_number, pages, size_bytes, printed_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)"
        ).map_err(|err| err.to_string())?;
        for job in &events {
            inserted += statement.execute(params![
                job.id, job.printer_name, job.document_name, job.user_name,
                job.job_number, job.pages, job.size_bytes, job.printed_at
            ]).map_err(|err| err.to_string())?;
        }
    }
    transaction.commit().map_err(|err| err.to_string())?;
    Ok(inserted)
}

pub fn load_history(limit: usize) -> Result<History, String> {
    let db = open()?;
    let mut statement = db.prepare(
        "SELECT id, printer, document, user_name, job_number, pages, size_bytes, printed_at
         FROM jobs ORDER BY printed_at DESC LIMIT ?1"
    ).map_err(|err| err.to_string())?;
    let rows = statement.query_map([limit.clamp(1, 1000) as i64], |row| {
        Ok(PrintJob {
            id: row.get(0)?,
            printer_name: row.get(1)?,
            document_name: row.get(2)?,
            user_name: row.get(3)?,
            job_number: row.get(4)?,
            pages: row.get(5)?,
            size_bytes: row.get(6)?,
            printed_at: row.get(7)?,
        })
    }).map_err(|err| err.to_string())?;
    let jobs: Vec<PrintJob> = rows.collect::<Result<Vec<_>, _>>()
        .map_err(|err| err.to_string())?;
    let total_count: i64 = db.query_row("SELECT COUNT(*) FROM jobs", [], |r| r.get(0))
        .map_err(|err| err.to_string())?;
    let today_count: i64 = db.query_row(
        "SELECT COUNT(*) FROM jobs WHERE date(printed_at,'localtime') = date('now','localtime')",
        [], |r| r.get(0)
    ).map_err(|err| err.to_string())?;
    Ok(History { jobs, total_count, today_count })
}
