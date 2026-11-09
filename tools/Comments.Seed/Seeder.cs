using System.Data;
using System.Text;
using Bogus;
using Microsoft.Data.SqlClient;

namespace Comments.Seed;

/// <summary>
/// Generates a realistic data set and writes it with SqlBulkCopy (explicit ids, constraints checked so they stay trusted).
/// <para>
/// Shape: a share of comments start threads. Replies go 3% to twenty popular threads (about a thousand replies each,
/// so thread paging is exercised), 80% to recent threads with a strong bias to the newest, and the rest anywhere.
/// Within a thread a reply answers the root or a random earlier reply, nesting up to 12 levels. Dates grow with ids over
/// the last year, so id order equals date order as in production.
/// </para>
/// </summary>
internal sealed class Seeder(SeedOptions options)
{
    private const int BatchSize = 50_000;
    private const int MaxDepth = 12;
    private const int HotThreads = 2_000;
    private const int PopularThreads = 20;

    private readonly Random _random = new(2026);
    private readonly Faker _english = new("en") { Random = new Randomizer(2026) };

    public async Task<(int Users, int Comments)> RunAsync()
    {
        await using var connection = new SqlConnection(options.SqlConnectionString);
        await connection.OpenAsync();

        var users = await InsertUsersAsync(connection);
        await InsertCommentsAsync(connection, users);
        await FinishAsync(connection);
        return (users.Count, options.Comments);
    }

    private async Task<List<SeedUser>> InsertUsersAsync(SqlConnection connection)
    {
        var firstId = (int)await NextIdAsync(connection, "Users");
        var now = DateTimeOffset.UtcNow;
        var users = new List<SeedUser>(options.Users);
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("UserName", typeof(string));
        table.Columns.Add("Email", typeof(string));
        table.Columns.Add("HomePage", typeof(string));
        table.Columns.Add("CreatedAt", typeof(DateTimeOffset));
        table.Columns.Add("LastSeenAt", typeof(DateTimeOffset));

        for (var i = 0; i < options.Users; i++)
        {
            var id = firstId + i;
            var name = LatinName(_english.Internet.UserName());
            var email = $"{name.ToLowerInvariant()}.{id}@example.com";
            var homePage = _random.NextDouble() < 0.2 ? $"https://example.com/{name.ToLowerInvariant()}" : null;
            users.Add(new SeedUser(id, name, email, homePage));
            table.Rows.Add(id, name, email, (object?)homePage ?? DBNull.Value, now.AddDays(-365), now);
        }

        await BulkCopyAsync(connection, "Users", table);
        Console.WriteLine($"Users: {users.Count:N0}");
        return users;
    }

    private async Task InsertCommentsAsync(SqlConnection connection, List<SeedUser> users)
    {
        var firstId = await NextIdAsync(connection, "Comments");
        var start = DateTimeOffset.UtcNow.AddDays(-365);
        var step = TimeSpan.FromDays(365).Ticks / Math.Max(1, options.Comments);
        var roots = new List<long>();
        var popular = new List<long>();
        var popularEvery = Math.Max(1, (int)(options.Comments * options.TopLevelShare / PopularThreads));
        var threads = new Dictionary<long, List<(long Id, int Depth)>>();
        var table = CreateCommentTable();

        for (var i = 0; i < options.Comments; i++)
        {
            var id = firstId + i;
            var user = users[_random.Next(users.Count)];
            long? parentId = null;
            long? rootId = null;
            var depth = 0;

            if (roots.Count == 0 || _random.NextDouble() < options.TopLevelShare)
            {
                roots.Add(id);
                threads[id] = [];
                if (popular.Count < PopularThreads && (roots.Count - 1) % popularEvery == 0)
                {
                    popular.Add(id);
                }
            }
            else
            {
                var roll = _random.NextDouble();
                var root = roll switch
                {
                    < 0.03 when popular.Count > 0 => popular[_random.Next(popular.Count)],
                    < 0.83 => roots[roots.Count - 1 - (int)(Math.Min(HotThreads, roots.Count) * Math.Pow(_random.NextDouble(), 3))],
                    _ => roots[_random.Next(roots.Count)],
                };
                var members = threads[root];
                var target = members.Count == 0 || _random.NextDouble() < 0.4 ? (Id: root, Depth: 0) : members[_random.Next(members.Count)];
                if (target.Depth >= MaxDepth)
                {
                    target = (root, 0);
                }

                parentId = target.Id;
                rootId = root;
                depth = target.Depth + 1;
                members.Add((id, depth));
            }

            table.Rows.Add(
                id,
                (object?)parentId ?? DBNull.Value,
                (object?)rootId ?? DBNull.Value,
                depth,
                user.Id,
                user.UserName,
                user.Email,
                (object?)user.HomePage ?? DBNull.Value,
                Text(),
                start.AddTicks(step * i),
                0,
                $"10.{_random.Next(256)}.{_random.Next(256)}.{_random.Next(1, 255)}",
                "Comments.Seed");

            if (table.Rows.Count == BatchSize)
            {
                await BulkCopyAsync(connection, "Comments", table);
                table.Clear();
                Console.WriteLine($"Comments: {i + 1:N0} / {options.Comments:N0}");
            }
        }

        if (table.Rows.Count > 0)
        {
            await BulkCopyAsync(connection, "Comments", table);
        }

        Console.WriteLine($"Threads: {roots.Count:N0}, replies: {options.Comments - roots.Count:N0}");
    }

    /// <summary>Recomputes every thread's reply count in one statement and refreshes statistics for the new data.</summary>
    private static async Task FinishAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 600;
        command.CommandText = """
            UPDATE r SET r.ReplyCount = x.Cnt
            FROM [Comments] r
            JOIN (SELECT [RootId], COUNT(*) AS Cnt FROM [Comments] WHERE [RootId] IS NOT NULL GROUP BY [RootId]) x ON x.[RootId] = r.[Id];
            UPDATE STATISTICS [Comments];
            UPDATE STATISTICS [Users];
            """;
        await command.ExecuteNonQueryAsync();
    }

    private string Text()
    {
        var faker = _english;
        var text = new StringBuilder();
        foreach (var word in faker.Lorem.Words(_random.Next(8, 60)))
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            var roll = _random.NextDouble();
            text.Append(roll switch
            {
                < 0.02 => $"<strong>{word}</strong>",
                < 0.04 => $"<i>{word}</i>",
                < 0.05 => $"<code>{word}</code>",
                _ => word,
            });
        }

        return text.Append('.').ToString();
    }

    private static string LatinName(string raw)
    {
        var letters = new string(raw.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (letters.Length == 0)
        {
            letters = "User";
        }

        return letters.Length > 40 ? letters[..40] : letters;
    }

    private static DataTable CreateCommentTable()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(long));
        table.Columns.Add("ParentId", typeof(long));
        table.Columns.Add("RootId", typeof(long));
        table.Columns.Add("Depth", typeof(int));
        table.Columns.Add("UserId", typeof(int));
        table.Columns.Add("UserName", typeof(string));
        table.Columns.Add("Email", typeof(string));
        table.Columns.Add("HomePage", typeof(string));
        table.Columns.Add("Html", typeof(string));
        table.Columns.Add("CreatedAt", typeof(DateTimeOffset));
        table.Columns.Add("ReplyCount", typeof(int));
        table.Columns.Add("IpAddress", typeof(string));
        table.Columns.Add("UserAgent", typeof(string));
        return table;
    }

    private static async Task<long> NextIdAsync(SqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ISNULL(MAX([Id]), 0) + 1 FROM [{table}]";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task BulkCopyAsync(SqlConnection connection, string table, DataTable rows)
    {
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.TableLock, null)
        {
            DestinationTableName = $"[{table}]",
            BatchSize = 10_000,
            BulkCopyTimeout = 600,
        };

        foreach (DataColumn column in rows.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(rows);
    }

    private sealed record SeedUser(int Id, string UserName, string Email, string? HomePage);
}
