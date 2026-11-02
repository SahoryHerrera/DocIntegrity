using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocIntegrity.Api.Data;
using DocIntegrity.Api.Models;
using DocIntegrity.Api.Services.Comparison;
using DocIntegrity.Api.Services.Interfaces;
using DocIntegrity.Api.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit.Abstractions;

namespace DocIntegrity.Api.Tests;

// La API real (autenticación, CSRF, límites de Kestrel, controlador) con:
//   - un secreto JWT aleatorio, solo para esta prueba;
//   - una base de datos en memoria con usuarios ficticios y un interceptor que
//     cuenta cualquier intento de escritura;
//   - un IBlockchainService que falla si alguien lo usa.
public sealed class ComparisonApiFactory : WebApplicationFactory<Program>
{
    public static readonly Guid FictitiousUser = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _database = $"comparaciones-{Guid.NewGuid()}";

    public string Secret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public WriteGuard Writes { get; } = new();
    public int MaxConcurrentComparisons { get; init; } = 2;
    public double QueueTimeoutSeconds { get; init; } = 5;
    public IPdfPageExtractor? Extractor { get; init; }

    public HttpClient Start()
    {
        UseKestrel(0);
        StartServer();

        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_database, _root).Options))
        {
            db.Users.Add(new User
            {
                Id = FictitiousUser, GoogleId = "google-ficticio", Email = "ficticio@ejemplo.test",
                Name = "Usuario ficticio", CreatedAt = DateTime.UtcNow, LastLoginAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        return CreateClient();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", Secret);
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=127.0.0.1;Port=1;Database=sin_uso");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_database, _root).AddInterceptors(Writes));

            services.RemoveAll<IBlockchainService>();
            services.AddScoped<IBlockchainService>(_ => throw new InvalidOperationException("La comparación no debe usar la blockchain."));

            services.Configure<ComparisonOptions>(options =>
            {
                options.MaxConcurrentComparisons = MaxConcurrentComparisons;
                options.QueueTimeoutSeconds = QueueTimeoutSeconds;
            });

            if (Extractor is not null)
            {
                services.RemoveAll<IPdfPageExtractor>();
                services.AddSingleton(Extractor);
            }
        });
    }

    public string Token(Guid userId, TimeSpan? lifetime = null)
    {
        var now = DateTime.UtcNow;
        var expires = now + (lifetime ?? TimeSpan.FromMinutes(10));

        var token = new JwtSecurityToken(
            "DocIntegrity.Api",
            "DocIntegrity.Client",
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            notBefore: expires < now ? expires.AddMinutes(-5) : now,
            expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public sealed class WriteGuard : SaveChangesInterceptor
    {
        private int _attempts;

        public int Attempts => _attempts;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Interlocked.Increment(ref _attempts);
            throw new InvalidOperationException("La comparación no debe escribir en la base de datos.");
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            throw new InvalidOperationException("La comparación no debe escribir en la base de datos.");
        }
    }
}

public class ComparisonApiTests : IClassFixture<ComparisonApiTests.Server>
{
    public sealed class Server : IDisposable
    {
        public ComparisonApiFactory Factory { get; } = new();
        public HttpClient Client { get; }

        public Server() => Client = Factory.Start();

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private readonly Server _server;

    private readonly ITestOutputHelper _output;

    public ComparisonApiTests(Server server, ITestOutputHelper output)
    {
        _server = server;
        _output = output;
    }

    private static readonly byte[] Original = PdfFixtures.Lines("CONTRATO", "El cliente pagara 1.000 euros.");
    private static readonly byte[] Modified = PdfFixtures.Lines("CONTRATO", "El cliente pagara 1.500 euros.");

    internal static MultipartFormDataContent Form(params (string Field, string FileName, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();

        foreach (var (field, fileName, content) in files)
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(part, field, fileName);
        }

        return form;
    }

    private static MultipartFormDataContent Pair(byte[] original, byte[] modified) =>
        Form(("original", "original.pdf", original), ("modified", "nueva.pdf", modified));

    private async Task<(HttpStatusCode Status, JsonElement Body)> Post(HttpContent content, Action<HttpRequestMessage>? configure = null, bool bearer = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/comparisons") { Content = content };

        if (bearer)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _server.Factory.Token(ComparisonApiFactory.FictitiousUser));
        }

        configure?.Invoke(request);

        using var response = await _server.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    // ---------------------------------------------------------------- comparación válida

    [Fact]
    public async Task ValidComparison_ReturnsDeterministicResult()
    {
        var (status, body) = await Post(Pair(Original, Modified));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("modified", body.GetProperty("verdict").GetString());
        Assert.Equal("complete", body.GetProperty("reliability").GetString());
        Assert.Equal(64, body.GetProperty("files").GetProperty("original").GetProperty("sha256").GetString()!.Length);
        var change = body.GetProperty("changes")[0];
        Assert.Equal("replaced", change.GetProperty("type").GetString());
        Assert.Equal("1.000", change.GetProperty("original").GetProperty("text").GetString());
        Assert.Equal("1.500", change.GetProperty("modified").GetProperty("text").GetString());
        Assert.Equal(1, change.GetProperty("modified").GetProperty("page").GetInt32());
        Assert.Contains("money", change.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.False(body.GetProperty("scope").GetProperty("images").GetBoolean());
        Assert.Equal(0, _server.Factory.Writes.Attempts);
    }

    [Fact]
    public async Task IdenticalFiles_AreIdentical()
    {
        var (status, body) = await Post(Pair(Original, Original));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("identical", body.GetProperty("verdict").GetString());
    }

    // ---------------------------------------------------------------- autenticación

    [Fact]
    public async Task WithoutSession_Is401()
    {
        // Con la cabecera CSRF del frontend pero sin sesión: lo rechaza la autenticación.
        var (status, body) = await Post(Pair(Original, Modified), r => r.Headers.Add("X-Requested-With", "Huella"), bearer: false);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("unauthorized", Code(body));
    }

    [Fact]
    public async Task WithoutSessionNorCsrfHeader_Is403()
    {
        // Sin Bearer, el middleware CSRF actúa antes que la autenticación (igual que en /api/documents).
        var (status, body) = await Post(Pair(Original, Modified), bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("csrf_check_failed", Code(body));
    }

    [Fact]
    public async Task ExpiredToken_Is401SessionExpired()
    {
        var (status, body) = await Post(Pair(Original, Modified), r =>
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _server.Factory.Token(ComparisonApiFactory.FictitiousUser, TimeSpan.FromMinutes(-1))));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("session_expired", Code(body));
    }

    [Fact]
    public async Task TokenOfUnknownUser_Is401()
    {
        var (status, body) = await Post(Pair(Original, Modified), r =>
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _server.Factory.Token(Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("unauthorized", Code(body));
    }

    [Fact]
    public async Task TokenSignedWithAnotherSecret_Is401()
    {
        var foreign = new ComparisonApiFactory().Token(ComparisonApiFactory.FictitiousUser);

        var (status, _) = await Post(Pair(Original, Modified), r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", foreign));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    // ---------------------------------------------------------------- CSRF (sesión por cookie)

    private Action<HttpRequestMessage> Cookie(bool csrfHeader, string? origin) => request =>
    {
        request.Headers.Add("Cookie", $"huella_session={_server.Factory.Token(ComparisonApiFactory.FictitiousUser)}");

        if (csrfHeader)
        {
            request.Headers.Add("X-Requested-With", "Huella");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
    };

    [Fact]
    public async Task Cookie_WithoutCsrfHeader_Is403()
    {
        var (status, body) = await Post(Pair(Original, Modified), Cookie(csrfHeader: false, "http://localhost:5173"), bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("csrf_check_failed", Code(body));
    }

    [Fact]
    public async Task Cookie_FromForeignOrigin_Is403()
    {
        var (status, body) = await Post(Pair(Original, Modified), Cookie(csrfHeader: true, "https://sitio-malicioso.example"), bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("csrf_check_failed", Code(body));
    }

    [Fact]
    public async Task Cookie_WithCsrfHeaderAndAllowedOrigin_IsAccepted()
    {
        var (status, body) = await Post(Pair(Original, Modified), Cookie(csrfHeader: true, "http://localhost:5173"), bearer: false);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("modified", body.GetProperty("verdict").GetString());
    }

    // ---------------------------------------------------------------- validación de la solicitud

    [Fact]
    public async Task MissingModifiedFile_Is400()
    {
        var (status, body) = await Post(Form(("original", "original.pdf", Original)));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("file_required", Code(body));
        Assert.Equal("modified", body.GetProperty("field").GetString());
    }

    [Fact]
    public async Task JsonBody_Is400MultipartRequired()
    {
        var (status, body) = await Post(new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("multipart_required", Code(body));
    }

    [Fact]
    public async Task EmptyFile_Is400()
    {
        var (status, body) = await Post(Form(("original", "original.pdf", Original), ("modified", "nueva.pdf", [])));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("empty_file", Code(body));
    }

    [Fact]
    public async Task NotAPdfExtension_Is400()
    {
        var (status, body) = await Post(Form(("original", "original.pdf", Original), ("modified", "nueva.txt", Modified)));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("unsupported_file_type", Code(body));
    }

    [Fact]
    public async Task InvalidPdf_Is400_AndDiffersFromUnreadablePdf()
    {
        var invalid = await Post(Pair(Original, Encoding.UTF8.GetBytes("Esto no es un PDF.")));
        var unreadable = await Post(Pair(Original, PdfFixtures.Garbage()));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        Assert.Equal("invalid_pdf", Code(invalid.Body));
        Assert.Equal(HttpStatusCode.OK, unreadable.Status);
        Assert.Equal("unreadable", unreadable.Body.GetProperty("notComparableReason").GetString());
    }

    [Fact]
    public async Task EncryptedPdf_Is200NotComparable_Not500()
    {
        var (status, body) = await Post(Pair(Original, PdfFixtures.EncryptedWithUserPassword()));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("not_comparable", body.GetProperty("verdict").GetString());
        Assert.Equal("encrypted", body.GetProperty("notComparableReason").GetString());
    }

    // ---------------------------------------------------------------- límites de tamaño

    private static byte[] OversizedPdf(int megabytes)
    {
        var bytes = new byte[megabytes * 1024 * 1024 + 1];
        Array.Fill(bytes, (byte)' ');
        "%PDF-1.4\n"u8.CopyTo(bytes);
        return bytes;
    }

    [Fact]
    public async Task FileOver20MB_Is413WithField()
    {
        var (status, body) = await Post(Pair(OversizedPdf(21), Modified));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
        Assert.Equal("file_too_large", Code(body));
        Assert.Equal("original", body.GetProperty("field").GetString());
    }

    [Fact]
    public async Task RequestOverTotalLimit_Is413_Not500()
    {
        // Dos archivos de 20 MB + 1 byte: la petición supera el límite de Kestrel (41 MB).
        HttpStatusCode? status = null;
        string? code = null;

        try
        {
            var response = await Post(Pair(OversizedPdf(21), OversizedPdf(21)));
            status = response.Status;
            code = Code(response.Body);
        }
        catch (HttpRequestException ex)
        {
            _output.WriteLine($"Conexión cerrada por Kestrel: {ex.Message}");
            // Kestrel puede cerrar la conexión mientras el cliente aún envía: también es un rechazo.
        }

        if (status is not null)
        {
            _output.WriteLine($"Respuesta {(int)status} {code}");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, status);
            Assert.Equal("file_too_large", code);
        }

        // El servidor sigue respondiendo con normalidad.
        Assert.Equal(HttpStatusCode.OK, (await Post(Pair(Original, Modified))).Status);
    }
}

// Concurrencia: máximo 2 comparaciones simultáneas; la tercera espera 0,3 s y recibe 503.
public class ComparisonConcurrencyTests
{
    private sealed class SlowExtractor(IPdfPageExtractor inner, TimeSpan delay) : IPdfPageExtractor
    {
        private int _running;
        private int _maxRunning;

        public int MaxRunning => _maxRunning;

        public ExtractedDocument Extract(byte[] pdf, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _running);
            InterlockedMax(ref _maxRunning, now);

            try
            {
                Thread.Sleep(delay);
                return inner.Extract(pdf, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;

            while ((current = target) < value && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    [Fact]
    public async Task OnlyTwoComparisonsRunAtOnce_OthersGet503()
    {
        var real = new PdfPageExtractor(
            Microsoft.Extensions.Options.Options.Create(new ComparisonOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfPageExtractor>.Instance);
        var slow = new SlowExtractor(real, TimeSpan.FromMilliseconds(700));

        using var factory = new ComparisonApiFactory { MaxConcurrentComparisons = 2, QueueTimeoutSeconds = 0.3, Extractor = slow };
        using var client = factory.Start();

        var original = PdfFixtures.Lines("Texto original.");
        var modified = PdfFixtures.Lines("Texto modificado.");

        async Task<(HttpStatusCode Status, string? RetryAfter, string Body)> Send()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/comparisons")
            {
                Content = ComparisonApiTests.Form(("original", "a.pdf", original), ("modified", "b.pdf", modified))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(ComparisonApiFactory.FictitiousUser));

            using var response = await client.SendAsync(request);
            return (response.StatusCode, response.Headers.RetryAfter?.ToString(), await response.Content.ReadAsStringAsync());
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Send()));

        Assert.Equal(2, results.Count(r => r.Status == HttpStatusCode.OK));
        var busy = results.Where(r => r.Status == HttpStatusCode.ServiceUnavailable).ToList();
        Assert.Equal(3, busy.Count);
        Assert.All(busy, r =>
        {
            Assert.Contains("comparison_busy", r.Body);
            Assert.Equal("10", r.RetryAfter);
        });
        Assert.True(slow.MaxRunning <= 2, $"se ejecutaron {slow.MaxRunning} extracciones a la vez");

        // Al terminar, los turnos quedan libres.
        Assert.Equal(HttpStatusCode.OK, (await Send()).Status);
        Assert.Equal(0, factory.Writes.Attempts);
    }
}
