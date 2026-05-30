using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services.Linkers;

/// <summary>
/// Auto-linker: promove <c>CashEntry</c> mobile a <c>MovimentoCaixa</c> ERP.
///
/// Extraido do god-Service <c>SyncAutoLinker</c> (F8). Strategy isolada
/// pra economizar contexto e facilitar testes individuais. Facade continua
/// expondo o pipeline completo via RunAsync + BackfillAsync.
/// </summary>
public sealed class CashEntryLinker(
    EasyStockDbContext db,
    ILogger<CashEntryLinker> log)
{
    public async Task ExecuteAsync(IEnumerable<string> mobileCashIds, Guid? empresaId)
    {
        var idsList = mobileCashIds as ICollection<string> ?? mobileCashIds.ToList();
        if (!empresaId.HasValue)
        {
            log.LogWarning(
                "AutoLink MovimentoCaixa SKIPPED: device nao pareado (empresaId=null), {Count} entradas ficam orfas em mobile_cash_entries",
                idsList.Count);
            return;
        }
        var created = 0;
        var idempotentSkip = 0;
        var errorSkip = 0;
        foreach (var ceid in idsList)
        {
            try
            {
                var mobileCE = await db.Set<CashEntry>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == ceid && c.EmpresaId == empresaId);
                if (mobileCE == null) { idempotentSkip++; continue; }
                if (mobileCE.ErpMovimentoCaixaId.HasValue && mobileCE.ErpMovimentoCaixaId.Value != Guid.Empty) { idempotentSkip++; continue; }

                var referencia = $"mobile:{mobileCE.Id}";
                var jaPromovido = await db.Set<MovimentoCaixa>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.EmpresaId == empresaId && m.Referencia == referencia);
                if (jaPromovido != null)
                {
                    mobileCE.ErpMovimentoCaixaId = jaPromovido.Id;
                    idempotentSkip++;
                    log.LogInformation("AutoLink MovimentoCaixa (idempotente): mobile={MobileId} → erp={ErpId}", ceid, jaPromovido.Id);
                    continue;
                }

                var tipo = string.Equals(mobileCE.Type, "income", StringComparison.OrdinalIgnoreCase) ? "entrada" : "saida";
                var mov = MovimentoCaixa.Criar(empresaId.Value, tipo, mobileCE.Amount, mobileCE.CreatedAt, mobileCE.LojaId);
                mov.Descricao = mobileCE.Description;
                mov.Metodo = "dinheiro";
                mov.Origem = "mobile";
                mov.RegistradoPorNome = mobileCE.LastOperatorName;
                mov.Referencia = referencia;

                db.Add(mov);
                mobileCE.ErpMovimentoCaixaId = mov.Id;
                if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
                created++;
                log.LogInformation("AutoLink MovimentoCaixa CRIADO: mobile={MobileId} → erp={ErpId} tipo={Tipo} valor={Valor}",
                    ceid, mov.Id, tipo, mov.Valor);
            }
            catch (Exception ex)
            {
                errorSkip++;
                log.LogError(ex,
                    "AutoLink MovimentoCaixa FALHOU mobile={MobileId} empresaId={EmpresaId} exType={ExType}: {Mensagem}",
                    ceid, empresaId, ex.GetType().Name, ex.Message);
            }
        }
        log.LogInformation(
            "AutoLink MovimentoCaixa summary empresaId={EmpresaId} total={Total} created={Created} idempotent={Idempotent} errors={Errors}",
            empresaId, idsList.Count, created, idempotentSkip, errorSkip);
    }
}
