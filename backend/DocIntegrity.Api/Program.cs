using System.Text;
using DocIntegrity.Api.Data;
using DocIntegrity.Api.Services;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// =========================================================
// CONTROLLERS
// =========================================================

builder.Services.AddControllers();

// =========================================================
// OPENAPI
// =========================================================

builder.Services.AddOpenApi();

// =========================================================
// DATABASE - POSTGRESQL
// =========================================================

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString(
            "DefaultConnection"
        )
    ));

// =========================================================
// CORS
// =========================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// =========================================================
// SERVICES
// =========================================================

builder.Services.AddScoped<IHashService, HashService>();

builder.Services.AddScoped<
    IBlockchainService,
    BlockchainService
>();

builder.Services.AddScoped<
    IDocumentTextExtractorService,
    PdfTextExtractorService
>();

builder.Services.AddScoped<
    IJwtService,
    JwtService
>();

// =========================================================
// ARTIFICIAL INTELLIGENCE - OLLAMA
// =========================================================

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

// =========================================================
// JWT AUTHENTICATION
// =========================================================

var jwtSecret =
    builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException(
        "La configuración Jwt:Secret no existe."
    );

var jwtIssuer =
    builder.Configuration["Jwt:Issuer"]
    ?? "DocIntegrity.Api";

var jwtAudience =
    builder.Configuration["Jwt:Audience"]
    ?? "DocIntegrity.Client";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            JwtBearerDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            jwtSecret
                        )
                    ),

                ClockSkew = TimeSpan.Zero
            };
    });

// =========================================================
// AUTHORIZATION
// =========================================================

builder.Services.AddAuthorization();

// =========================================================
// BUILD APPLICATION
// =========================================================

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// =========================================================
// MIDDLEWARE
// =========================================================

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();

app.UseAuthorization();

// =========================================================
// ROOT ENDPOINT
// =========================================================

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        application = "DocIntegrity API",
        status = "Running"
    });
});

// =========================================================
// CONTROLLERS
// =========================================================

app.MapControllers();

app.Run();