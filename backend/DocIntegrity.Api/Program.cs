using DocIntegrity.Api.Data;
using DocIntegrity.Api.Services;
using DocIntegrity.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// OpenAPI
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    ));
    

// Services
builder.Services.AddScoped<IHashService, HashService>();

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

// Map controllers
app.MapControllers();

app.Run();