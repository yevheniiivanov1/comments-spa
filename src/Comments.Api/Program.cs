using Comments.Api.Endpoints;
using Comments.Api.GraphQL;
using Comments.Api.Hosting;
using Comments.Api.Realtime;
using Comments.Infrastructure;
using Comments.Infrastructure.Json;
using Microsoft.AspNetCore.Http.Features;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddCommentsApi();

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options => JsonDefaults.Configure(options.SerializerOptions));
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = UploadLimits.MaxRequestBytes);
builder.Services.AddCommentsRateLimiting(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddSignalR().AddJsonProtocol(options => JsonDefaults.Configure(options.PayloadSerializerOptions));
builder.Services.AddHostedService<LiveUpdatesConsumer>();
builder.Services.AddGraphQLServer()
    .AddQueryType<CommentsQuery>()
    .AddMaxExecutionDepthRule(8)
    .ModifyCostOptions(options =>
    {
        options.EnforceCostLimits = true;
        options.MaxFieldCost = GraphQLLimits.MaxFieldCost;
    })
    .ModifyRequestOptions(options => options.IncludeExceptionDetails = builder.Environment.IsDevelopment());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapDefaultEndpoints();
app.MapCommentEndpoints();
app.MapCaptchaEndpoints();
app.MapFileEndpoints();
app.MapSearchEndpoints();
app.MapGraphQL().RequireRateLimiting(RateLimiting.GraphQLPolicy);
app.MapHub<CommentsHub>(CommentsHub.Path);
app.MapFallbackToFile("index.html");
app.MapUnknownRoutesToNotFound("/api", "/files", "/hubs");

await app.MigrateDatabaseAsync();
await app.RunAsync();

public partial class Program;
