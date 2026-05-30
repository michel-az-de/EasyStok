using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services.Linkers;

/// <summary>
/// Auto-linker: matcheia <c>Client</c> mobile com <c>Cliente</c> ERP existente
/// (por nome ILIKE ou telefone) ou cria novo. Loga em ClienteAlteracao.
///
/// Extraido do god-Service <c>SyncAutoLinker</c> (F8).
/// </summary>
public sealed class ClientLinker(
    EasyStockDbContext db,
    ILogger<ClientLinker> log)
{
    public async Task ExecuteAsync(IEnumerable<string> mobileClientIds, Guid? empresaId)
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

                Cliente? match = await baseQuery
                    .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Nome, mobileC.Name));

                if (match == null && !string.IsNullOrWhiteSpace(mobileC.Phone))
                {
                    var phone = mobileC.Phone;
                    match = await baseQuery.FirstOrDefaultAsync(c => c.Telefone == phone);
                }

                if (match != null)
                {
                    mobileC.ErpClienteId = match.Id;
                    matched++;
                    log.LogInformation("AutoLink Cliente matched: mobile={MobileId} → erp={ErpId}", cid, match.Id);
                    db.Add(new ClienteAlteracao
                    {
                        Id = Guid.NewGuid(),
                        EmpresaId = empresaId.Value,
                        ClienteId = match.Id,
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
