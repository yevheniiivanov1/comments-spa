using System.Globalization;

namespace Comments.Seed;

internal sealed record SeedOptions(
    int Comments, int Users, double TopLevelShare, string SqlConnectionString, string? RedisConnectionString, string? PopularThreadsFile)
{
    public static SeedOptions Parse(string[] args)
    {
        string? Arg(string name) =>
            args.SkipWhile(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

        var sql = Arg("--sql") ?? Environment.GetEnvironmentVariable("ConnectionStrings__commentsdb")
            ?? throw new ArgumentException("Pass --sql or set ConnectionStrings__commentsdb.");

        return new SeedOptions(
            int.Parse(Arg("--comments") ?? "1000000", CultureInfo.InvariantCulture),
            int.Parse(Arg("--users") ?? "100000", CultureInfo.InvariantCulture),
            double.Parse(Arg("--top-level-share") ?? "0.3", CultureInfo.InvariantCulture),
            sql,
            Arg("--redis") ?? Environment.GetEnvironmentVariable("ConnectionStrings__redis"),
            Arg("--popular-out") ?? Environment.GetEnvironmentVariable("POPULAR_THREADS_FILE"));
    }
}
