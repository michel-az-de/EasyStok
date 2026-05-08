using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Fiscal.ReprocessarContingencia;

/// <summary>
/// Tenta retransmitir uma nota em contingência ao Focus. Chamado pelo
/// ReprocessarContingenciaJob a cada 5min. Em sucesso → MarcarAutorizadaPosContingencia
/// + outbox. Em falha temporária → deixa pra próxima rodada. Após 24h
/// sem sucesso → MarcarRejeitada + alerta crítico.
/// </summary>
public sealed class ReprocessarContingenciaUseCase(
    INotaFiscalRepository repo,
    IGatewayFiscal gateway,
    IConfigFiscalResolver configResolver,
    IPublicadorEventoIntegracao eventos,
    IUnitOfWork uow,
    ILogger<ReprocessarContingenciaUseCase> log)
    : IUseCase<ReprocessarContingenciaCommand, ReprocessarContingenciaResult>
{
    public async Task<ReprocessarContingenciaResult> ExecuteAsync(ReprocessarContingenciaCommand cmd)
    {
        var ct = CancellationToken.None;

        var nota = await repo.ObterPorIdComItensAsync(cmd.EmpresaId, cmd.NotaFiscalId, ct);
        if (nota is null)
            return new ReprocessarContingenciaResult(cmd.NotaFiscalId, "NotFound", null, "Nota nao encontrada");

        if (nota.Status != StatusNotaFiscal.EmContingencia)
            return new ReprocessarContingenciaResult(nota.Id, nota.Status.ToString(), null, "Nao em contingencia");

        var idade = DateTime.UtcNow - nota.DataEmissao;
        if (idade.TotalHours > 24)
        {
            log.LogError("Contingencia expirada {Id} idade={Horas}h", nota.Id, idade.TotalHours);
            await uow.ExecuteInTransactionAsync(async token =>
            {
                nota.MarcarRejeitada("CONT_EXPIRADA", "Contingencia nao transmitida em 24h.");
                await repo.AtualizarAsync(nota, token);
                await eventos.PublicarAsync(
                    empresaId: nota.EmpresaId,
                    tipoEvento: "nfce.contingencia.expirada",
                    aggregateType: nameof(NotaFiscal),
                    aggregateId: nota.Id,
                    payload: new
                    {
                        notaFiscalId = nota.Id,
                        chaveAcesso = nota.ChaveAcesso.Valor,
                        dhEmi = nota.DataEmissao,
                        horasSemTransmissao = idade.TotalHours,
                        lojaId = nota.LojaId,
                    },
                    ct: token);
                await uow.CommitAsync();
            });
            return new ReprocessarContingenciaResult(nota.Id, nota.Status.ToString(), null, "Expirada");
        }

        var config = await configResolver.ResolverAsync(nota.EmpresaId, nota.LojaId ?? Guid.Empty, ct);

        ResultadoEmissaoNFCe resp;
        try
        {
            resp = await gateway.RetransmitirContingenciaAsync(nota, config, ct);
        }
        catch (FocusUnreachableException)
        {
            log.LogInformation("Focus ainda indisponivel para nota {Id} — proxima rodada.", nota.Id);
            return new ReprocessarContingenciaResult(nota.Id, nota.Status.ToString(), null, "Focus indisponivel");
        }

        await uow.ExecuteInTransactionAsync(async token =>
        {
            switch (resp.Resultado)
            {
                case ResultadoEmissao.Autorizada:
                    nota.MarcarAutorizadaPosContingencia(
                        resp.Protocolo ?? "0",
                        resp.XmlAutorizado ?? "<auth/>",
                        resp.DhAutorizacao ?? DateTime.UtcNow);
                    await repo.AtualizarAsync(nota, token);
                    await eventos.PublicarAsync(
                        empresaId: nota.EmpresaId,
                        tipoEvento: "nfce.contingencia.transmitida",
                        aggregateType: nameof(NotaFiscal),
                        aggregateId: nota.Id,
                        payload: new
                        {
                            notaFiscalId = nota.Id,
                            chaveAcesso = nota.ChaveAcesso.Valor,
                            protocolo = nota.ProtocoloAutorizacao,
                            dhAutorizacao = nota.DataAutorizacao,
                        },
                        ct: token);
                    break;

                case ResultadoEmissao.Rejeitada:
                case ResultadoEmissao.Denegada:
                    nota.MarcarRejeitada(resp.Codigo ?? "REJ_CONT",
                        resp.Motivo ?? "Rejeicao apos contingencia.");
                    await repo.AtualizarAsync(nota, token);
                    await eventos.PublicarAsync(
                        empresaId: nota.EmpresaId,
                        tipoEvento: "nfce.rejeitada_apos_contingencia",
                        aggregateType: nameof(NotaFiscal),
                        aggregateId: nota.Id,
                        payload: new
                        {
                            notaFiscalId = nota.Id,
                            chaveAcesso = nota.ChaveAcesso.Valor,
                            codigo = resp.Codigo,
                            motivo = resp.Motivo,
                        },
                        ct: token);
                    break;
            }
            await uow.CommitAsync();
        });

        return new ReprocessarContingenciaResult(
            nota.Id,
            nota.Status.ToString(),
            nota.ProtocoloAutorizacao,
            resp.Motivo);
    }
}
