var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithDataVolume("comments-sql-data")
    .AddDatabase("commentsdb");

var redis = builder.AddRedis("redis");

var blobs = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume("comments-azurite-data"))
    .AddBlobs("blobs");

var api = builder.AddProject<Projects.Comments_Api>("api")
    .WithReference(sql).WaitFor(sql)
    .WithReference(redis).WaitFor(redis)
    .WithReference(blobs).WaitFor(blobs)
    .WithHttpHealthCheck("/alive", endpointName: "http")
    .WithUrlForEndpoint("http", _ => new() { Url = "/scalar", DisplayText = "API reference" });

builder.AddJavaScriptApp("web", "../Comments.Web", "start:aspire")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithHttpHealthCheck("/");

builder.Build().Run();
