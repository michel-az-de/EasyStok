using EasyStock.Api.Services.Helpdesk;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Services.Faturacao;

/// <summary>
/// Implementacao de <see cref="IFalhaPagamentoNotifier"/>: registra a falha
/// como <see cref="TipoEventoFatura.PagamentoFalhou"/> na fatura e — quando
/// o limiar de 3 falhas em 7 dias e atingido — abre ticket admin
/// categoria=Financeiro/Alta vinculado a fatura.
///
/// <para>
/// Threshold conservador (3 em 7d) escolhido para nao gerar ticket em
/// flutuacao normal de tentativas (Pix expira, cliente tenta pagar, etc.).
/// Ajustar via <c>Faturacao:AutoTicketLimiar</c> e <c>Faturacao:AutoTicketJanelaDias</c>
/// se necessario.
/// </para>
/// </summary>
public sealed class AutoTicketFalhaPagamento(
    EasyStockDbContext db,
    HelpdeskTicketService ticketService,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    ILogger<AutoTicketFalhaPagamento> logger) : IFalhaPagamentoNotifier
{
    public async Task RegistrarFalhaAsync(
        Guid empresaId, Guid? faturaId, string motivo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo)) motivo = "(sem motivo)";

        if (!faturaId.HasValue || faturaId.Value == Guid.Empty)
        {
            logger.LogWarning(
                "AutoTicketFalhaPagamento: falha sem fatura linkada. EmpresaId={EmpresaId} Motivo={Motivo}",
                empresaId, motivo);
            return;
        }

        var fatura = await db.Faturas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == faturaId.Value, ct);
        if (fatura is null)
        {
            logger.LogWarning("AutoTicketFalhaPagamento: fatura {FaturaId} nao encontrada.", faturaId);
            return;
        }

        // 1) Audita a falha em FaturaEvento.
        db.FaturaEventos.Add(FaturaEvento.Criar(
            fatura.Id,
            TipoEventoFatura.PagamentoFalhou,
            origem: "auto-ticket",
            valorDepois: motivo));
        await db.SaveChangesAsync(ct);

        // 2) Se a fatura ja tem ticket vinculado, nao duplica.
        if (fatura.TicketRelacionadoId.HasValue)
        {
            logger.LogDebug(
                "AutoTicketFalhaPagamento: fatura {FaturaId} ja tem ticket {TicketId} — nao duplicando.",
                fatura.Id, fatura.TicketRelacionadoId);
            return;
        }

        // 3) Threshold — quantas falhas nos ultimos N dias para esta fatura?
        var limiar = configuration.GetValue("Faturacao:AutoTicketLimiar", 3);
        var janelaDias = configuration.GetValue("Faturacao:AutoTicketJanelaDias", 7);
        var inicio = DateTime.UtcNow.AddDays(-janelaDias);

        var falhas = await db.FaturaEventos
            .IgnoreQueryFilters()
            .CountAsync(e => e.FaturaId == fatura.Id
                && e.Tipo == TipoEventoFatura.PagamentoFalhou
                && e.OcorridoEm >= inicio, ct);

        if (falhas < limiar)
        {
            logger.LogDebug(
                "AutoTicketFalhaPagamento: fatura {FaturaId} tem {N} falhas em {Dias}d (limiar {Limiar}) — sem ticket ainda.",
                fatura.Id, falhas, janelaDias, limiar);
            return;
        }

        // 4) Abre ticket interno categoria=Financeiro / Alta.
        try
        {
            var ticket = await ticketService.AbrirAsync(new AbrirAdminTicketCommand(
                EmpresaId: fatura.EmpresaId,
                Titulo: $"Falhas repetidas no pagamento da fatura {fatura.Numero}",
                Descricao: $"Detectadas {falhas} falhas de pagamento nos ultimos {janelaDias} dias para esta fatura.\n" +
                           $"Ultima falha: {motivo}\n\n" +
                           $"Acao sugerida: contatar cliente, validar dados, eventualmente cancelar e reemitir.",
                Categoria: TicketCategoria.Financeiro,
                Prioridade: TicketPrioridade.Alta,
                Nivel: NivelAtendimento.N2,
                FaturaId: fatura.Id
            ), ct);

            logger.LogInformation(
                "AutoTicketFalhaPagamento: ticket {TicketId} aberto automaticamente para fatura {FaturaId} ({N} falhas em {Dias}d).",
                ticket.Id, fatura.Id, falhas, janelaDias);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "AutoTicketFalhaPagamento: falha ao abrir ticket automatico para fatura {FaturaId}.",
                fatura.Id);
        }
    }
}
