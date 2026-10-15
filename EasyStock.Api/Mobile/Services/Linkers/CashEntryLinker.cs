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
    /// <summary>Chave que liga o movimento do ERP ao lançamento do PWA.</summary>
    public static string ReferenciaDe(CashEntry lancamento) => $"mobile:{lancamento.Id}";

    private static string TipoErp(CashEntry lancamento) =>
        string.Equals(lancamento.Type, "income", StringComparison.OrdinalIgnoreCase) ? "entrada" : "saida";

    /// <summary>
    /// Leva tipo, valor, descrição e forma do lançamento ao movimento. Usado ao promover e,
    /// desde o #1520, quando o lançamento é editado no PWA.
    /// </summary>
    public static void Espelhar(CashEntry lancamento, MovimentoCaixa movimento)
    {
        movimento.Tipo = TipoErp(lancamento);
        movimento.Valor = Math.Abs(lancamento.Amount);
        movimento.Descricao = lancamento.Description;
        movimento.Metodo = FormaPagamentoMobile.ParaErp(lancamento.Metodo);
    }

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
                // #1520: lancamento excluido no PWA antes de ser promovido nao entra no caixa do ERP.
                if (mobileCE.DeletedAt != null) { idempotentSkip++; continue; }
                if (mobileCE.ErpMovimentoCaixaId.HasValue && mobileCE.ErpMovimentoCaixaId.Value != Guid.Empty) { idempotentSkip++; continue; }

                var referencia = ReferenciaDe(mobileCE);
                var jaPromovido = await db.Set<MovimentoCaixa>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.EmpresaId == empresaId && m.Referencia == referencia);
                if (jaPromovido != null)
                {
                    mobileCE.ErpMovimentoCaixaId = jaPromovido.Id;
                    idempotentSkip++;
                    log.LogInformation("AutoLink MovimentoCaixa (idempotente): mobile={MobileId} → erp={ErpId}", ceid, jaPromovido.Id);
                    continue;
                }

                var tipo = TipoErp(mobileCE);
                var mov = MovimentoCaixa.Criar(empresaId.Value, tipo, mobileCE.Amount, mobileCE.CreatedAt, mobileCE.LojaId);
                Espelhar(mobileCE, mov);
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
