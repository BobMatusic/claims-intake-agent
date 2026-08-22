using System.Text;
using Azure;
using Azure.AI.OpenAI;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using ClaimsIntake.Core;
using ClaimsIntake.Core.Agents;
using ClaimsIntake.Core.Extraction;
using ClaimsIntake.Core.Policies;
using ClaimsIntake.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.FeatureManagement;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using ClaimsIntake.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(sp => new AzureDocumentIntelligence(
    builder.Configuration["DocIntelligence:Endpoint"]!,
    builder.Configuration["DocIntelligence:Key"]!));

builder.Services.AddSingleton<IChatClient>(sp =>
    new AzureOpenAIClient(
            new Uri(builder.Configuration["AzureOpenAI:Endpoint"]!),
            new AzureKeyCredential(builder.Configuration["AzureOpenAI:Key"]!))
        .GetChatClient(builder.Configuration["AzureOpenAI:Deployment"]!)
        .AsIChatClient()
        .AsBuilder()
        .UseOpenTelemetry(
            sourceName: "ClaimsIntake.AI",
            configure: c => c.EnableSensitiveData = builder.Environment.IsDevelopment())
        .Build());

builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
    new AzureOpenAIClient(
            new Uri(builder.Configuration["AzureEmbedding:Endpoint"]!),
            new AzureKeyCredential(builder.Configuration["AzureEmbedding:Key"]!))
        .GetEmbeddingClient(builder.Configuration["AzureEmbedding:Deployment"]!)
        .AsIEmbeddingGenerator());

builder.Services.AddSingleton<PolicyConditionsIndexer>();
builder.Services.AddSingleton<PolicyConditionsSearch>();

builder.Services.AddSingleton<CaseFileExtractor>();
builder.Services.AddSingleton<ExclusionChecker>();
builder.Services.AddSingleton<AdjusterSummaryWriter>();
builder.Services.AddSingleton<PolicyService>();
builder.Services.AddSingleton<DecisionEngine>();
builder.Services.AddSingleton<ClaimEvaluator>();
builder.Services.AddSingleton<ClaimsAgent>();

builder.Configuration.AddAzureAppConfiguration(options =>
{
    options.Connect(builder.Configuration["AppConfig:ConnectionString"])
           .UseFeatureFlags(ff => ff.SetRefreshInterval(TimeSpan.FromSeconds(30)));
});

builder.Services.AddAzureAppConfiguration();
builder.Services.AddFeatureManagement();

var langfusePublicKey = builder.Configuration["Langfuse:PublicKey"]!;
var langfuseSecretKey = builder.Configuration["Langfuse:SecretKey"]!;
var langfuseBaseUrl = builder.Configuration["Langfuse:BaseUrl"]!;
var langfuseAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{langfusePublicKey}:{langfuseSecretKey}"));

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource("ClaimsIntake")
        .AddSource("ClaimsIntake.AI")
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri($"{langfuseBaseUrl}/api/public/otel/v1/traces");
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
            o.Headers = $"Authorization=Basic {langfuseAuth}";
        }))
    .UseAzureMonitor();

var app = builder.Build();

app.UseAzureAppConfiguration();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var indexer = app.Services.GetRequiredService<PolicyConditionsIndexer>();
if (!indexer.IsIndexed)
{
    var vppPath = Path.Combine(app.Environment.ContentRootPath, "Content", "vpp-kasko.md");
    var chunks = PolicyConditionsParser.Parse(await File.ReadAllTextAsync(vppPath));
    await indexer.IndexAsync(chunks);
}

app.Run();
