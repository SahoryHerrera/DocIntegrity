using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

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

app.MapPost("/api/documents/hash", async (IFormFile file) =>
{
    if (file.Length == 0)
    {
        return Results.BadRequest(new
        {
            message = "El archivo está vacío."
        });
    }

    await using var stream = file.OpenReadStream();

    var hashBytes = await SHA256.HashDataAsync(stream);
    var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

    return Results.Ok(new
    {
        fileName = file.FileName,
        size = file.Length,
        sha256 = hash
    });
})
.DisableAntiforgery();

app.Run();