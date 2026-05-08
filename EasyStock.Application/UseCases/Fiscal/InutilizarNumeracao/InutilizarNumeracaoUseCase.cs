using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Fiscal.InutilizarNumeracao;

public sealed class InutilizarNumeracaoUseCase(
    INotaFiscalRepository repo,
    IGatewayFiscal gateway,
    IConfigFiscalResolver configResolver,
    IPublicadorEventoIntegracao eventos,
    IUnitOfWork uow,
    ILogger<InutilizarNumeracaoUseCase> log)
    : IUseCase<InutilizarNumeracaoCommand, InutilizarNumeracaoResult>
{
    public async Task<InutilizarNumeracaoResult> ExecuteAsync(InutilizarNumeracaoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.LojaId, nameof(cmd.LojaId));
        if (cmd.NumeroFinal < cmd.NumeroInicial)
            throw new UseCaseValidationException("NumeroFinal deve ser maior ou igual a NumeroInicial.");
        if (string.IsNullOrWhiteSpace(cmd.Justificativa) || cmd.Justificativa.Length is < 15 or > 255)
            throw new UseCaseValidationException("Justificativa deve ter entre 15 e 255 caracteres.");

        var ct = CancellationToken.None;

        // Não inutilizar números já usados (regra SEFAZ — se foi emitido, não inutiliza).
        var usados = await repo.ListarNumerosUsadosAsync(
            cmd.EmpresaId, cmd.LojaId, ModeloDocumentoFiscal.NFCe, cmd.Serie,
            cmd.NumeroInicial, cmd.NumeroFinal, ct);
        if (usados.Count > 0)
            throw new UseCaseValidationException(
                $"Faixa contém números já utilizados: {string.Join(", ", usados.Take(5))}{(usados.Count > 5 ? ", ..." : "")}.");

        var inut = NotaFiscalInutilizacao.Criar(
            empresaId: cmd.EmpresaId,
            lojaId: cmd.LojaId,
            modelo: ModeloDocumentoFiscal.NFCe,
            serie: cmd.Serie,
            numeroInicial: cmd.NumeroInicial,
            numeroFinal: cmd.NumeroFinal,
            ano: cmd.Ano,
            justificativa: cmd.Justificativa);

        await repo.AdicionarInutilizacaoAsync(inut, ct);
        await uow.CommitAsync();

        var config = await configResolver.ResolverAsync(cmd.EmpresaId, cmd.LojaId, ct);

        ResultadoInutilizacaoNFCe resp;
        try
        {
            resp = await gateway.InutilizarNumeracaoAsync(inut, config, ct);
        }
        catch (FocusUnreachableException ex)
        {
            log.LogError(ex, "Focus indisponivel ao inutilizar {Id}. Mantem EmAndamento.", inut.Id);
            throw new UseCaseValidationException("Falha temporaria no gateway. Tente novamente.");
        }

        if (resp.Sucesso)
        {
            inut.MarcarAutorizada(resp.Protocolo ?? "0", resp.XmlEvento ?? "<inut/>");
            await repo.AtualizarInutilizacaoAsync(inut, ct);
            await eventos.PublicarAsync(
                empresaId: inut.EmpresaId,
                tipoEvento: "nfce.numeracao.inutilizada",
                aggregateType: nameof(NotaFiscalInutilizacao),
                aggregateId: inut.Id,
                payload: new
                {
                    inutilizacaoId = inut.Id,
                    lojaId = inut.LojaId,
                    serie = inut.Serie,
                    numeroInicial = inut.NumeroInicial,
                    numeroFinal = inut.NumeroFinal,
                    ano = inut.Ano,
                    protocolo = inut.ProtocoloInutilizacao,
                },
                ct: ct);
        }
        else
        {
            inut.MarcarRejeitada(resp.Motivo ?? "Rejeitada pela SEFAZ.");
            await repo.AtualizarInutilizacaoAsync(inut, ct);
        }

        await uow.CommitAsync();

        return new InutilizarNumeracaoResult(
            InutilizacaoId: inut.Id,
            Status: inut.Status.ToString(),
            Protocolo: inut.ProtocoloInutilizacao,
            Motivo: inut.MotivoRejeicao);
    }
}
