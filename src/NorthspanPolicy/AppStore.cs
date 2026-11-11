using System.Net.Mail;
using Microsoft.Data.Sqlite;

namespace NorthspanPolicy;

/// <summary>
/// Small SQLite store for the launch waitlist and a daily count of AI checks (to cap API spend).
/// Uploaded policies are never stored here or anywhere else.
/// </summary>
public sealed class AppStore
{
    private readonly string _connectionString;

    public AppStore(IConfiguration config)
    {
        var path = config["Data:Path"] ?? Path.Combine(AppContext.BaseDirectory, "data", "policy.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Initialize();
    }

    private void Initialize()
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS waitlist (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                email TEXT NOT NULL,
                organization TEXT,
                interest TEXT NOT NULL,
                marketing_ok INTEGER NOT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_waitlist_email_interest ON waitlist(email, interest);
            CREATE TABLE IF NOT EXISTS check_usage (
                day TEXT PRIMARY KEY,
                count INTEGER NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var con = new SqliteConnection(_connectionString);
        con.Open();
        return con;
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        try { return new MailAddress(email.Trim()).Address == email.Trim(); }
        catch (FormatException) { return false; }
    }

    /// <summary>Adds someone to the launch list. Returns false if they were already on it for this product.</summary>
    public bool AddToWaitlist(string email, string? organization, string interest, bool marketingOk)
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO waitlist (email, organization, interest, marketing_ok, created_utc)
            VALUES ($email, $org, $interest, $mk, $created);
            """;
        cmd.Parameters.AddWithValue("$email", email.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$org", (object?)organization?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$interest", interest);
        cmd.Parameters.AddWithValue("$mk", marketingOk ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        return cmd.ExecuteNonQuery() == 1;
    }

    public int WaitlistCount()
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM waitlist;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Records one AI check for today if under the daily limit. Returns false when the limit is reached.</summary>
    public bool TryRecordCheck(int dailyLimit)
    {
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        using var con = Open();
        using var tx = con.BeginTransaction();
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT count FROM check_usage WHERE day = $day;";
        cmd.Parameters.AddWithValue("$day", day);
        var current = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        if (current >= dailyLimit) { tx.Rollback(); return false; }

        cmd.CommandText = """
            INSERT INTO check_usage (day, count) VALUES ($day, 1)
            ON CONFLICT(day) DO UPDATE SET count = count + 1;
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();
        return true;
    }
}
