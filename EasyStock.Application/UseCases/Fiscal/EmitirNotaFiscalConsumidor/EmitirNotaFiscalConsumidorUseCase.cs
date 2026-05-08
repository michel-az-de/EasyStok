using System.Text.Json;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.ValueObjects.Fiscal;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Fiscal.EmitirNotaFiscalConsumidor;

/// <summary>
/// Caso de uso de emissão de NFC-e (modelo 65). Sequência:
///  1. Idempotência por (empresa, loja, pedido).
///  2. Carrega Pedido e valida tenant + ausência de NFC-e prévia.
///  3. Resolve config fiscal (emitente, ambiente, série).
///  4. Reserva próximo número (FOR UPDATE em nota_fiscal_contador).
///  5. Constrói chave de acesso 44d com DV módulo 11.
///  6. Cria NotaFiscal status=EmEmissao + itens + pagamentos.
///  7. Chama gateway. Em timeout/5xx persistente → contingência (tpEmis=9).
///  8. Aplica resultado na entity (state machine valida).
///  9. Publica evento outbox (nfce.autorizada / .rejeitada / .denegada / .contingencia.iniciada).
/// 10. Commit.
/// </summary>
public sealed class EmitirNotaFiscalConsumidorUseCase(
    INotaFiscalRepository notaRepo,
    IPedidoRepository pedidoRepo,
    IGatewayFiscal gateway,
    INumeracaoNotaFiscalService numeracao,
    IGeradorChaveAcesso geradorChave,
    IConfigFiscalResolver configResolver,
    IPublicadorEventoIntegracao eventos,
    IUnitOfWork uow,
    ILogger<EmitirNotaFiscalConsumidorUseCase> log)
    : IUseCase<EmitirNotaFiscalConsumidorCommand, EmitirNotaFiscalConsumidorResult>
{
    public async Task<EmitirNotaFiscalConsumidorResult> ExecuteAsync(EmitirNotaFiscalConsumidorCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, nameof(cmd.PedidoId));
        UseCaseGuards.EnsureNotEmpty(cmd.LojaId, nameof(cmd.LojaId));
        if (cmd.Pagamentos is null || cmd.Pagamentos.Count == 0)
            throw new UseCaseValidationException("Pelo menos um pagamento é obrigatório.");
        if (cmd.Pagamentos.Any(p => p.Valor <= 0))
            throw new UseCaseValidationException("Valores de pagamento devem ser positivos.");

        var idempotKey = $"{cmd.EmpresaId:N}:{cmd.LojaId:N}:{cmd.PedidoId:N}";
        var ct = CancellationToken.None;

        // 1. Idempotência
        var existente = await notaRepo.ObterPorIdempotencyKeyAsync(cmd.EmpresaId, idempotKey, ct);
        if (existente is not null)
        {
            log.LogInformation("NFC-e ja emitida idempot {Key} → {Id}", idempotKey, existente.Id);
            return EmitirNotaFiscalConsumidorResult.From(existente);
        }

        // 2. Pedido
        var pedido = await pedidoRepo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId)
            ?? throw new UseCaseValidationException($"Pedido {cmd.PedidoId} nao encontrado.");

        if (pedido.EmpresaId != cmd.EmpresaId)
            throw new UseCaseValidationException("Pedido pertence a outro tenant.");

        if (pedido.Itens is null || pedido.Itens.Count == 0)
            throw new UseCaseValidationException("Pedido sem itens.");

        // 3. Config
        var config = await configResolver.ResolverAsync(cmd.EmpresaId, cmd.LojaId, ct);

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            // 4. Reserva número
            var numero = await numeracao.ReservarProximoNumeroAsync(
                cmd.EmpresaId, cmd.LojaId, ModeloDocumentoFiscal.NFCe, config.Serie, token);

            // 5. Chave de acesso
            var cNumerico = geradorChave.GerarCodigoNumerico8();
            var dataEmissao = DateTime.UtcNow;
            var chave = ChaveAcessoNFe.Construir(
                ufCodigoIbge: config.UfCodigoIbge,
                dhEmi: dataEmissao,
                cnpj: config.CnpjEmitente,
                modelo: ModeloDocumentoFiscal.NFCe,
                serie: config.Serie,
                numero: numero,
                tpEmis: TipoEmissao.Normal,
                codigoNumericoOitoDigitos: cNumerico);

            // 6. Cria NotaFiscal
            var valorTotal = Dinheiro.FromDecimal(pedido.Total.Valor);
            var nota = NotaFiscal.CriarParaEmissao(
                empresaId: cmd.EmpresaId,
                lojaId: cmd.LojaId,
                pedidoId: cmd.PedidoId,
                modelo: ModeloDocumentoFiscal.NFCe,
                serie: config.Serie,
                numero: numero,
                chaveAcesso: chave,
                tipoEmissao: TipoEmissao.Normal,
                ambiente: config.Ambiente,
                dataEmissao: dataEmissao,
                valorTotal: valorTotal,
                clienteCpfCnpj: cmd.ClienteCpfCnpj,
                idempotencyKey: idempotKey,
                origem: cmd.Origem ?? "api",
                criadoPorUsuarioId: cmd.UsuarioId);

            var ordem = 0;
            foreach (var pi in pedido.Itens.OrderBy(i => i.Nome))
            {
                ordem++;
                var item = NotaFiscalItem.Criar(
                    notaFiscalId: nota.Id,
                    empresaId: cmd.EmpresaId,
                    ordem: ordem,
                    produtoId: pi.ProdutoId,
                    descricaoSnapshot: pi.Nome ?? $"Item {ordem}",
                    codigoProduto: pi.ProdutoId?.ToString() ?? $"ITEM{ordem:D4}",
                    ean: null,
                    ncm: NCM.Parse("00000000"),
                    cfop: CFOP.VendaIntraEstado(),
                    cest: null,
                    unidadeComercial: pi.Unidade ?? "UN",
                    quantidade: pi.Quantidade,
                    precoUnitario: pi.PrecoUnitario,
                    desconto: 0m,
                    origem: OrigemMercadoria.Nacional,
                    cstCsosn: CSTouCSOSN.ParaSimples("102"),
                    cstPis: "07",
                    cstCofins: "07");
                nota.AdicionarItem(item);
            }

            var ordemPag = 0;
            foreach (var pp in cmd.Pagamentos)
            {
                ordemPag++;
                nota.AdicionarPagamento(NotaFiscalPagamento.Criar(
                    notaFiscalId: nota.Id,
                    empresaId: cmd.EmpresaId,
                    ordem: ordemPag,
                    formaPagamento: pp.FormaPagamento,
                    valor: Dinheiro.FromDecimal(pp.Valor),
                    bandeiraCartao: pp.BandeiraCartao,
                    cnpjCredenciadora: pp.CnpjCredenciadora,
                    nsu: pp.Nsu));
            }

            await notaRepo.AdicionarAsync(nota, token);
            await uow.CommitAsync();

            // 7-9. Gateway → state transition + outbox
            try
            {
                var resultado = await gateway.EmitirNFCeAsync(nota, config, token);
                await AplicarResultadoAsync(nota, resultado, config, token);
            }
            catch (FocusUnreachableException ex)
            {
                log.LogWarning(ex, "Focus indisponivel — entrando em contingencia para nota {Id}", nota.Id);
                var xmlLocal = gateway.GerarXmlAssinadoLocal(nota, config);
                nota.MarcarEmContingencia(xmlLocal, "Focus indisponivel: " + ex.Message);
                await notaRepo.AtualizarAsync(nota, token);
                await PublicarEventoAsync(eventos, nota, "nfce.contingencia.iniciada", token);
            }

            await uow.CommitAsync();
            return EmitirNotaFiscalConsumidorResult.From(nota);
        });
    }

    private async Task AplicarResultadoAsync(NotaFiscal nota, ResultadoEmissaoNFCe resultado, ConfigFiscalDto config, CancellationToken ct)
    {
        switch (resultado.Resultado)
        {
            case ResultadoEmissao.Autorizada:
                nota.MarcarAutorizada(
                    resultado.Protocolo ?? "0",
                    resultado.XmlAutorizado ?? "<auth/>",
                    resultado.DhAutorizacao ?? DateTime.UtcNow);
                await notaRepo.AtualizarAsync(nota, ct);
                await PublicarEventoAsync(eventos, nota, "nfce.autorizada", ct, urlQr: resultado.UrlConsultaQr);
                break;

            case ResultadoEmissao.Rejeitada:
                nota.MarcarRejeitada(resultado.Codigo ?? "REJ", resultado.Motivo ?? "Rejeitada pela SEFAZ");
                await notaRepo.AtualizarAsync(nota, ct);
                await PublicarEventoAsync(eventos, nota, "nfce.rejeitada", ct);
                break;

            case ResultadoEmissao.Denegada:
                nota.MarcarDenegada(resultado.Codigo ?? "DEN", resultado.Motivo ?? "Denegada pela SEFAZ");
                await notaRepo.AtualizarAsync(nota, ct);
                await PublicarEventoAsync(eventos, nota, "nfce.denegada", ct);
                break;
        }
    }

    private static async Task PublicarEventoAsync(
        IPublicadorEventoIntegracao eventos,
        NotaFiscal nota,
        string tipoEvento,
        CancellationToken ct,
        string? urlQr = null)
    {
        var payload = new
        {
            notaFiscalId = nota.Id,
            chaveAcesso = nota.ChaveAcesso.Valor,
            modelo = (short)nota.Modelo,
            serie = nota.Serie,
            nNF = nota.Numero,
            dhEmi = nota.DataEmissao,
            dhAutorizacao = nota.DataAutorizacao,
            protocolo = nota.ProtocoloAutorizacao,
            valorTotal = nota.ValorTotal.Valor,
            lojaId = nota.LojaId,
            pedidoId = nota.PedidoId,
            urlConsultaQr = urlQr,
        };
        await eventos.PublicarAsync(
            empresaId: nota.EmpresaId,
            tipoEvento: tipoEvento,
            aggregateType: nameof(NotaFiscal),
            aggregateId: nota.Id,
            payload: payload,
            payloadSchemaVersion: 1,
            ct: ct);
    }
}
