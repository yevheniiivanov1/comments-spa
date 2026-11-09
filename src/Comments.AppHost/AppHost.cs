var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithDataVolume("comments-sql-data")
    .AddDatabase("commentsdb");

var redis = builder.AddRedis("redis");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithImageTag("4.1-management")
    .WithManagementPlugin();

var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithImageTag("8.19.4")
    .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
    .WithDataVolume("comments-es-data");

var blobs = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume("comments-azurite-data"))
    .AddBlobs("blobs");

var api = builder.AddProject<Projects.Comments_Api>("api")
    .WithReference(sql).WaitFor(sql)
    .WithReference(redis).WaitFor(redis)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(elasticsearch)
    .WithReference(blobs).WaitFor(blobs)
    .WithHttpHealthCheck("/alive", endpointName: "http")
    .WithUrlForEndpoint("http", _ => new() { Url = "/scalar", DisplayText = "API reference" });

builder.AddProject<Projects.Comments_Worker>("worker")
    .WithReference(sql).WaitFor(api)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(elasticsearch).WaitFor(elasticsearch);

builder.AddJavaScriptApp("web", "../Comments.Web", "start:aspire")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithHttpHealthCheck("/");

builder.Build().Run();
