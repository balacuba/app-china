using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChineseLearning.Sync;
using Microsoft.Data.Sqlite;

namespace ChineseLearningServer;

// Accounts, sign-in tokens and each account's progress, in one SQLite file.
// Passwords are kept only as salted PBKDF2 hashes and tokens only as SHA-256
// hashes, so the file never holds anything that signs someone in.
public sealed class Database(string path)
{
    private const int HashIterations = 210_000;
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public void Initialize()
    {
        using var connection = Open();
        Execute(connection, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserName TEXT NOT NULL UNIQUE COLLATE NOCASE,
                PasswordHash BLOB NOT NULL,
                Salt BLOB NOT NULL,
                CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Tokens (
                TokenHash BLOB PRIMARY KEY,
                UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                ExpiresAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Progress (
                UserId INTEGER PRIMARY KEY REFERENCES Users(Id) ON DELETE CASCADE,
                Version INTEGER NOT NULL,
                Document TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL);
            """);

        // Accounts signed in with Google carry Google's id for the account.
        // Added to databases made before Google sign-in existed.
        using var columns = connection.CreateCommand();
        columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = 'GoogleSub'";
        if ((long)columns.ExecuteScalar()! == 0)
        {
            Execute(connection, "ALTER TABLE Users ADD COLUMN GoogleSub TEXT");
        }

        Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS UsersByGoogleSub ON Users(GoogleSub) WHERE GoogleSub IS NOT NULL");

        // A learner's permission for this server to use Gemini for them: their
        // Google refresh token, encrypted for the Windows account the server runs as.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS GeminiGrants (
                UserId INTEGER PRIMARY KEY REFERENCES Users(Id) ON DELETE CASCADE,
                RefreshToken BLOB NOT NULL,
                UpdatedAt TEXT NOT NULL);
            """);
    }

    public void SaveGeminiGrant(long userId, string refreshToken)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GeminiGrants (UserId, RefreshToken, UpdatedAt) VALUES ($user, $token, $now)
            ON CONFLICT(UserId) DO UPDATE SET RefreshToken = $token, UpdatedAt = $now
            """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$token", Protect(refreshToken));
        command.Parameters.AddWithValue("$now", Now());
        command.ExecuteNonQuery();
    }

    // The learner's refresh token, or null when they have not allowed Gemini
    // (or it can no longer be read, e.g. the database moved to another PC).
    public string? LoadGeminiGrant(long userId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RefreshToken FROM GeminiGrants WHERE UserId = $user";
        command.Parameters.AddWithValue("$user", userId);
        if (command.ExecuteScalar() is not byte[] stored)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser)
                : stored);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void DeleteGeminiGrant(long userId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM GeminiGrants WHERE UserId = $user";
        command.Parameters.AddWithValue("$user", userId);
        command.ExecuteNonQuery();
    }

    // Encrypted with Windows (DPAPI) for the account the server runs as; the
    // server is built for Windows, elsewhere it is stored as it is.
    private static byte[] Protect(string text) => OperatingSystem.IsWindows()
        ? ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser)
        : Encoding.UTF8.GetBytes(text);

    // The account for a Google sign-in, made on first use with the Gmail
    // address as its name. It has no usable password: it signs in with Google.
    public (long Id, string UserName) FindOrCreateGoogleUser(GoogleIdentity identity)
    {
        using var connection = Open();
        using (var find = connection.CreateCommand())
        {
            find.CommandText = "SELECT Id, UserName FROM Users WHERE GoogleSub = $sub";
            find.Parameters.AddWithValue("$sub", identity.Subject);
            using var reader = find.ExecuteReader();
            if (reader.Read())
            {
                return (reader.GetInt64(0), reader.GetString(1));
            }
        }

        using var create = connection.CreateCommand();
        create.CommandText = """
            INSERT INTO Users (UserName, PasswordHash, Salt, CreatedAt, GoogleSub) VALUES ($name, $hash, $salt, $now, $sub)
            ON CONFLICT(UserName) DO UPDATE SET GoogleSub = $sub WHERE GoogleSub IS NULL
            RETURNING Id, UserName;
            """;
        create.Parameters.AddWithValue("$name", identity.Email);
        create.Parameters.AddWithValue("$hash", RandomNumberGenerator.GetBytes(32));
        create.Parameters.AddWithValue("$salt", RandomNumberGenerator.GetBytes(16));
        create.Parameters.AddWithValue("$now", Now());
        create.Parameters.AddWithValue("$sub", identity.Subject);
        using var created = create.ExecuteReader();
        return created.Read()
            ? (created.GetInt64(0), created.GetString(1))
            : throw new InvalidOperationException("Địa chỉ Gmail này đã gắn với một tài khoản Google khác.");
    }

    // Null when the name is taken.
    public long? CreateUser(string userName, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Users (UserName, PasswordHash, Salt, CreatedAt) VALUES ($name, $hash, $salt, $now)
            ON CONFLICT(UserName) DO NOTHING RETURNING Id;
            """;
        command.Parameters.AddWithValue("$name", userName);
        command.Parameters.AddWithValue("$hash", HashPassword(password, salt));
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$now", Now());
        return command.ExecuteScalar() is long id ? id : null;
    }

    // The account's id when the password is right, otherwise null.
    public long? CheckPassword(string userName, string password)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PasswordHash, Salt FROM Users WHERE UserName = $name";
        command.Parameters.AddWithValue("$name", userName);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            // Spend the same time as a real check, so timing does not reveal names.
            HashPassword(password, new byte[16]);
            return null;
        }

        var stored = (byte[])reader["PasswordHash"];
        var attempt = HashPassword(password, (byte[])reader["Salt"]);
        return CryptographicOperations.FixedTimeEquals(stored, attempt) ? reader.GetInt64(0) : null;
    }

    public string IssueToken(long userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM Tokens WHERE ExpiresAt < $now;
            INSERT INTO Tokens (TokenHash, UserId, ExpiresAt) VALUES ($hash, $user, $expires);
            """;
        command.Parameters.AddWithValue("$now", Now());
        command.Parameters.AddWithValue("$hash", HashToken(token));
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$expires", Format(DateTimeOffset.UtcNow + TokenLifetime));
        command.ExecuteNonQuery();
        return token;
    }

    // The signed-in account and its name, or null for an unknown or expired token.
    public (long Id, string UserName)? FindToken(string token)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Users.Id, Users.UserName FROM Tokens JOIN Users ON Users.Id = Tokens.UserId
            WHERE Tokens.TokenHash = $hash AND Tokens.ExpiresAt > $now
            """;
        command.Parameters.AddWithValue("$hash", HashToken(token));
        command.Parameters.AddWithValue("$now", Now());
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetString(1)) : null;
    }

    public void RevokeToken(string token)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Tokens WHERE TokenHash = $hash";
        command.Parameters.AddWithValue("$hash", HashToken(token));
        command.ExecuteNonQuery();
    }

    // Version 0 with an empty document for an account that has never synced.
    public ProgressEnvelope GetProgress(long userId)
    {
        using var connection = Open();
        return ReadProgress(connection, userId);
    }

    // Saves when `expectedVersion` is still current and returns the new version;
    // otherwise saves nothing and returns null (someone else synced first).
    public ProgressEnvelope? SaveProgress(long userId, int expectedVersion, ProgressDocument document)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = expectedVersion == 0
            ? """
              INSERT INTO Progress (UserId, Version, Document, UpdatedAt) VALUES ($user, 1, $doc, $now)
              ON CONFLICT(UserId) DO NOTHING
              """
            : """
              UPDATE Progress SET Version = Version + 1, Document = $doc, UpdatedAt = $now
              WHERE UserId = $user AND Version = $expected
              """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$expected", expectedVersion);
        command.Parameters.AddWithValue("$doc", JsonSerializer.Serialize(document, Json));
        command.Parameters.AddWithValue("$now", Now());
        return command.ExecuteNonQuery() == 1 ? new ProgressEnvelope(expectedVersion + 1, document) : null;
    }

    private static ProgressEnvelope ReadProgress(SqliteConnection connection, long userId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version, Document FROM Progress WHERE UserId = $user";
        command.Parameters.AddWithValue("$user", userId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ProgressEnvelope(reader.GetInt32(0), JsonSerializer.Deserialize<ProgressDocument>(reader.GetString(1), Json) ?? new())
            : new ProgressEnvelope(0, new ProgressDocument());
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, HashIterations, HashAlgorithmName.SHA256, 32);

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Now() => Format(DateTimeOffset.UtcNow);

    private static string Format(DateTimeOffset time) => time.UtcDateTime.ToString("O");
}
