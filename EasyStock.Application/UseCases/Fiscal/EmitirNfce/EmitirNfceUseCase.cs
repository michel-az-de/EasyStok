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
/// <b>Idempotencia (defesa em duas camadas):</b>
/// (1) <c>IdempotencyMiddleware</c> HTTP-level cacheia a resposta por
/// <c>(EmpresaId, Idempotency-Key)</c> com TTL 24h — mas pode silenciar a falha
/// de <c>SaveAsync</c> do cache (catch WARN), abrindo brecha para duplicacao em
/// retry. (2) Esta camada consulta <see cref="INfeRepository.FindByIdempotencyKeyAsync"/>
/// ANTES de reservar numero fiscal e armazena <c>IdempotencyKey</c> no
/// <see cref="NfeDocumento"/>. Unique partial index
/// <c>ux_nfe_documentos_empresa_idempotency</c> serve como ultima linha de
/// defesa contra race entre 2 requests concorrentes — quem perde a corrida
/// pega <c>DbUpdateException</c> e o use case re-pesquisa para devolver o
/// vencedor. Ver issue #290.
/// </para>
/// </summary>
public class EmitirNfceUseCase(
    INfeRepository nfeRepo,
    INumeracaoNfeService numeracao,
    IGeradorChaveAcesso geradorChave,
    IGatewayFiscalFactory gatewayFactory,
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

        // === Defesa em DB (issue #290): consulta antes de reservar numero ===
        // Se o middleware HTTP-level falhou ao persistir o cache (catch WARN),
        // retry chega aqui sem hit no cache. Antes de queimar segundo numero
        // fiscal — ou de resolver config / acionar gateway — checa o repo.
        // Hit -> devolve a NFC-e ja emitida sem efeito colateral.
        var jaEmitida = await nfeRepo.FindByIdempotencyKeyAsync(cmd.EmpresaId, cmd.IdempotencyKey);
        if (jaEmitida is not null)
        {
            logger.LogInformation(
                "Nfe {Id} ja emitida com IdempotencyKey={Key} (retry idempotente). Chave={Chave}.",
                jaEmitida.Id, cmd.IdempotencyKey, jaEmitida.ChaveAcesso);
            return ToResult(jaEmitida);
        }

        var config = await configResolver.ResolveAsync(cmd.EmpresaId);
        var gateway = gatewayFactory.ObterPara(config.Provedor);

        // === Tx 1: reservar numero, criar NfeDocumento Rascunho->EnviadaAguardando ===
        Guid nfeId;
        try
        {
            nfeId = await uow.ExecuteInTransactionAsync(async _ =>
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
                    origem: cmd.Origem,
                    idempotencyKey: cmd.IdempotencyKey);

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
        }
        catch (Exception ex)
        {
            // Race-resolution: 2 calls com mesma IdempotencyKey chegaram quase juntos.
            // O perdedor da unique constraint ux_nfe_documentos_empresa_idempotency
            // recebe DbUpdateException; o vencedor ja esta gravado. Re-query confirma
            // race (vencedor existe). Se nao existe, e' outro erro real -> propaga.
            var raceWinner = await nfeRepo.FindByIdempotencyKeyAsync(cmd.EmpresaId, cmd.IdempotencyKey);
            if (raceWinner is null) throw;

            logger.LogInformation(ex,
                "Nfe {Id} race-resolved: outra TX venceu unique constraint IdempotencyKey={Key}.",
                raceWinner.Id, cmd.IdempotencyKey);
            return ToResult(raceWinner);
        }

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
            await MarcarFalhaTransienteAsync(cmd, nfeId, trans, "transiente");
        }
        catch (Exception ex) when (ex is not GatewayFiscalCredencialException
                                     and not GatewayFiscalDenegadaException)
        {
            // Captura defensiva: TaskCanceledException, HttpRequestException, falhas inesperadas
            // do adapter. Sem este catch, a Nfe ficaria presa em EnviadaAguardandoRetorno
            // (job de contingência só pega FalhaTransiente). Credencial/Denegada propagam
            // porque exigem ação humana (renovar token / regularizar tenant na SEFAZ).
            await MarcarFalhaTransienteAsync(cmd, nfeId, ex, "inesperada");
        }

        // Round-trip final: re-leitura do agregado atualizado para resposta consistente.
        // (Não usamos o tracker da Tx2 porque ele já foi descartado ao fechar o scope da transação.)
        var nfeFinal = await nfeRepo.GetByIdAsync(cmd.EmpresaId, nfeId)
            ?? throw new InvalidOperationException($"NfeDocumento {nfeId} sumiu após commit final.");
        return ToResult(nfeFinal);
    }

    private async Task MarcarFalhaTransienteAsync(EmitirNfceCommand cmd, Guid nfeId, Exception causa, string tipo)
    {
        // Mensagem amigável para o operador no PWA Caixa — guia decisão de
        // contingência em <2s. Categoria técnica fica no log estruturado.
        var (categoria, mensagemOperador) = ClassificarFalhaParaOperador(causa);

        await uow.ExecuteInTransactionAsync(async _ =>
        {
            var nfe = await nfeRepo.GetByIdAsync(cmd.EmpresaId, nfeId)
                ?? throw new InvalidOperationException($"NfeDocumento {nfeId} sumiu antes do rollback FalhaTransiente ({tipo}).");

            // detalhe gravado como motivoRejeicao é o que o caixa.js exibe ao operador.
            nfe.MarcarFalhaTransiente(mensagemOperador, cmd.UsuarioId, cmd.UsuarioNome, cmd.Origem);
            await nfeRepo.UpdateAsync(nfe);
        });

        logger.LogWarning(causa,
            "Nfe {Id} FalhaTransiente categoria={Categoria} tipo={Tipo} mensagemOperador={Msg}",
            nfeId, categoria, tipo, mensagemOperador);
    }

    /// <summary>
    /// Mapeia exception → (categoria, mensagem amigável). Categoria fica em logs/APM
    /// para agrupar incidentes; mensagem vai pro operador no PWA Caixa.
    /// </summary>
    private static (string categoria, string mensagem) ClassificarFalhaParaOperador(Exception ex) =>
        ex switch
        {
            TaskCanceledException => ("timeout",
                "Tempo esgotado conectando ao SEFAZ. NFC-e está em fila — vai voltar em alguns segundos."),
            OperationCanceledException => ("timeout",
                "Tempo esgotado conectando ao SEFAZ. NFC-e está em fila — vai voltar em alguns segundos."),
            TimeoutException => ("timeout",
                "Tempo limite excedido pela SEFAZ. NFC-e em fila de reprocessamento."),
            HttpRequestException => ("sem-conexao",
                "Sem conexão com o gateway fiscal. NFC-e será reenviada automaticamente quando voltar."),
            GatewayFiscalTransienteException => ("gateway-transiente",
                "Gateway fiscal instável no momento. NFC-e em fila — sem ação necessária."),
            _ => ("inesperada",
                "Falha temporária na emissão. NFC-e em fila — toque em Nova venda e tente novamente se preferir.")
        };

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
