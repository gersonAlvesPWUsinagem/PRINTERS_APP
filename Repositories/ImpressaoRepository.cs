using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Data;
using PRINTERS_APP.Models;

namespace PRINTERS_APP.Repositories;

public interface IImpressaoRepository
{
    Task<(DocumentoRetido Documento, bool Criado)> AddDocumentoSeNaoExistirAsync(DocumentoRetido documento, CancellationToken cancellationToken);
    Task<List<DocumentoRetido>> ListarPendentesAsync(IReadOnlyCollection<string> identificadores, CancellationToken cancellationToken);
    Task<DocumentoRetido?> ObterPendenteDoUsuarioAsync(int id, IReadOnlyCollection<string> identificadores, CancellationToken cancellationToken);
    Task<ImpressoraConfig?> ObterImpressoraAtivaAsync(string destino, CancellationToken cancellationToken);
    Task<List<ImpressoraConfig>> ListarImpressorasAsync(CancellationToken cancellationToken);
    Task<ImpressoraConfig?> ObterImpressoraAsync(int id, CancellationToken cancellationToken);
    Task<ImpressoraConfig> AddImpressoraAsync(ImpressoraConfig impressora, CancellationToken cancellationToken);
    Task AtualizarDocumentoAsync(DocumentoRetido documento, CancellationToken cancellationToken);
    Task<DocumentoRetido?> ExcluirPendenteAsync(int id, IReadOnlyCollection<string> identificadores, CancellationToken cancellationToken);
}

public sealed class ImpressaoRepository(AppDbContext db) : IImpressaoRepository
{
    private static readonly SemaphoreSlim RecebimentoLock = new(1, 1);

    public async Task<(DocumentoRetido Documento, bool Criado)> AddDocumentoSeNaoExistirAsync(
        DocumentoRetido documento,
        CancellationToken cancellationToken)
    {
        await RecebimentoLock.WaitAsync(cancellationToken);
        try
        {
            // O mesmo documento só pode ser incluído novamente depois que sua linha
            // anterior tiver sido removida do SQLite.
            var candidatos = await db.DocumentosRetidos
                .AsNoTracking()
                .Where(x => x.Username == documento.Username &&
                    x.NomeArquivo == documento.NomeArquivo &&
                    x.NumeroPaginas == documento.NumeroPaginas)
                .ToListAsync(cancellationToken);

            var existente = candidatos.FirstOrDefault(x =>
                x.DadosArquivo.AsSpan().SequenceEqual(documento.DadosArquivo));
            if (existente is not null)
                return (existente, false);

            db.DocumentosRetidos.Add(documento);
            await db.SaveChangesAsync(cancellationToken);
            return (documento, true);
        }
        finally
        {
            RecebimentoLock.Release();
        }
    }

    public Task<List<DocumentoRetido>> ListarPendentesAsync(IReadOnlyCollection<string> identificadores, CancellationToken cancellationToken) =>
        db.DocumentosRetidos.AsNoTracking()
            .Where(x => x.Status == StatusDocumento.Pendente &&
                identificadores.Contains(x.Username.ToLower()))
            .OrderBy(x => x.DataEnvio)
            .ToListAsync(cancellationToken);

    public Task<DocumentoRetido?> ObterPendenteDoUsuarioAsync(int id, IReadOnlyCollection<string> identificadores, CancellationToken cancellationToken) =>
        db.DocumentosRetidos
            .FirstOrDefaultAsync(x => x.Id == id && x.Status == StatusDocumento.Pendente &&
                identificadores.Contains(x.Username.ToLower()), cancellationToken);

    public Task<ImpressoraConfig?> ObterImpressoraAtivaAsync(string destino, CancellationToken cancellationToken) =>
        db.ImpressorasConfig.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Ativa && (x.NomeImpressora == destino || x.IpFisico == destino), cancellationToken);

    public Task<List<ImpressoraConfig>> ListarImpressorasAsync(CancellationToken cancellationToken) =>
        db.ImpressorasConfig.AsNoTracking().Where(x => x.Ativa).OrderBy(x => x.NomeImpressora).ToListAsync(cancellationToken);

    public Task<ImpressoraConfig?> ObterImpressoraAsync(int id, CancellationToken cancellationToken) =>
        db.ImpressorasConfig.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<ImpressoraConfig> AddImpressoraAsync(ImpressoraConfig impressora, CancellationToken cancellationToken)
    {
        db.ImpressorasConfig.Add(impressora);
        await db.SaveChangesAsync(cancellationToken);
        return impressora;
    }

    public async Task AtualizarDocumentoAsync(DocumentoRetido documento, CancellationToken cancellationToken) =>
        await db.SaveChangesAsync(cancellationToken);

    public async Task<DocumentoRetido?> ExcluirPendenteAsync(int id, IReadOnlyCollection<string> identificadores, CancellationToken cancellationToken)
    {
        var documento = await db.DocumentosRetidos.FirstOrDefaultAsync(
            x => x.Id == id && x.Status == StatusDocumento.Pendente &&
                identificadores.Contains(x.Username.ToLower()),
            cancellationToken);
        if (documento is null)
            return null;

        db.DocumentosRetidos.Remove(documento);
        await db.SaveChangesAsync(cancellationToken);
        return documento;
    }
}
