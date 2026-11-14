using Microsoft.Data.Sqlite;
using VPrint.Models;

namespace VPrint.Services;

/// <summary>Local-only, append/deduplicate job metadata. Never stores printed document content.</summary>
public sealed class JobRepository
{
    private readonly string _connectionString;
    private readonly object _sync = new();

    public JobRepository(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new ArgumentException("Database path must include a directory.", nameof(path)));
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true };
        _connectionString = builder.ToString();

        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS jobs (
                    id TEXT PRIMARY KEY NOT NULL,
                    printer TEXT NOT NULL,
                    document TEXT NOT NULL,
                    user_name TEXT NOT NULL,
                    job_number INTEGER NOT NULL,
                    pages INTEGER NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    printed_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_jobs_printed_at ON jobs(printed_at DESC);
                """;
            command.ExecuteNonQuery();
        }
    }

    public bool Insert(PrintJob job)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO jobs
                (id, printer, document, user_name, job_number, pages, size_bytes, printed_at)
                VALUES ($id, $printer, $document, $user, $number, $pages, $size, $date)
                """;
            command.Parameters.AddWithValue("$id", job.Id);
            command.Parameters.AddWithValue("$printer", job.PrinterName);
            command.Parameters.AddWithValue("$document", job.DocumentName);
            command.Parameters.AddWithValue("$user", job.UserName);
            command.Parameters.AddWithValue("$number", job.JobNumber);
            command.Parameters.AddWithValue("$pages", job.Pages);
            command.Parameters.AddWithValue("$size", job.SizeBytes);
            command.Parameters.AddWithValue("$date", job.PrintedAt.UtcDateTime.ToString("O"));
            return command.ExecuteNonQuery() > 0;
        }
    }

    public IReadOnlyList<PrintJob> GetRecent(int limit = 750)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, printer, document, user_name, job_number, pages, size_bytes, printed_at
                FROM jobs ORDER BY printed_at DESC LIMIT $limit
                """;
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
            using var reader = command.ExecuteReader();
            var results = new List<PrintJob>();
            while (reader.Read())
                results.Add(new PrintJob(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5),
                    reader.GetInt64(6), DateTimeOffset.Parse(reader.GetString(7))));
            return results;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }
}
