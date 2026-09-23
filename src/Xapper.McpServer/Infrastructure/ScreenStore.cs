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

        AddRegionKeyColumnIfMissing();
    }

    /// <summary>
    /// 이미 만들어져 있는 데이터베이스에 region_key 열을 붙인다. 새로 만든 것에는 CREATE 문에 없으므로 여기서 함께 붙인다.
    ///
    /// 이 열이 필요한 이유는 지문 하나가 화면 하나를 뜻하지 않기 때문이다. 같은 화면에서 행을 고르거나 목록을
    /// 펼치면 인라인 편집기 같은 요소가 트리에 나타나 지문이 갈린다(실측: 같은 최적화 화면이 지문 셋으로 갈렸고,
    /// 셋의 영역 열일곱 개가 완전히 같았다). 그러면 같은 화면을 세 번 배우고 알아낸 것이 셋으로 쪼개진다.
    /// 영역 셀렉터의 집합은 그런 상태 변화에 흔들리지 않으므로, 지문이 빗나갔을 때 기댈 두 번째 열쇠가 된다.
    /// </summary>
    private void AddRegionKeyColumnIfMissing()
    {
        var present = false;
        using (var columns = _connection.CreateCommand())
        {
            columns.CommandText = "PRAGMA table_info(screens);";
            using var reader = columns.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "region_key", StringComparison.Ordinal))
                    present = true;
            }
        }

        if (!present)
            Execute("ALTER TABLE screens ADD COLUMN region_key TEXT;");

        Execute("CREATE INDEX IF NOT EXISTS idx_screens_region_key ON screens(region_key);");

        BackfillRegionKeys();
        MergeRecordsSharingRegions();
    }

    /// <summary>
    /// 열쇠가 비어 있는 기존 기록에 영역 열쇠를 채운다.
    /// 채우지 않으면 예전에 배운 화면은 두 번째 길로 영영 찾아지지 않는다.
    /// </summary>
    private void BackfillRegionKeys()
    {
        var pending = new List<string>();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT signature FROM screens WHERE region_key IS NULL;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                pending.Add(reader.GetString(0));
        }

        foreach (var signature in pending)
        {
            var key = RegionKeyOf(SelectorsOf(signature).Select(selector => new ScreenRegion
            {
                Type = "",
                Selector = selector
            }));

            if (key is null)
                continue;

            using var update = _connection.CreateCommand();
            update.CommandText = "UPDATE screens SET region_key = $key WHERE signature = $signature;";
            update.Parameters.AddWithValue("$key", key);
            update.Parameters.AddWithValue("$signature", signature);
            update.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 같은 영역 집합을 가진 기록이 여럿이면 하나로 합친다.
    ///
    /// 열쇠를 더하는 것만으로는 이미 갈라진 기록이 붙지 않는다. 앞으로만 막고 지난 것을 두면, 알아낸 사실이
    /// 쪼개진 채로 남아 어느 쪽으로 들어오느냐에 따라 절반만 읽힌다(실측: 최적화 화면 하나가 셋으로 갈렸고
    /// 팝업을 스냅샷으로 읽을 수 있다는 사실이 그중 하나에만 있었다).
    ///
    /// 가장 최근에 본 줄을 남기고 나머지의 비고를 잇는다. 같은 줄은 한 번만 남긴다.
    /// </summary>
    private void MergeRecordsSharingRegions()
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT region_key, signature FROM screens
                 WHERE region_key IS NOT NULL
                 ORDER BY last_seen_at DESC;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                if (!groups.TryGetValue(key, out var members))
                    groups[key] = members = [];

                members.Add(reader.GetString(1));
            }
        }

        foreach (var members in groups.Values.Where(members => members.Count > 1))
            MergeInto(members[0], members.Skip(1).ToList());
    }

    /// <summary>남길 기록에 나머지의 비고를 잇고 나머지를 지운다.</summary>
    private void MergeInto(string keep, List<string> drop)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var signature in drop.Prepend(keep))
        {
            foreach (var line in (NotesOf(signature) ?? "").Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && seen.Add(trimmed))
                    lines.Add(trimmed);
            }
        }

        if (lines.Count > 0)
            ReplaceNotes(keep, string.Join('\n', lines));

        foreach (var signature in drop)
        {
            using var delete = _connection.CreateCommand();
            delete.CommandText = "DELETE FROM screens WHERE signature = $signature;";
            delete.Parameters.AddWithValue("$signature", signature);
            delete.ExecuteNonQuery();
        }
    }

    /// <summary>한 화면에 저장된 셀렉터 목록.</summary>
    private List<string> SelectorsOf(string signature)
    {
        var selectors = new List<string>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT selector FROM regions WHERE signature = $signature AND selector IS NOT NULL;";
        command.Parameters.AddWithValue("$signature", signature);

        using var reader = command.ExecuteReader();
        while (reader.Read())
            selectors.Add(reader.GetString(0));

        return selectors;
    }

    /// <summary>한 화면의 비고. 없으면 null.</summary>
    private string? NotesOf(string signature)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT notes FROM screens WHERE signature = $signature;";
        command.Parameters.AddWithValue("$signature", signature);

        return command.ExecuteScalar() as string;
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
    public string Save(ScreenRecord record)
    {
        if (record is null)
            throw new ArgumentNullException(nameof(record));

        using var transaction = _connection.BeginTransaction();

        var regionKey = RegionKeyOf(record.Regions);

        // 같은 영역 집합을 가진 기록이 이미 있으면 그 줄에 쓴다. 지문만 보고 새로 만들면 같은 화면이 상태에
        // 따라 여러 줄로 갈라지고, 알아낸 것이 그만큼 쪼개진다(실측: 최적화 화면 하나가 셋으로 갈렸다).
        var target = regionKey is null ? null : ExistingSignatureFor(regionKey, record.Signature);
        if (target is not null)
            record.Signature = target;

        UpsertScreen(record, regionKey);
        ReplaceRegions(record);
        EnforceCap(record.App);

        transaction.Commit();
        return record.Signature;
    }

    /// <summary>같은 영역 열쇠를 가진 다른 기록의 지문. 없으면 null.</summary>
    private string? ExistingSignatureFor(string regionKey, string signature)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT signature FROM screens
             WHERE region_key = $key AND signature <> $signature
             ORDER BY last_seen_at DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$key", regionKey);
        command.Parameters.AddWithValue("$signature", signature);

        return command.ExecuteScalar() as string;
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

    /// <summary>
    /// 이미 있는 기록의 비고에 한 줄을 덧붙인다.
    /// 다시 배우는 길로 비고를 남기면 그때 쓴 것만 남고 앞의 것이 사라지는데, 화면에 들어설 때 아는 사실보다
    /// 눌러 보고 나서야 아는 사실이 더 값지다. 그래서 영역은 건드리지 않고 비고만 잇는 길을 따로 둔다.
    /// </summary>
    /// <param name="signature">비고를 붙일 화면의 지문.</param>
    /// <param name="note">덧붙일 한 줄.</param>
    /// <returns>그 지문의 기록이 있어 붙였으면 true.</returns>
    public bool AppendNote(string signature, string note)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE screens
               SET notes = CASE
                     WHEN notes IS NULL OR notes = '' THEN $note
                     ELSE notes || char(10) || $note
                   END
             WHERE signature = $signature;
            """;
        command.Parameters.AddWithValue("$note", note);
        command.Parameters.AddWithValue("$signature", signature);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 이미 있는 기록의 비고를 통째로 새 것으로 갈아 끼운다.
    /// 덧붙이기만 있으면 틀린 줄을 바로잡을 길이 없어, 한 번 잘못 적힌 사실이 계속 읽히며 다음 판단을 흐린다.
    /// </summary>
    /// <param name="signature">비고를 갈아 끼울 화면의 지문.</param>
    /// <param name="notes">새 비고 전체.</param>
    /// <returns>그 지문의 기록이 있어 바꿨으면 true.</returns>
    public bool ReplaceNotes(string signature, string notes)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE screens SET notes = $notes WHERE signature = $signature;";
        command.Parameters.AddWithValue("$notes", notes);
        command.Parameters.AddWithValue("$signature", signature);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 영역 목록에서 두 번째 열쇠를 만든다. 셀렉터만 정렬해 쓰며, 하나도 없으면 null 이다.
    ///
    /// 좌표를 넣지 않는 이유는 창 크기나 배율이 조금만 달라도 값이 흔들리기 때문이다. 이름 있는 컨트롤의
    /// 집합은 그런 것에 흔들리지 않으면서도 화면을 충분히 가른다.
    /// </summary>
    /// <param name="regions">화면의 영역 목록.</param>
    public static string? RegionKeyOf(IEnumerable<ScreenRegion> regions)
    {
        var selectors = regions
            .Select(region => region.Selector)
            .Where(selector => !string.IsNullOrWhiteSpace(selector))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(selector => selector, StringComparer.Ordinal)
            .ToList();

        if (selectors.Count == 0)
            return null;

        var joined = string.Join('\n', selectors);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// 같은 영역 집합을 가진 기록을 찾는다. 지문이 빗나갔을 때 기대는 두 번째 길이다.
    /// 둘 이상이면 가장 최근에 본 것을 돌려준다.
    /// </summary>
    /// <param name="regionKey">찾을 영역 열쇠.</param>
    /// <returns>기록. 없으면 null.</returns>
    public ScreenRecord? FindByRegionKey(string regionKey)
    {
        string? signature;
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                "SELECT signature FROM screens WHERE region_key = $key ORDER BY last_seen_at DESC LIMIT 1;";
            command.Parameters.AddWithValue("$key", regionKey);
            signature = command.ExecuteScalar() as string;
        }

        return signature is null ? null : Find(signature);
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
    private void UpsertScreen(ScreenRecord record, string? regionKey)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO screens (signature, app, name, notes, learned_at, last_seen_at, seen_count, region_key)
            VALUES ($signature, $app, $name, $notes, $now, $now, 1, $regionKey)
            ON CONFLICT(signature) DO UPDATE SET
              app = excluded.app,
              name = excluded.name,
              notes = excluded.notes,
              last_seen_at = excluded.last_seen_at,
              region_key = excluded.region_key;
            """;
        command.Parameters.AddWithValue("$regionKey", (object?)regionKey ?? DBNull.Value);
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
            // 텍스트는 적지 않는다. 배울 때의 값이라 데이터가 바뀌면 거짓이 되고, 조회는 언제나 지금 화면에서
            // 다시 읽어 채우므로 저장된 값이 쓰일 자리가 없다.
            insert.Parameters.AddWithValue("$text", DBNull.Value);
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
