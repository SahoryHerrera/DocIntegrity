using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using DocIntegrity.Api.DTOs;
using DocIntegrity.Api.Exceptions;
using DocIntegrity.Api.Models;
using DocIntegrity.Api.Services.Comparison;
using DocIntegrity.Api.Services.Interfaces;
using DocIntegrity.Api.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocIntegrity.Api.Tests;

// Blockchain de prueba: solo lectura configurable; registrar un documento falla y se cuenta.
internal sealed class FakeBlockchain : IBlockchainService
{
    public const long BlockNumber = 2;
    public const string TransactionHash = "0x" + "ab12" + "00000000000000000000000000000000000000000000000000000000cd34";

    private int _reads;
    private int _writes;

    public int Reads => _reads;
    public int Writes => _writes;

    public bool RecordExists { get; set; } = true;
    public StoredTransactionCheck Transaction { get; set; } = new(true, true, BlockNumber, true, true);
    public Exception? Failure { get; set; }
    public TimeSpan Delay { get; set; }

    public string ContractAddress => "0x" + new string('c', 40);

    private async Task ReadAsync()
    {
        Interlocked.Increment(ref _reads);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay);
        }

        if (Failure is not null)
        {
            throw Failure;
        }
    }

    public async Task<long> GetChainIdAsync()
    {
        await ReadAsync();
        return 31337;
    }

    public async Task<OnChainDocumentRecord> GetDocumentRecordAsync(string sha256)
    {
        await ReadAsync();
        return RecordExists
            ? new OnChainDocumentRecord(true, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), "0x" + new string('e', 40))
            : new OnChainDocumentRecord(false, null, null);
    }

    public async Task<StoredTransactionCheck> CheckRegistrationTransactionAsync(string transactionHash, string sha256)
    {
        await ReadAsync();
        return Transaction;
    }

    public async Task<bool> IsDocumentRegisteredAsync(string sha256)
    {
        await ReadAsync();
        return RecordExists;
    }

    public async Task<(string TransactionHash, long BlockNumber)?> FindConfirmedRegistrationAsync(string sha256)
    {
        await ReadAsync();
        return null;
    }

    public Task<(string TransactionHash, long BlockNumber)> RegisterDocumentAsync(string sha256)
    {
        Interlocked.Increment(ref _writes);
        throw new InvalidOperationException("La comparación nunca debe registrar documentos en la blockchain.");
    }
}

// POST /api/comparisons/documents/{documentId}: comparación de una nueva versión con un
// documento de la biblioteca, verificando antes la huella del original.
public class RegisteredComparisonApiTests
{
    private sealed class CountingExtractor(IPdfPageExtractor inner) : IPdfPageExtractor
    {
        private int _calls;
        public int Calls => _calls;

        public ExtractedDocument Extract(byte[] pdf, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.Extract(pdf, cancellationToken);
        }
    }

    private sealed class FakeExplainer : IComparisonExplainer
    {
        private int _calls;
        public int Calls => _calls;
        public ComparisonResultDto? Received { get; private set; }
        public Exception? Failure { get; set; }

        public Task<ComparisonExplanationDto> ExplainAsync(Guid comparisonId, ComparisonResultDto result, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Received = result;

            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new ComparisonExplanationDto
            {
                ComparisonId = comparisonId,
                Model = "falso",
                Summary = "Resumen de prueba.",
                Changes = result.Changes.Select(c => new ChangeExplanationDto { Id = c.Id, Explanation = "Explicación de prueba.", Relevance = "media" }).ToList()
            });
        }
    }

    private sealed class Env : IDisposable
    {
        public required ComparisonApiFactory Factory { get; init; }
        public required HttpClient Client { get; init; }
        public required FakeBlockchain Blockchain { get; init; }
        public required CountingExtractor Extractor { get; init; }
        public required FakeExplainer Explainer { get; init; }

        public int CachedComparisons => Factory.Services.GetRequiredService<ComparisonCache>().Count;

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private static readonly byte[] Original = PdfFixtures.Lines(ExplanationSamples.Original);
    private static readonly byte[] Modified = PdfFixtures.Lines(ExplanationSamples.Modified);
    private static readonly byte[] Unrelated = PdfFixtures.Lines("OTRO DOCUMENTO", "Sin relacion con el contrato.");

    private static string Sha(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static readonly Guid OwnDocument = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static readonly Guid UppercaseHashDocument = Guid.Parse("dddddddd-0000-4000-8000-000000000002");
    private static readonly Guid ForeignDocument = Guid.Parse("dddddddd-0000-4000-8000-000000000003");
    private static readonly Guid HistoricDocument = Guid.Parse("dddddddd-0000-4000-8000-000000000004");

    private static Document Doc(Guid id, Guid? owner, string sha256, string fileName = "contrato.pdf") => new()
    {
        Id = id,
        FileName = fileName,
        Sha256 = sha256,
        Size = 1234,
        RegisteredAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
        RegisteredByUserId = owner,
        BlockchainTransactionHash = FakeBlockchain.TransactionHash,
        BlockchainBlockNumber = FakeBlockchain.BlockNumber,
        AiTitle = "Contrato de arrendamiento",
        AiDocumentType = "contrato",
        AiSummary = "Resumen ficticio."
    };

    private static Env Start(Action<FakeBlockchain>? blockchain = null, double blockchainTimeoutSeconds = 10)
    {
        var fake = new FakeBlockchain();
        blockchain?.Invoke(fake);

        var extractor = new CountingExtractor(new PdfPageExtractor(Options.Create(new ComparisonOptions()), NullLogger<PdfPageExtractor>.Instance));
        var explainer = new FakeExplainer();

        var factory = new ComparisonApiFactory
        {
            Blockchain = fake,
            BlockchainCheckTimeoutSeconds = blockchainTimeoutSeconds,
            Extractor = extractor,
            Explainer = explainer,
            Documents =
            [
                Doc(OwnDocument, ComparisonApiFactory.FictitiousUser, Sha(Original)),
                Doc(UppercaseHashDocument, ComparisonApiFactory.FictitiousUser, Sha(Unrelated).ToUpperInvariant(), "otro.pdf"),
                Doc(ForeignDocument, ComparisonApiFactory.OtherFictitiousUser, Sha(Original)),
                Doc(HistoricDocument, null, Sha(Original))
            ]
        };

        return new Env { Factory = factory, Client = factory.Start(), Blockchain = fake, Extractor = extractor, Explainer = explainer };
    }

    private static MultipartFormDataContent Pair(byte[] original, byte[] modified) =>
        ComparisonApiTests.Form(("original", "original.pdf", original), ("modified", "nueva.pdf", modified));

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Send(
        Env env, string path, HttpContent? content, Guid? user = null, Action<HttpRequestMessage>? configure = null, bool bearer = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };

        if (bearer)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", env.Factory.Token(user ?? ComparisonApiFactory.FictitiousUser));
        }

        configure?.Invoke(request);

        using var response = await env.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Task<(HttpStatusCode Status, JsonElement Body)> Compare(
        Env env, Guid documentId, byte[] original, byte[] modified, Guid? user = null, Action<HttpRequestMessage>? configure = null, bool bearer = true) =>
        Send(env, $"/api/comparisons/documents/{documentId}", Pair(original, modified), user, configure, bearer);

    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    private static JsonElement Chain(JsonElement body) => body.GetProperty("registeredDocument").GetProperty("blockchain");

    // Nada se guardó ni se registró.
    private static void AssertNoWrites(Env env)
    {
        Assert.Equal(0, env.Factory.Writes.Attempts);
        Assert.Equal(0, env.Blockchain.Writes);
    }

    // ---------------------------------------------------------------- comparación con la biblioteca

    [Fact]
    public async Task MatchingOriginal_ComparesAndReportsConfirmedChain()
    {
        using var env = Start();

        var (status, body) = await Compare(env, OwnDocument, Original, Modified);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("modified", body.GetProperty("verdict").GetString());
        Assert.True(body.GetProperty("changes").GetArrayLength() > 0);

        var registered = body.GetProperty("registeredDocument");
        Assert.Equal(OwnDocument, registered.GetProperty("id").GetGuid());
        Assert.Equal(Sha(Original), registered.GetProperty("sha256").GetString());
        Assert.Equal("Contrato de arrendamiento", registered.GetProperty("title").GetString());
        Assert.True(registered.GetProperty("originalMatchesRegisteredSha256").GetBoolean());
        Assert.Equal(Sha(Original), body.GetProperty("files").GetProperty("original").GetProperty("sha256").GetString());

        var chain = Chain(body);
        Assert.Equal("confirmed", chain.GetProperty("status").GetString());
        Assert.True(chain.GetProperty("detail").GetProperty("registeredInBlockchain").GetBoolean());
        Assert.Equal("confirmed", chain.GetProperty("detail").GetProperty("storedTransaction").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, chain.GetProperty("failureCode").ValueKind);

        Assert.True(body.GetProperty("explanationAvailable").GetBoolean());
        Assert.Equal(1, env.CachedComparisons);
        Assert.Equal(2, env.Extractor.Calls);
        AssertNoWrites(env);
    }

    [Fact]
    public async Task IdenticalNewVersion_IsIdentical_WithoutExtractionNorComparisonId()
    {
        using var env = Start();

        var (status, body) = await Compare(env, OwnDocument, Original, Original);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("identical", body.GetProperty("verdict").GetString());
        Assert.Equal("confirmed", Chain(body).GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("comparisonId").ValueKind);
        Assert.False(body.GetProperty("explanationAvailable").GetBoolean());
        Assert.Equal(0, env.Extractor.Calls);
        Assert.Equal(0, env.CachedComparisons);
        AssertNoWrites(env);
    }

    [Fact]
    public async Task StoredHashInUppercase_StillMatches()
    {
        using var env = Start();

        var (status, body) = await Compare(env, UppercaseHashDocument, Unrelated, Modified);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("registeredDocument").GetProperty("originalMatchesRegisteredSha256").GetBoolean());
    }

    // ---------------------------------------------------------------- huella incorrecta

    [Fact]
    public async Task WrongOriginal_Is422_BeforeExtractionChainCacheOrAi()
    {
        using var env = Start();

        var (status, body) = await Compare(env, OwnDocument, Modified, Original);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("original_hash_mismatch", Code(body));
        Assert.Equal("original", body.GetProperty("field").GetString());
        Assert.Equal(Sha(Original), body.GetProperty("registeredSha256").GetString());
        Assert.Equal(Sha(Modified), body.GetProperty("providedSha256").GetString());
        Assert.False(body.TryGetProperty("comparisonId", out _));

        Assert.Equal(0, env.Extractor.Calls);
        Assert.Equal(0, env.Blockchain.Reads);
        Assert.Equal(0, env.CachedComparisons);
        Assert.Equal(0, env.Explainer.Calls);
        AssertNoWrites(env);
    }

    // ---------------------------------------------------------------- estado en la blockchain (informativo)

    [Fact]
    public async Task HashNotInContract_IsNotRegistered_AndComparisonStillRuns()
    {
        using var env = Start(chain => chain.RecordExists = false);

        var (status, body) = await Compare(env, OwnDocument, Original, Modified);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("modified", body.GetProperty("verdict").GetString());
        Assert.Equal("not_registered", Chain(body).GetProperty("status").GetString());
        Assert.False(Chain(body).GetProperty("detail").GetProperty("registeredInBlockchain").GetBoolean());
        // La huella coincide con PostgreSQL aunque la cadena no la tenga.
        Assert.True(body.GetProperty("registeredDocument").GetProperty("originalMatchesRegisteredSha256").GetBoolean());
        AssertNoWrites(env);
    }

    [Theory]
    [InlineData(false, true, FakeBlockchain.BlockNumber, true, true, "not_found")]
    [InlineData(true, false, FakeBlockchain.BlockNumber, true, true, "failed")]
    [InlineData(true, true, 99L, true, true, "mismatch")]
    [InlineData(true, true, FakeBlockchain.BlockNumber, false, true, "mismatch")]
    [InlineData(true, true, FakeBlockchain.BlockNumber, true, false, "mismatch")]
    public async Task HashInContractWithoutConfirmedTransaction_IsNeverConfirmed(
        bool found, bool succeeded, long block, bool toContract, bool emitsHash, string transactionStatus)
    {
        using var env = Start(chain => chain.Transaction = new StoredTransactionCheck(found, succeeded, block, toContract, emitsHash));

        var (status, body) = await Compare(env, OwnDocument, Original, Modified);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("unconfirmed", Chain(body).GetProperty("status").GetString());
        Assert.Equal(transactionStatus, Chain(body).GetProperty("detail").GetProperty("storedTransaction").GetProperty("status").GetString());
        Assert.False(Chain(body).GetProperty("detail").GetProperty("storedTransactionConfirmed").GetBoolean());
    }

    public static TheoryData<Exception, string> ChainFailures => new()
    {
        { new ExternalServiceException(ExternalService.Blockchain, ExternalFailure.Unavailable, "La red blockchain no está disponible."), "blockchain_unavailable" },
        { new ExternalServiceException(ExternalService.Blockchain, ExternalFailure.Timeout, "La red blockchain tardó demasiado en responder."), "blockchain_timeout" },
        { new ExternalServiceException(ExternalService.Blockchain, ExternalFailure.Rejected, "La red blockchain rechazó la operación."), "blockchain_rejected" },
        { new InvalidOperationException("La configuración de blockchain está incompleta."), "blockchain_misconfigured" }
    };

    [Theory]
    [MemberData(nameof(ChainFailures))]
    public async Task ChainFailure_IsUnavailable_AndComparisonStillRuns(Exception failure, string failureCode)
    {
        using var env = Start(chain => chain.Failure = failure);

        var (status, body) = await Compare(env, OwnDocument, Original, Modified);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("modified", body.GetProperty("verdict").GetString());
        Assert.Equal("unavailable", Chain(body).GetProperty("status").GetString());
        Assert.Equal(failureCode, Chain(body).GetProperty("failureCode").GetString());
        Assert.Equal(JsonValueKind.Null, Chain(body).GetProperty("detail").ValueKind);
        Assert.True(body.GetProperty("explanationAvailable").GetBoolean());
        AssertNoWrites(env);
    }

    [Fact]
    public async Task ChainThatDoesNotAnswer_IsUnavailableAfterItsTimeout()
    {
        using var env = Start(chain => chain.Delay = TimeSpan.FromSeconds(30), blockchainTimeoutSeconds: 0.3);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var (status, body) = await Compare(env, OwnDocument, Original, Modified);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("unavailable", Chain(body).GetProperty("status").GetString());
        Assert.Equal("blockchain_timeout", Chain(body).GetProperty("failureCode").GetString());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
    }

    // ---------------------------------------------------------------- privacidad

    [Fact]
    public async Task ForeignMissingAndHistoricDocuments_GetTheSame404_BeforeAnyWork()
    {
        using var env = Start();

        var foreign = await Compare(env, ForeignDocument, Original, Modified);
        var missing = await Compare(env, Guid.NewGuid(), Original, Modified);
        var historic = await Compare(env, HistoricDocument, Original, Modified);

        foreach (var (status, body) in new[] { foreign, missing, historic })
        {
            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Equal("document_not_found", Code(body));
        }

        Assert.Equal(missing.Body.GetRawText(), foreign.Body.GetRawText());
        Assert.Equal(missing.Body.GetRawText(), historic.Body.GetRawText());
        Assert.Equal(0, env.Extractor.Calls);
        Assert.Equal(0, env.Blockchain.Reads);
        AssertNoWrites(env);
    }

    [Fact]
    public async Task ForeignDocument_Is404EvenWithInvalidBody()
    {
        using var env = Start();

        // La propiedad se comprueba antes que el formulario: no se revela si el id existe.
        var (status, body) = await Send(env, $"/api/comparisons/documents/{ForeignDocument}", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("document_not_found", Code(body));
    }

    [Fact]
    public async Task OwnerOfTheForeignDocument_CanCompareIt()
    {
        using var env = Start();

        var (status, body) = await Compare(env, ForeignDocument, Original, Modified, ComparisonApiFactory.OtherFictitiousUser);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(ForeignDocument, body.GetProperty("registeredDocument").GetProperty("id").GetGuid());
    }

    // ---------------------------------------------------------------- autenticación y CSRF

    [Fact]
    public async Task WithoutSession_Is401()
    {
        using var env = Start();

        var (status, body) = await Compare(env, OwnDocument, Original, Modified, configure: r => r.Headers.Add("X-Requested-With", "Huella"), bearer: false);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("unauthorized", Code(body));
    }

    [Fact]
    public async Task TokenOfUnknownUser_Is401()
    {
        using var env = Start();

        var (status, body) = await Compare(env, OwnDocument, Original, Modified, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("unauthorized", Code(body));
    }

    [Fact]
    public async Task ExpiredToken_Is401SessionExpired()
    {
        using var env = Start();

        var (status, body) = await Compare(env, OwnDocument, Original, Modified, configure: r =>
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", env.Factory.Token(ComparisonApiFactory.FictitiousUser, TimeSpan.FromMinutes(-1))));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("session_expired", Code(body));
    }

    private static Action<HttpRequestMessage> Cookie(Env env, bool csrfHeader, string? origin) => request =>
    {
        request.Headers.Add("Cookie", $"huella_session={env.Factory.Token(ComparisonApiFactory.FictitiousUser)}");

        if (csrfHeader)
        {
            request.Headers.Add("X-Requested-With", "Huella");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
    };

    [Theory]
    [InlineData(false, "http://localhost:5173")]
    [InlineData(true, "https://sitio-malicioso.example")]
    public async Task Cookie_WithoutCsrfHeaderOrFromForeignOrigin_Is403(bool csrfHeader, string origin)
    {
        using var env = Start();

        var (status, body) = await Compare(env, OwnDocument, Original, Modified, configure: Cookie(env, csrfHeader, origin), bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("csrf_check_failed", Code(body));
        Assert.Equal(0, env.Blockchain.Reads);
    }

    [Fact]
    public async Task Cookie_WithCsrfHeaderAndAllowedOrigin_IsAccepted()
    {
        using var env = Start();

        var (status, body) = await Compare(env, OwnDocument, Original, Modified, configure: Cookie(env, true, "http://localhost:5173"), bearer: false);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("confirmed", Chain(body).GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------- validación (la misma que la comparación libre)

    [Fact]
    public async Task MissingModifiedFile_Is400WithField()
    {
        using var env = Start();

        var (status, body) = await Send(env, $"/api/comparisons/documents/{OwnDocument}", ComparisonApiTests.Form(("original", "original.pdf", Original)));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("file_required", Code(body));
        Assert.Equal("modified", body.GetProperty("field").GetString());
        Assert.Equal(0, env.Blockchain.Reads);
    }

    [Fact]
    public async Task NotAPdf_Is400()
    {
        using var env = Start();

        var (status, body) = await Send(env, $"/api/comparisons/documents/{OwnDocument}",
            ComparisonApiTests.Form(("original", "original.pdf", Original), ("modified", "nueva.pdf", System.Text.Encoding.UTF8.GetBytes("Esto no es un PDF."))));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid_pdf", Code(body));
        Assert.Equal("modified", body.GetProperty("field").GetString());
    }

    [Fact]
    public async Task JsonBody_Is400MultipartRequired()
    {
        using var env = Start();

        var (status, body) = await Send(env, $"/api/comparisons/documents/{OwnDocument}", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("multipart_required", Code(body));
    }

    [Fact]
    public async Task EncryptedNewVersion_Is200NotComparable()
    {
        using var env = Start();

        var (status, body) = await Compare(env, OwnDocument, Original, PdfFixtures.EncryptedWithUserPassword());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("not_comparable", body.GetProperty("verdict").GetString());
        Assert.Equal("confirmed", Chain(body).GetProperty("status").GetString());
        Assert.Equal(0, env.CachedComparisons);
    }

    // ---------------------------------------------------------------- explicación con IA (Ollama simulado)

    [Fact]
    public async Task RegisteredComparison_CanBeExplained_WithoutSendingRegistryData()
    {
        using var env = Start();
        var (_, body) = await Compare(env, OwnDocument, Original, Modified);
        var id = body.GetProperty("comparisonId").GetString()!;

        var (status, explanation) = await Send(env, $"/api/comparisons/{id}/explanation", null);
        var again = await Send(env, $"/api/comparisons/{id}/explanation", null);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(explanation.GetProperty("generatedByAi").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(1, env.Explainer.Calls);

        // La IA recibe solo los cambios: ni la huella, ni el nombre, ni la transacción del registro.
        var prompt = ExplanationPrompt.Build(env.Explainer.Received!, new ExplanationOptions());
        Assert.DoesNotContain(Sha(Original), prompt.UserPrompt);
        Assert.DoesNotContain("contrato.pdf", prompt.UserPrompt);
        Assert.DoesNotContain(FakeBlockchain.TransactionHash, prompt.UserPrompt);
        AssertNoWrites(env);
    }

    [Fact]
    public async Task RegisteredComparison_ExplanationOfAnotherUser_Is404()
    {
        using var env = Start();
        var id = (await Compare(env, OwnDocument, Original, Modified)).Body.GetProperty("comparisonId").GetString()!;

        var (status, body) = await Send(env, $"/api/comparisons/{id}/explanation", null, ComparisonApiFactory.OtherFictitiousUser);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("comparison_not_found", Code(body));
        Assert.Equal(0, env.Explainer.Calls);
    }

    [Fact]
    public async Task OllamaUnavailable_Is503_AndComparisonStaysAvailable()
    {
        using var env = Start();
        env.Explainer.Failure = new ExternalServiceException(ExternalService.Ai, ExternalFailure.Unavailable, "Ollama no está disponible.");
        var id = (await Compare(env, OwnDocument, Original, Modified)).Body.GetProperty("comparisonId").GetString()!;

        var failed = await Send(env, $"/api/comparisons/{id}/explanation", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.Status);
        Assert.Equal("ai_unavailable", Code(failed.Body));

        env.Explainer.Failure = null;
        var retried = await Send(env, $"/api/comparisons/{id}/explanation", null);

        Assert.Equal(HttpStatusCode.OK, retried.Status);
        AssertNoWrites(env);
    }

    // ---------------------------------------------------------------- comparación libre y estado del documento

    [Fact]
    public async Task FreeComparison_HasNoRegisteredDocumentAndDoesNotReadTheChain()
    {
        using var env = Start();

        var (status, body) = await Send(env, "/api/comparisons", Pair(Original, Modified));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("registeredDocument").ValueKind);
        Assert.Equal(0, env.Blockchain.Reads);
    }

    [Fact]
    public async Task DocumentBlockchainStatus_KeepsItsResponseAfterSharingTheReader()
    {
        using var env = Start();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{OwnDocument}/blockchain-status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", env.Factory.Token(ComparisonApiFactory.FictitiousUser));
        using var response = await env.Client.SendAsync(request);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnDocument, body.GetProperty("documentId").GetGuid());
        Assert.Equal(31337, body.GetProperty("networkChainId").GetInt64());
        Assert.True(body.GetProperty("registeredInBlockchain").GetBoolean());
        Assert.True(body.GetProperty("storedTransactionConfirmed").GetBoolean());
        Assert.Equal(FakeBlockchain.TransactionHash, body.GetProperty("storedTransactionHash").GetString());
        Assert.Equal(FakeBlockchain.BlockNumber, body.GetProperty("storedBlockNumber").GetInt64());
    }
}

// Clasificación del estado informativo: "confirmed" solo con las dos evidencias de la cadena activa.
public class RegisteredBlockchainCheckTests
{
    private static BlockchainStatusDto Status(bool inContract, string transaction) => new()
    {
        RegisteredInBlockchain = inContract,
        StoredTransactionConfirmed = transaction == "confirmed",
        StoredTransaction = new StoredTransactionStatusDto { Status = transaction },
        CheckedAt = DateTime.UtcNow
    };

    [Fact]
    public void ContractAndConfirmedTransaction_IsConfirmed() =>
        Assert.Equal("confirmed", RegisteredBlockchainCheck.From(Status(true, "confirmed")).Status);

    [Theory]
    [InlineData("not_stored")]
    [InlineData("not_found")]
    [InlineData("failed")]
    [InlineData("mismatch")]
    public void ContractWithoutConfirmedTransaction_IsUnconfirmed(string transaction) =>
        Assert.Equal("unconfirmed", RegisteredBlockchainCheck.From(Status(true, transaction)).Status);

    [Theory]
    [InlineData("confirmed")]
    [InlineData("not_found")]
    [InlineData("not_stored")]
    public void HashMissingFromContract_IsNotRegistered(string transaction) =>
        Assert.Equal("not_registered", RegisteredBlockchainCheck.From(Status(false, transaction)).Status);
}
