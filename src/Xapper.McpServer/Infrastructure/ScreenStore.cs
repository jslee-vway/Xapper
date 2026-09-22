using Microsoft.Data.Sqlite;

namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 화면 기록이 사는 곳. 지문 하나에 기록 하나를 걸어 두고, 지문으로 정확히 같은 것을 찾아 준다.
///
/// 파일 하나에 SQLite 로 담는 이유는 이 서버가 자주 죽었다 살아나기 때문이다. MCP 클라이언트는 서버 프로세스를
/// 수시로 다시 띄우고, 배포할 때마다 쓰는 도중에 끊긴다. 평범한 JSON 파일이라면 두 프로세스가 같이 쓸 때 깨지고
/// 쓰다 죽으면 절반만 적힌 파일이 남는다. 여기서 필요한 것이 잠금과 원자적 커밋이라 데이터베이스를 쓴다.
///
/// 시간이 지났다고 지우지는 않는다. 앱이 바뀌면 지문도 함께 바뀌어 낡은 기록은 조회에 걸리지 않고 묻히므로,
/// 날짜로 지우면 여전히 맞는 정보까지 같이 버리게 된다. 대신 같은 지문이면 덮어쓰고, 앱 단위로 비우는 길을 두고,
/// 앱마다 개수 상한을 안전망으로 둔다.
/// </summary>
public sealed class ScreenStore : IDisposable
{
    #region Fields

    /// <summary>열어 둔 데이터베이스 연결. 저장소 수명 동안 하나만 쓴다.</summary>
    private readonly SqliteConnection _connection;

    /// <summary>앱 하나가 가질 수 있는 기록 수의 상한. 넘으면 가장 오래 안 쓰인 것부터 밀어낸다.</summary>
    private readonly int _maxPerApp;

    #endregion

    #region Constructor

    /// <summary>
    /// 데이터베이스를 열고 필요하면 만든다. 파일이 들어갈 폴더가 없으면 먼저 만들고,
    /// 표가 없으면 새로 만들며, 화면을 지울 때 그 영역까지 함께 지워지도록 외래 키 검사를 켠다.
    /// </summary>
    /// <param name="databasePath">데이터베이스 파일 경로.</param>
    /// <param name="maxPerApp">앱당 보관할 기록 수의 상한.</param>
    public ScreenStore(string databasePath, int maxPerApp = 200)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("A database path is required.", nameof(databasePath));

        var folder = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        _maxPerApp = maxPerApp;
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        _connection.Open();

        Execute("""
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS screens (
              signature     TEXT PRIMARY KEY,
              app           TEXT NOT NULL,
              name          TEXT NOT NULL,
              notes         TEXT,
              learned_at    TEXT NOT NULL,
              last_seen_at  TEXT NOT NULL,
              seen_count    INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS regions (
              signature  TEXT NOT NULL REFERENCES screens(signature) ON DELETE CASCADE,
              ordinal    INTEGER NOT NULL,
              type       TEXT NOT NULL,
              selector   TEXT,
              anchor     TEXT,
              anchor_x   REAL,
              anchor_y   REAL,
              text       TEXT,
              PRIMARY KEY (signature, ordinal)
            );
            """);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 지문으로 기록을 찾는다. 찾으면 쓰인 흔적(마지막으로 본 시각과 횟수)을 올리고,
    /// 올린 뒤의 값이 담긴 기록을 돌려준다. 무엇이 실제로 쓰이는지가 상한을 적용할 때의 기준이 된다.
    /// </summary>
    /// <param name="signature">찾을 화면의 지문.</param>
    /// <returns>기록. 그 지문으로 저장된 것이 없으면 null.</returns>
    public ScreenRecord? Find(string signature)
    {
        using var transaction = _connection.BeginTransaction();

        using (var touch = _connection.CreateCommand())
        {
            touch.CommandText =
                "UPDATE screens SET seen_count = seen_count + 1, last_seen_at = $now WHERE signature = $signature;";
            touch.Parameters.AddWithValue("$now", Now());
            touch.Parameters.AddWithValue("$signature", signature);
            if (touch.ExecuteNonQuery() == 0)
                return null;
        }

        var record = ReadScreen(signature);
        if (record is null)
            return null;

        ReadRegions(record);
        transaction.Commit();
        return record;
    }

    /// <summary>
    /// 기록을 저장한다. 같은 지문이 이미 있으면 이름·메모·영역을 새 것으로 갈아 끼우되 처음 기록한 시각은 그대로 둔다.
    /// 영역은 지우고 다시 넣으므로 예전 영역이 남아 섞이지 않는다.
    /// 마지막으로 그 앱의 기록 수가 상한을 넘으면 가장 오래 안 쓰인 것부터 밀어낸다.
    /// 화면만 있고 영역이 없는 반쪽 상태가 남지 않도록 전부 한 트랜잭션으로 묶는다.
    /// </summary>
    /// <param name="record">저장할 기록.</param>
    public void Save(ScreenRecord record)
    {
        if (record is null)
            throw new ArgumentNullException(nameof(record));

        using var transaction = _connection.BeginTransaction();

        UpsertScreen(record);
        ReplaceRegions(record);
        EnforceCap(record.App);

        transaction.Commit();
    }

    /// <summary>
    /// 한 앱의 기록을 모두 지운다. 앱을 새로 빌드해 기록이 통째로 쓸모없어졌다는 것은 사람만 알 수 있으므로,
    /// 저장소가 알아서 지우는 대신 이 길을 열어 둔다. 영역은 외래 키를 타고 함께 지워진다.
    /// </summary>
    /// <param name="app">비울 앱의 프로세스 이름.</param>
    /// <returns>지워진 화면 수.</returns>
    public int Forget(string app)
    {
        using var transaction = _connection.BeginTransaction();

        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM screens WHERE app = $app;";
        command.Parameters.AddWithValue("$app", app);
        var removed = command.ExecuteNonQuery();

        transaction.Commit();
        return removed;
    }

    /// <summary>데이터베이스 연결을 닫는다.</summary>
    public void Dispose() => _connection.Dispose();

    #endregion

    #region Private Methods

    /// <summary>
    /// 지금 시각을 문자열로 만든다. 왕복해도 값이 살아남는 형식(라운드트립)으로 적어 100나노초까지 남기는데,
    /// 상한을 적용할 때 마지막으로 본 시각의 앞뒤를 가려야 하므로 두 기록이 같은 값을 갖지 않아야 하기 때문이다.
    /// </summary>
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    /// <summary>파라미터 없는 문장을 그대로 실행한다. 스키마를 만들 때만 쓴다.</summary>
    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>화면 줄을 넣거나, 이미 있으면 갱신한다. 처음 기록한 시각과 조회 횟수는 건드리지 않는다.</summary>
    private void UpsertScreen(ScreenRecord record)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO screens (signature, app, name, notes, learned_at, last_seen_at, seen_count)
            VALUES ($signature, $app, $name, $notes, $now, $now, 1)
            ON CONFLICT(signature) DO UPDATE SET
              app = excluded.app,
              name = excluded.name,
              notes = excluded.notes,
              last_seen_at = excluded.last_seen_at;
            """;
        command.Parameters.AddWithValue("$signature", record.Signature);
        command.Parameters.AddWithValue("$app", record.App);
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$notes", (object?)record.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Now());
        command.ExecuteNonQuery();
    }

    /// <summary>그 화면의 영역을 전부 지우고 새 목록을 순서대로 다시 넣는다.</summary>
    private void ReplaceRegions(ScreenRecord record)
    {
        using (var clear = _connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM regions WHERE signature = $signature;";
            clear.Parameters.AddWithValue("$signature", record.Signature);
            clear.ExecuteNonQuery();
        }

        for (var ordinal = 0; ordinal < record.Regions.Count; ordinal++)
        {
            var region = record.Regions[ordinal];

            using var insert = _connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO regions (signature, ordinal, type, selector, anchor, anchor_x, anchor_y, text)
                VALUES ($signature, $ordinal, $type, $selector, $anchor, $anchorX, $anchorY, $text);
                """;
            insert.Parameters.AddWithValue("$signature", record.Signature);
            insert.Parameters.AddWithValue("$ordinal", ordinal);
            insert.Parameters.AddWithValue("$type", region.Type);
            insert.Parameters.AddWithValue("$selector", (object?)region.Selector ?? DBNull.Value);
            insert.Parameters.AddWithValue("$anchor", (object?)region.Anchor ?? DBNull.Value);
            insert.Parameters.AddWithValue("$anchorX", (object?)region.AnchorX ?? DBNull.Value);
            insert.Parameters.AddWithValue("$anchorY", (object?)region.AnchorY ?? DBNull.Value);
            insert.Parameters.AddWithValue("$text", (object?)region.Text ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }
    }

    /// <summary>한 앱의 기록이 상한을 넘으면, 마지막으로 본 시각이 가장 이른 것부터 상한만큼만 남기고 지운다.</summary>
    private void EnforceCap(string app)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            DELETE FROM screens WHERE signature IN (
              SELECT signature FROM screens WHERE app = $app
              ORDER BY last_seen_at DESC
              LIMIT -1 OFFSET $max
            );
            """;
        command.Parameters.AddWithValue("$app", app);
        command.Parameters.AddWithValue("$max", _maxPerApp);
        command.ExecuteNonQuery();
    }

    /// <summary>화면 줄 하나를 읽어 기록으로 옮긴다. 영역은 채우지 않는다.</summary>
    private ScreenRecord? ReadScreen(string signature)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT app, name, notes, learned_at, seen_count FROM screens WHERE signature = $signature;";
        command.Parameters.AddWithValue("$signature", signature);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new ScreenRecord
        {
            Signature = signature,
            App = reader.GetString(0),
            Name = reader.GetString(1),
            Notes = reader.IsDBNull(2) ? null : reader.GetString(2),
            LearnedAt = reader.IsDBNull(3) ? null : reader.GetString(3),
            SeenCount = reader.GetInt32(4)
        };
    }

    /// <summary>그 화면의 영역을 저장한 순서 그대로 읽어 기록에 채운다.</summary>
    private void ReadRegions(ScreenRecord record)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT type, selector, anchor, anchor_x, anchor_y, text
            FROM regions WHERE signature = $signature ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$signature", record.Signature);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            record.Regions.Add(new ScreenRegion
            {
                Type = reader.GetString(0),
                Selector = reader.IsDBNull(1) ? null : reader.GetString(1),
                Anchor = reader.IsDBNull(2) ? null : reader.GetString(2),
                AnchorX = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                AnchorY = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                Text = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
    }

    #endregion
}
