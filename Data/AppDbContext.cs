using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Models;

namespace PRINTERS_APP.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<DocumentoRetido> DocumentosRetidos => Set<DocumentoRetido>();
    public DbSet<ImpressoraConfig> ImpressorasConfig => Set<ImpressoraConfig>();
    public DbSet<PontoColeta> PontosColeta => Set<PontoColeta>();
    public DbSet<ApiLog> ApiLogs => Set<ApiLog>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RevokedToken>(entity =>
        {
            entity.ToTable("RevokedTokens");
            entity.HasKey(x => x.TokenHash);
            entity.Property(x => x.TokenHash).HasMaxLength(64);
            entity.HasIndex(x => x.ExpiresAtUtc);
        });


        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.ToTable("PERMISSAO_USER");
            entity.HasKey(x => x.RegistrationId);
            entity.Property(x => x.RegistrationId).HasColumnName("ID_REGISTRO");
            entity.Property(x => x.UserId).HasColumnName("ID_USUARIO");
            entity.Property(x => x.PermissionId).HasColumnName("ID_PERMISSAO");
            entity.Property(x => x.Level).HasColumnName("NIVEL");
            entity.Property(x => x.Code).HasColumnName("ID_COD");
            entity.Property(x => x.PermissionName).HasColumnName("PERMNOME");
            entity.Property(x => x.Module).HasColumnName("PERMMODULO");
            entity.HasIndex(x => new { x.UserId, x.Code, x.Module });
        });

        modelBuilder.Entity<DocumentoRetido>(entity =>
        {
            entity.ToTable("DocumentosRetidos");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Username).IsRequired().HasMaxLength(100);
            entity.Property(x => x.NomeArquivo).IsRequired().HasMaxLength(255);
            entity.Property(x => x.NumeroPaginas).IsRequired().HasDefaultValue(1);
            entity.Property(x => x.DadosArquivo).IsRequired();
            entity.Property(x => x.DataEnvio).IsRequired();
            entity.Property(x => x.Status).IsRequired().HasMaxLength(20).HasDefaultValue(StatusDocumento.Pendente);
            entity.Property(x => x.ImpressoraDestino).HasMaxLength(255);
            entity.HasIndex(x => new { x.Username, x.Status });
        });

        modelBuilder.Entity<ImpressoraConfig>(entity =>
        {
            entity.ToTable("ImpressorasConfig");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.NomeImpressora).IsRequired().HasMaxLength(150);
            entity.Property(x => x.IpFisico).IsRequired().HasMaxLength(255);
            entity.Property(x => x.Ativa).IsRequired().HasDefaultValue(true);
            entity.HasIndex(x => x.NomeImpressora).IsUnique();
            entity.HasIndex(x => x.IpFisico).IsUnique();
        });

        modelBuilder.Entity<PontoColeta>(entity =>
        {
            entity.ToTable("PontosColeta");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Nome).IsRequired().HasMaxLength(150);
            entity.Property(x => x.IpMaquina).IsRequired().HasMaxLength(45);
            entity.Property(x => x.Ativo).IsRequired().HasDefaultValue(true);
            entity.Property(x => x.TokenValidadeDias).IsRequired().HasDefaultValue(360);
            entity.HasIndex(x => x.Nome).IsUnique();
            entity.HasIndex(x => x.IpMaquina).IsUnique();
            entity.HasOne(x => x.Impressora)
                .WithMany()
                .HasForeignKey(x => x.ImpressoraId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApiLog>(entity =>
        {
            entity.ToTable("ApiLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.DataHora).IsRequired();
            entity.Property(x => x.Nivel).IsRequired().HasMaxLength(20);
            entity.Property(x => x.Evento).IsRequired().HasMaxLength(100);
            entity.Property(x => x.Mensagem).IsRequired().HasMaxLength(2000);
            entity.Property(x => x.Endpoint).HasMaxLength(500);
            entity.Property(x => x.Usuario).HasMaxLength(100);
            entity.Property(x => x.Detalhes).HasMaxLength(8000);
            entity.HasIndex(x => x.DataHora);
            entity.HasIndex(x => x.Evento);
        });
    }
}
