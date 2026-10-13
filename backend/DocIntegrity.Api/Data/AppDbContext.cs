using DocIntegrity.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocIntegrity.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Document> Documents { get; set; }

    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(document =>
        {
            // Borrar un usuario con documentos falla: sus registros no se pierden en silencio.
            document
                .HasOne(d => d.RegisteredByUser)
                .WithMany()
                .HasForeignKey(d => d.RegisteredByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Listado de la biblioteca de cada usuario, del más reciente al más antiguo.
            document
                .HasIndex(d => new { d.RegisteredByUserId, d.RegisteredAt });

            // Un usuario no puede tener dos veces el mismo archivo; usuarios distintos sí.
            // Es parcial: los registros históricos sin propietario (y sus duplicados) quedan fuera.
            document
                .HasIndex(d => new { d.RegisteredByUserId, d.Sha256 })
                .IsUnique()
                .HasFilter("\"RegisteredByUserId\" IS NOT NULL")
                .HasDatabaseName("UX_Documents_Owner_Sha256");
        });
    }
}