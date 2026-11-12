using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services.Linkers;

/// <summary>
/// Auto-linker: matcheia <c>Client</c> mobile com <c>Cliente</c> ERP existente
/// (por telefone normalizado; por nome só quando nenhum dos dois tem telefone) ou cria novo. Loga em ClienteAlteracao.
///
/// Extraido do god-Service <c>SyncAutoLinker</c> (F8).
/// </summary>
public sealed class ClientLinker(
    EasyStockDbContext db,
    ILogger<ClientLinker> log)
{
    public async Task ExecuteAsync(IEnumerable<string> mobileClientIds, Guid? empresaId,
        IReadOnlyDictionary<string, string>? documentos = null)
    {
        var idsList = mobileClientIds as ICollection<string> ?? mobileClientIds.ToList();
        if (!empresaId.HasValue)
        {
            log.LogWarning(
                "AutoLink Cliente SKIPPED: device nao pareado (empresaId=null), {Count} clientes ficam orfaos em mobile_clients",
                idsList.Count);
            return;
        }
        var matched = 0;
        var created = 0;
        var idempotentSkip = 0;
        var errorSkip = 0;
        foreach (var cid in idsList)
        {
            try
            {
                var mobileC = await db.Set<Client>()
                    .FirstOrDefaultAsync(c => c.Id == cid && c.EmpresaId == empresaId);
                if (mobileC == null || mobileC.ErpClienteId.HasValue) { idempotentSkip++; continue; }

                var baseQuery = db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.EmpresaId == empresaId && c.Ativo);

                var chaveApp = ChaveDeTelefone.De(mobileC.Phone);
                var candidatos = await baseQuery
                    .Select(c => new { c.Id, c.Nome, c.Telefone })
                    .ToListAsync();
                // Telefone manda: só casa por nome quando nenhum dos dois tem telefone (CLI-7, CLI-6).
                var matchId = chaveApp.Length > 0
                    ? candidatos.FirstOrDefault(c => ChaveDeTelefone.De(c.Telefone) == chaveApp)?.Id
                    : candidatos.FirstOrDefault(c => ChaveDeTelefone.De(c.Telefone).Length == 0
                        && string.Equals(c.Nome, mobileC.Name, StringComparison.OrdinalIgnoreCase))?.Id;

                // CLI-5: documento que o app mandou neste envio (mobile_clients nao guarda).
                string? documento = null;
                if (documentos != null && documentos.TryGetValue(cid, out var bruto))
                    documento = DocumentoClienteMobile.Normalizar(bruto);

                if (matchId is { } erpMatchId)
                {
                    mobileC.ErpClienteId = erpMatchId;
                    if (documento != null)
                    {
                        var existente = await db.Set<Cliente>().IgnoreQueryFilters()
                            .FirstOrDefaultAsync(c => c.Id == erpMatchId && c.EmpresaId == empresaId);
                        if (existente != null) existente.Documento = documento;
                    }
                    matched++;
                    log.LogInformation("AutoLink Cliente matched: mobile={MobileId} → erp={ErpId}", cid, erpMatchId);
                    db.Add(new ClienteAlteracao
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = empresaId.Value,
                        ClienteId = erpMatchId,
                        Campo = "vinculacao_mobile",
                        ValorAntigo = null,
                        ValorNovo = $"mobile_client={cid}",
                        AlteradoEm = DateTime.UtcNow,
                        Origem = "mobile"
                    });
                    continue;
                }

                var novoC = Cliente.Criar(empresaId.Value, mobileC.Name);
                novoC.Apt = mobileC.Apt;
                novoC.Endereco = mobileC.Address;
                novoC.Telefone = mobileC.Phone;
                novoC.LastOrderAt = mobileC.LastOrder;
                novoC.OrderCount = mobileC.OrderCount;
                novoC.Documento = documento;
                db.Add(novoC);
                mobileC.ErpClienteId = novoC.Id;
                db.Add(new ClienteAlteracao
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId.Value,
                    ClienteId = novoC.Id,
                    Campo = "criado",
                    ValorAntigo = null,
                    ValorNovo = $"Nome={mobileC.Name}; Telefone={mobileC.Phone}; mobile_client={cid}",
                    AlteradoEm = DateTime.UtcNow,
                    Origem = "mobile"
                });
                created++;
                log.LogInformation("AutoLink Cliente CRIADO: mobile={MobileId} → erp={ErpId} ({Nome})",
                    cid, novoC.Id, mobileC.Name);
            }
            catch (Exception ex)
            {
                errorSkip++;
                log.LogError(ex,
                    "AutoLink Cliente FALHOU mobile={MobileId} empresaId={EmpresaId} exType={ExType}: {Mensagem}",
                    cid, empresaId, ex.GetType().Name, ex.Message);
            }
        }
        log.LogInformation(
            "AutoLink Cliente summary empresaId={EmpresaId} total={Total} matched={Matched} created={Created} idempotent={Idempotent} errors={Errors}",
            empresaId, idsList.Count, matched, created, idempotentSkip, errorSkip);
    }
}
