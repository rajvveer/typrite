using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Typrite.Contracts;
using Typrite.Contracts.Delivery;
using Typrite.Player.Delivery;

namespace Typrite.LocalStore;

/// <summary>
/// Store-and-forward queue for delivery events. Bounded by count and age; drops are counted and persisted
/// so the device can report loss honestly instead of silently.
/// </summary>
public sealed class EventOutbox : IEventOutbox, IDisposable
{
    private readonly SqliteConnection _db;
    private readonly TimeProvider _time;
    private readonly int _maxCount;
    private readonly TimeSpan _maxAge;

    public EventOutbox(string path, TimeProvider time, int maxCount, TimeSpan maxAge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxCount, 0);
        _time = time;
        _maxCount = maxCount;
        _maxAge = maxAge;

        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _db.Open();
        Exec("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS queued_events (
                client_event_id TEXT PRIMARY KEY,
                enqueued_at TEXT NOT NULL,
                payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_queued_events_enqueued_at ON queued_events(enqueued_at);
            CREATE TABLE IF NOT EXISTS counters (name TEXT PRIMARY KEY, value INTEGER NOT NULL);
            INSERT OR IGNORE INTO counters(name, value) VALUES ('dropped', 0);
            """);
    }

    public int Count => Convert.ToInt32(Scalar("SELECT COUNT(*) FROM queued_events"), CultureInfo.InvariantCulture);

    public long DroppedCount => Convert.ToInt64(Scalar("SELECT value FROM counters WHERE name = 'dropped'"), CultureInfo.InvariantCulture);

    public void Enqueue(DeliveryEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using var tx = _db.BeginTransaction();
        using (var cmd = _db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO queued_events(client_event_id, enqueued_at, payload) VALUES ($id, $at, $payload)";
            cmd.Parameters.AddWithValue("$id", e.ClientEventId.ToString("N"));
            cmd.Parameters.AddWithValue("$at", Stamp(_time.GetUtcNow()));
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(e, ContractsJson.Options));
            cmd.ExecuteNonQuery();
        }

        using (var cap = _db.CreateCommand())
        {
            cap.Transaction = tx;
            cap.CommandText = """
                DELETE FROM queued_events WHERE rowid IN (
                    SELECT rowid FROM queued_events ORDER BY enqueued_at, rowid
                    LIMIT MAX(0, (SELECT COUNT(*) FROM queued_events) - $max));
                UPDATE counters SET value = value + changes() WHERE name = 'dropped';
                """;
            cap.Parameters.AddWithValue("$max", _maxCount);
            cap.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public IReadOnlyList<DeliveryEvent> PeekBatch(int max)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM queued_events ORDER BY enqueued_at, rowid LIMIT $max";
        cmd.Parameters.AddWithValue("$max", max);
        using var reader = cmd.ExecuteReader();
        var result = new List<DeliveryEvent>();
        while (reader.Read())
        {
            result.Add(JsonSerializer.Deserialize<DeliveryEvent>(reader.GetString(0), ContractsJson.Options)!);
        }

        return result;
    }

    public void Acknowledge(IEnumerable<Guid> clientEventIds)
    {
        ArgumentNullException.ThrowIfNull(clientEventIds);
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM queued_events WHERE client_event_id = $id";
        var p = cmd.Parameters.Add("$id", SqliteType.Text);
        foreach (var id in clientEventIds)
        {
            p.Value = id.ToString("N");
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <returns>Number of events dropped because they were older than the retention window.</returns>
    public int PruneExpired()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            DELETE FROM queued_events WHERE enqueued_at < $cutoff;
            UPDATE counters SET value = value + changes() WHERE name = 'dropped';
            SELECT changes();
            """;
        cmd.Parameters.AddWithValue("$cutoff", Stamp(_time.GetUtcNow() - _maxAge));
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Dispose() => _db.Dispose();

    private static string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private object Scalar(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()!;
    }
}
