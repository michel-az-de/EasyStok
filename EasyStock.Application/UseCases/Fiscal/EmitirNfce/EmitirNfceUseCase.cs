using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Fiscal.EmitirNfce;

/// <summary>
/// Emite uma NFC-e em 3 atos para evitar o anti-padrao "HTTP em transacao":
/// <list type="number">
///   <item>
///     <b>Tx 1:</b> Reserva de numero + cria <see cref="NfeDocumento"/>
///     em <see cref="StatusNfe.Rascunho"/>, depois <c>MarcarEnviada()</c>. Commit.
///   </item>
///   <item>
///     <b>HTTP:</b> chama <see cref="IGatewayFiscal.EmitirAsync"/> FORA da transacao.
///   </item>
///   <item>
///     <b>Tx 2:</b> Aplica resultado (<c>MarcarAutorizada/MarcarRejeitada/MarcarFalhaTransiente</c>)
///     e commita.
///   </item>
/// </list>
///
/// <para>
/// <b>Idempotencia:</b> Por enquanto, idempotencia HTTP-level fica a cargo do middleware
/// <see cref="IIdempotencyKeyRepository"/>. Migration F1.5 (AddNfeF1RepoIndexes) adicionara
/// coluna <c>IdempotencyKey</c> em <c>nfe_documentos</c> para hardening defensivo no DB.
/// Ate la, controller deve confiar no middleware.
/// </para>
/// </summary>
public class EmitirNfceUseCase(
    INfeRepository nfeRepo,
    INumeracaoNfeService numeracao,
    IGeradorChaveAcesso geradorChave,
    IGatewayFiscal gateway,
    IConfigFiscalResolver configResolver,
    IUnitOfWork uow,
    ILogger<EmitirNfceUseCase> logger) : IUseCase<EmitirNfceCommand, EmitirNfceResult>
{
    public async Task<EmitirNfceResult> ExecuteAsync(EmitirNfceCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");
        if (string.IsNullOrWhiteSpace(cmd.IdempotencyKey))
            throw new UseCaseValidationException("IdempotencyKey obrigatoria.");
        if (cmd.TotalNota <= 0m)
            throw new UseCaseValidationException("Total da nota deve ser maior que zero.");
        if (cmd.Itens.Count == 0)
            throw new UseCaseValidationException("Itens obrigatorios.");

        var config = await configResolver.ResolveAsync(cmd.EmpresaId);

        // === Tx 1: reservar numero, criar NfeDocumento Rascunho->EnviadaAguardando ===
        var nfeId = await uow.ExecuteInTransactionAsync(async _ =>
        {
            var (serie, numero) = await numeracao.ReservarProximoNumeroAsync(cmd.EmpresaId);

            var ufEmitente = config.Endereco?.Uf
                ?? throw new UseCaseValidationException("UF do emitente obrigatoria na configuracao fiscal.");

            var chave = geradorChave.Gerar(
                uf: ufEmitente,
                cnpjEmitente: config.Cnpj,
                serie: serie,
                numero: numero,
                dataEmissao: DateTime.UtcNow,
                modeloFiscal: NfeDocumento.ModeloNfce,
                tipoEmissao: 1);

            var nfe = NfeDocumento.Criar(
                empresaId: cmd.EmpresaId,
                pedidoId: cmd.PedidoId,
                serie: serie,
                numero: numero,
                dadosEmitente: MapEmitente(cmd.Emitente),
                dadosDestinatario: MapDestinatario(cmd.Destinatario),
                totalNota: Dinheiro.FromDecimal(cmd.TotalNota),
                usuarioId: cmd.UsuarioId,
                usuarioNome: cmd.UsuarioNome,
                origem: cmd.Origem);

            nfe.ChaveAcesso = chave;

            foreach (var item in cmd.Itens)
            {
                nfe.AdicionarItem(
                    nomeSnapshot: item.NomeSnapshot,
                    quantidade: item.Quantidade,
                    precoUnitario: Dinheiro.FromDecimal(item.PrecoUnitario),
                    unidade: item.Unidade,
                    ncm: item.Ncm,
                    cfop: item.Cfop,
                    produtoIdSnapshot: item.ProdutoIdSnapshot,
                    origemMercadoria: item.OrigemMercadoria,
                    cstOuCsosn: item.CstOuCsosn);
            }

            nfe.MarcarEnviada(cmd.UsuarioId, cmd.UsuarioNome, cmd.Origem);

            await nfeRepo.AddAsync(nfe);
            return nfe.Id;
        });

        // === HTTP: chamada Focus FORA de transacao (anti-padrao B-052 evitado) ===
        var nfePreEnvio = await nfeRepo.GetByIdWithDetailsAsync(cmd.EmpresaId, nfeId)
            ?? throw new InvalidOperationException($"NfeDocumento {nfeId} sumiu apos criacao.");

        try
        {
            var resultado = await gateway.EmitirAsync(nfePreEnvio, config);

            // === Tx 2: aplicar autorizacao ===
            await uow.ExecuteInTransactionAsync(async _ =>
            {
                var nfe = await nfeRepo.GetByIdAsync(cmd.EmpresaId, nfeId)
                    ?? throw new InvalidOperationException($"NfeDocumento {nfeId} sumiu antes do commit.");

                nfe.MarcarAutorizada(
                    chaveAcesso: resultado.ChaveAcesso,
                    protocoloAutorizacao: resultado.ProtocoloAutorizacao,
                    xmlAssinadoStorageKey: resultado.XmlAssinadoUrl,
                    danfeUrl: resultado.DanfeUrl,
                    usuarioId: cmd.UsuarioId,
                    usuarioNome: cmd.UsuarioNome,
                    origem: cmd.Origem);

                await nfeRepo.UpdateAsync(nfe);
            });

            logger.LogInformation("Nfe {Id} autorizada. Chave={Chave}.", nfeId, resultado.ChaveAcesso);
        }
        catch (GatewayFiscalRejeitadaException rej)
        {
            await uow.ExecuteInTransactionAsync(async _ =>
            {
                var nfe = await nfeRepo.GetByIdAsync(cmd.EmpresaId, nfeId)
                    ?? throw new InvalidOperationException($"NfeDocumento {nfeId} sumiu antes do rollback Rejeitada.");

                nfe.MarcarRejeitada(rej.Motivo, cmd.UsuarioId, cmd.UsuarioNome, cmd.Origem);
                await nfeRepo.UpdateAsync(nfe);
            });
            logger.LogWarning("Nfe {Id} rejeitada por SEFAZ: {Codigo} {Motivo}.", nfeId, rej.Codigo, rej.Motivo);
        }
        catch (GatewayFiscalTransienteException trans)
        {
            await uow.ExecuteInTransactionAsync(async _ =>
            {
                var nfe = await nfeRepo.GetByIdAsync(cmd.EmpresaId, nfeId)
                    ?? throw new InvalidOperationException($"NfeDocumento {nfeId} sumiu antes do rollback FalhaTransiente.");

                nfe.MarcarFalhaTransiente(trans.Message, cmd.UsuarioId, cmd.UsuarioNome, cmd.Origem);
                await nfeRepo.UpdateAsync(nfe);
            });
            logger.LogWarning(trans, "Nfe {Id} em FalhaTransiente — job de contingencia ira reprocessar.", nfeId);
        }

        var nfeFinal = await nfeRepo.GetByIdAsync(cmd.EmpresaId, nfeId)
            ?? throw new InvalidOperationException($"NfeDocumento {nfeId} sumiu apos commit final.");
        return ToResult(nfeFinal);
    }

    private static EmitirNfceResult ToResult(NfeDocumento nfe) => new(
        NfeId: nfe.Id,
        Status: nfe.Status,
        ChaveAcesso: nfe.ChaveAcesso,
        ProtocoloAutorizacao: nfe.ProtocoloAutorizacao,
        DataAutorizacao: nfe.DataAutorizacao,
        MotivoRejeicao: nfe.MotivoRejeicao,
        DanfeUrl: nfe.DanfeUrl);

    private static DadosEmissor MapEmitente(DadosEmitenteInput input) => new(
        Nome: !string.IsNullOrWhiteSpace(input.RazaoSocial) ? input.RazaoSocial : input.NomeFantasia ?? input.Cnpj,
        Documento: input.Cnpj,
        RazaoSocial: input.RazaoSocial,
        InscricaoMunicipal: input.InscricaoMunicipal,
        InscricaoEstadual: input.InscricaoEstadual);

    private static DadosFaturado? MapDestinatario(DadosDestinatarioInput? input) =>
        input is null
            ? null
            : new DadosFaturado(
                Nome: input.Nome ?? "Consumidor",
                Documento: input.CpfCnpj,
                Email: input.Email);
}
