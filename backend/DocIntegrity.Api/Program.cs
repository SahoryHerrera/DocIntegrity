using System.Text;
using DocIntegrity.Api.Auth;
using DocIntegrity.Api.Data;
using DocIntegrity.Api.Middleware;
using DocIntegrity.Api.Services;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// =========================================================
// CONTROLLERS
// =========================================================

builder.Services.AddControllers();

// =========================================================
// ERRORES - RESPUESTAS JSON SEGURAS
// =========================================================

builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddProblemDetails();

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

var frontendOrigins =
    builder.Configuration.GetSection("Frontend:Origins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        // Credenciales: el navegador envía la cookie de sesión a la API.
        policy
            .WithOrigins(frontendOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
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
                RequireExpirationTime = true,
                RequireSignedTokens = true,

                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,

                // Solo HS256: se rechazan "none" y cualquier otro algoritmo.
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            jwtSecret
                        )
                    ),

                ClockSkew = TimeSpan.Zero
            };

        options.Events = new JwtBearerEvents
        {
            // El navegador envía el JWT en la cookie HttpOnly; los clientes
            // de API pueden seguir usando Authorization: Bearer.
            OnMessageReceived = context =>
            {
                if (!context.Request.Headers.ContainsKey("Authorization") &&
                    context.Request.Cookies.TryGetValue(SessionCookie.Name, out var cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            },

            // 401 y 403 en JSON, como el resto de errores de la API.
            OnChallenge = async context =>
            {
                context.HandleResponse();

                var expired = context.AuthenticateFailure is SecurityTokenExpiredException;

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                await context.Response.WriteAsJsonAsync(new
                {
                    message = expired
                        ? "Tu sesión expiró. Inicia sesión de nuevo."
                        : "Debes iniciar sesión.",
                    code = expired ? "session_expired" : "unauthorized"
                });
            },

            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;

                await context.Response.WriteAsJsonAsync(new
                {
                    message = "No tienes permiso para realizar esta acción.",
                    code = "forbidden"
                });
            }
        };
    });

// =========================================================
// AUTHORIZATION
// Privado por defecto: todo endpoint exige sesión salvo que
// se marque explícitamente con [AllowAnonymous].
// =========================================================

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

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

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseMiddleware<CsrfProtectionMiddleware>((object)frontendOrigins);

app.UseAuthentication();

app.UseAuthorization();

// =========================================================
// ROOT ENDPOINT (público)
// =========================================================

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        application = "DocIntegrity API",
        status = "Running"
    });
}).AllowAnonymous();

// =========================================================
// CONTROLLERS
// =========================================================

app.MapControllers();

app.Run();