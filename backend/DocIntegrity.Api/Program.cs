using DocIntegrity.Api.Data;
using DocIntegrity.Api.Services;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// OpenAPI
builder.Services.AddOpenApi();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString(
            "DefaultConnection"
        )
    ));

// Services
builder.Services.AddScoped<IHashService, HashService>();

builder.Services.AddScoped<
    IBlockchainService,
    BlockchainService
>();

builder.Services.AddScoped<
    IDocumentTextExtractorService,
    PdfTextExtractorService
>();

// Artificial Intelligence - Ollama
builder.Services.AddHttpClient<
    IAiService,
    OllamaAiService
>(client =>
{
    var baseUrl =
        builder.Configuration["Ollama:BaseUrl"]
        ?? "http://localhost:11434";

    client.BaseAddress = new Uri(baseUrl);

    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        application = "DocIntegrity API",
        status = "Running"
    });
});

app.MapControllers();

app.Run();