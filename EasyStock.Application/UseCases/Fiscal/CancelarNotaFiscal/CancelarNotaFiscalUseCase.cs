using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.Exceptions.Fiscal;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Fiscal.CancelarNotaFiscal;

/// <summary>
/// Cancela NFC-e dentro do prazo de 30 minutos. Sequência:
///  1. Carrega nota + valida tenant + status=Autorizada.
///  2. nota.IniciarCancelamento (validação de prazo + justificativa).
///  3. Persiste estado intermediário CancelamentoEmAndamento + COMMIT TX1.
///  4. Chama gateway (fora da TX original — pode ser longo).
///  5. Em sucesso → MarcarCancelada + outbox; em falha → ReverterCancelamento + alerta.
/// </summary>
public sealed class CancelarNotaFiscalUseCase(
    INotaFiscalRepository repo,
    IGatewayFiscal gateway,
    IConfigFiscalResolver configResolver,
    IPublicadorEventoIntegracao eventos,
    IUnitOfWork uow,
    ILogger<CancelarNotaFiscalUseCase> log)
    : IUseCase<CancelarNotaFiscalCommand, CancelarNotaFiscalResult>
{
    public async Task<CancelarNotaFiscalResult> ExecuteAsync(CancelarNotaFiscalCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.NotaFiscalId, nameof(cmd.NotaFiscalId));
        if (string.IsNullOrWhiteSpace(cmd.Justificativa) || cmd.Justificativa.Length is < 15 or > 255)
            throw new UseCaseValidationException("Justificativa deve ter entre 15 e 255 caracteres.");

        var ct = CancellationToken.None;

        var nota = await repo.ObterPorIdAsync(cmd.EmpresaId, cmd.NotaFiscalId, ct)
            ?? throw new UseCaseValidationException($"Nota fiscal {cmd.NotaFiscalId} nao encontrada.");

        if (nota.Status == StatusNotaFiscal.Cancelada)
            return From(nota);

        try
        {
            nota.IniciarCancelamento(cmd.Justificativa, cmd.UsuarioId ?? Guid.Empty, DateTime.UtcNow);
        }
        catch (PrazoCancelamentoExpiradoException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
        catch (TransicaoNotaFiscalInvalidaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await repo.AtualizarAsync(nota, ct);
        await uow.CommitAsync();

        // Chama gateway fora da TX original — operação pode ser lenta e não
        // pode segurar lock pessimista por 8s.
        var config = await configResolver.ResolverAsync(cmd.EmpresaId, nota.LojaId ?? Guid.Empty, ct);

        ResultadoCancelamentoNFCe resp;
        try
        {
            resp = await gateway.CancelarNFCeAsync(nota, cmd.Justificativa, config, ct);
        }
        catch (FocusUnreachableException ex)
        {
            log.LogError(ex, "Focus indisponivel ao cancelar nota {Id}. Status fica em CancelamentoEmAndamento — job retentará.", nota.Id);
            throw new UseCaseValidationException("Falha temporaria no gateway. Cancelamento ficara pendente.");
        }

        await uow.ExecuteInTransactionAsync(async token =>
        {
            if (resp.Sucesso)
            {
                nota.MarcarCancelada(
                    resp.Protocolo ?? "0",
                    resp.XmlEvento ?? "<canc/>",
                    resp.DhCancelamento ?? DateTime.UtcNow);
                await repo.AtualizarAsync(nota, token);
                await eventos.PublicarAsync(
                    empresaId: nota.EmpresaId,
                    tipoEvento: "nfce.cancelada",
                    aggregateType: nameof(NotaFiscal),
                    aggregateId: nota.Id,
                    payload: new
                    {
                        notaFiscalId = nota.Id,
                        chaveAcesso = nota.ChaveAcesso.Valor,
                        protocolo = nota.ProtocoloCancelamento,
                        dhCancelamento = nota.DataCancelamento,
                        justificativa = nota.JustificativaCancelamento,
                        usuarioId = cmd.UsuarioId,
                    },
                    payloadSchemaVersion: 1,
                    ct: token);
            }
            else
            {
                log.LogError("Cancelamento NFC-e {Id} rejeitado pelo gateway: {Codigo} {Motivo}",
                    nota.Id, resp.Codigo, resp.Motivo);
                nota.ReverterCancelamento(resp.Motivo ?? "Cancelamento rejeitado pela SEFAZ.");
                await repo.AtualizarAsync(nota, token);
            }
            await uow.CommitAsync();
        });

        if (!resp.Sucesso)
            throw new UseCaseValidationException($"Cancelamento rejeitado: {resp.Motivo}");

        return From(nota);
    }

    private static CancelarNotaFiscalResult From(NotaFiscal nota) =>
        new(
            NotaFiscalId: nota.Id,
            Status: nota.Status.ToString(),
            ProtocoloCancelamento: nota.ProtocoloCancelamento,
            DhCancelamento: nota.DataCancelamento);
}
