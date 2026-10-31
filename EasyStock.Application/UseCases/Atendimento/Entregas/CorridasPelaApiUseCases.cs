using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Integration.Logistica;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

// #2160 (ADR-0064): corrida chamada pela API do provedor. O operador cota e chama; depois disso o status vem
// do provedor (webhook, consulta de contingência ou o "atualizar" da tela), sempre por reconsulta.

public sealed record CorridaApiResult(
    Guid Id, Guid ViagemId, EmpresaEntregador Provedor, StatusCorrida Status, VeiculoCorrida? Veiculo, int CustoCentavos,
    int? CustoCotadoCentavos, string? IdExterno, string? LinkRastreio, string? Motorista, string? MotoristaTelefone,
    string? MotoristaPlaca, DateTime RegistradoEm, DateTime? EncerradaEm, bool PodeCancelar)
{
    internal static CorridaApiResult De(CorridaExterna c, DateTime agora) => new(
        c.Id, c.ViagemId, c.Provedor, c.Status ?? StatusCorrida.Falha, c.Veiculo, c.CustoCentavos, c.CustoCotadoCentavos,
        c.IdExterno, c.LinkRastreio, c.Motorista, c.MotoristaTelefone, c.MotoristaPlaca, c.RegistradoEm, c.EncerradaEm,
        c.PodeCancelar(agora));
}

public sealed record CotacaoCorridaResult(
    string CotacaoId, EmpresaEntregador Provedor, VeiculoCorrida Veiculo, int ValorCentavos, DateTimeOffset ExpiraEm, int? DistanciaMetros);

public sealed record CotarCorridaInput(Guid EmpresaId, Guid ViagemId, VeiculoCorrida Veiculo, DateTimeOffset? AgendarPara);

public sealed record ChamarCorridaInput(
    Guid EmpresaId, Guid UsuarioId, Guid ViagemId, string CotacaoId, VeiculoCorrida Veiculo, string? Observacao);

/// <summary>Monta coleta, entrega e contatos da viagem de uma parada só (R7) para o provedor.</summary>
internal sealed class MontagemCorrida(IViagemRepository viagens, IPedidoRepository pedidos, IStorefrontRepository storefronts)
{
    public sealed record Dados(Viagem Viagem, Domain.Entities.Pedido Pedido, PontoCorrida Coleta, PontoCorrida Entrega,
        ContatoCorrida Remetente, ContatoCorrida Destinatario);

    public async Task<Dados> MontarAsync(Guid empresaId, Guid viagemId, CancellationToken ct)
    {
        var viagem = await viagens.ObterAsync(empresaId, viagemId, ct) ?? throw new ViagemNaoEncontradaException(viagemId);
        if (viagem.Situacao != SituacaoViagem.Montando)
            throw new UseCaseValidationException("Só viagem montando chama corrida.");
        if (viagem.Paradas.Count != 1)
            throw new UseCaseValidationException("A corrida pela API leva um pedido por vez: deixe uma parada só nesta viagem.");

        var pedidoId = viagem.Paradas.Single().PedidoId;
        var pedido = await pedidos.GetByIdAsync(empresaId, pedidoId) ?? throw new PedidoNaoEncontradoParaViagemException(pedidoId);
        if (pedido.EntregaLat is not { } lat || pedido.EntregaLng is not { } lng || string.IsNullOrWhiteSpace(pedido.EnderecoEntrega))
            throw new UseCaseValidationException($"Pedido {CodigoCurto.De(pedidoId)} sem endereço localizado no mapa. Confirme o endereço antes de chamar a corrida.");
        if (string.IsNullOrWhiteSpace(pedido.ClienteNome) || string.IsNullOrWhiteSpace(pedido.ClienteTelefone))
            throw new UseCaseValidationException($"Pedido {CodigoCurto.De(pedidoId)} sem nome ou telefone de quem recebe.");

        var loja = await storefronts.GetByEmpresaAsync(empresaId, ct);
        if (loja?.CozinhaLat is not { } origemLat || loja.CozinhaLng is not { } origemLng)
            throw new UseCaseValidationException("A localização da cozinha não está configurada. Configure em Gestão > Entregas.");
        if (string.IsNullOrWhiteSpace(loja.WhatsappPedidos))
            throw new UseCaseValidationException("O telefone de pedidos da loja não está configurado: o motorista liga para ele na coleta.");

        var endereco = string.Join(", ", new[] { pedido.EnderecoEntrega, pedido.ClienteApt, pedido.BairroEntrega, pedido.CepEntrega }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
        return new Dados(viagem, pedido,
            new PontoCorrida(origemLat, origemLng, loja.TituloPublico),
            new PontoCorrida(lat, lng, endereco),
            new ContatoCorrida(loja.TituloPublico, loja.WhatsappPedidos),
            new ContatoCorrida(pedido.ClienteNome, pedido.ClienteTelefone));
    }
}

public sealed class CotarCorridaUseCase(
    IViagemRepository viagens, IPedidoRepository pedidos, IStorefrontRepository storefronts, ILogisticaGateway gateway)
{
    public async Task<CotacaoCorridaResult> ExecuteAsync(CotarCorridaInput input, CancellationToken ct = default)
    {
        var dados = await new MontagemCorrida(viagens, pedidos, storefronts).MontarAsync(input.EmpresaId, input.ViagemId, ct);
        if (!await gateway.ConectadoAsync(input.EmpresaId, ct)) throw new LogisticaNaoConectadaException(gateway.Provedor.ToString());
        var cotacao = await Provedor.ChamarAsync(() => gateway.CotarAsync(input.EmpresaId,
            new PedidoCotacaoCorrida(dados.Coleta, dados.Entrega, input.Veiculo, input.AgendarPara), ct));
        return new CotacaoCorridaResult(cotacao.CotacaoId, gateway.Provedor, input.Veiculo, cotacao.ValorCentavos, cotacao.ExpiraEm, cotacao.DistanciaMetros);
    }
}

/// <summary>
/// Chama a corrida de uma cotação. A corrida é gravada <c>Solicitando</c> antes do POST (R4) e o índice de uma em
/// curso por viagem barra o segundo clique. Recusa definitiva encerra a corrida; timeout deixa para a reconsulta.
/// </summary>
public sealed class ChamarCorridaUseCase(
    IViagemRepository viagens, IPedidoRepository pedidos, IStorefrontRepository storefronts, ICorridaExternaRepository corridas,
    ILogisticaGateway gateway, IUnitOfWork unitOfWork, TimeProvider relogio, ILogger<ChamarCorridaUseCase> logger)
{
    public async Task<CorridaApiResult> ExecuteAsync(ChamarCorridaInput input, CancellationToken ct = default)
    {
        var dados = await new MontagemCorrida(viagens, pedidos, storefronts).MontarAsync(input.EmpresaId, input.ViagemId, ct);
        // R8: a coleta põe o pedido em "saiu para entrega"; de "preparando" a transição derrubaria a viagem.
        if (dados.Pedido.StatusEnum != StatusPedido.Pronto)
            throw new UseCaseValidationException($"Pedido {CodigoCurto.De(dados.Pedido.Id)} ainda não está pronto: a corrida só sai com o pedido pronto.");
        if (!await gateway.ConectadoAsync(input.EmpresaId, ct)) throw new LogisticaNaoConectadaException(gateway.Provedor.ToString());

        var cotacao = await Provedor.ChamarAsync(() => gateway.ObterCotacaoAsync(input.EmpresaId, input.CotacaoId, ct));
        if (cotacao.ExpiraEm <= relogio.GetUtcNow())
            throw new UseCaseValidationException("A cotação venceu. Cote de novo para ver o preço atual.");

        var corrida = Regra.Validar(() => CorridaExterna.SolicitarPorApi(
            dados.Viagem, gateway.Provedor, input.Veiculo, cotacao.CotacaoId, cotacao.ValorCentavos, input.UsuarioId, Agora));
        await corridas.AddAsync(corrida, ct);
        try
        {
            await unitOfWork.CommitAsync();
        }
        catch (Exception ex) when (unitOfWork.EhViolacaoDeUnicidade(ex))
        {
            unitOfWork.DescartarAlteracoesPendentes();
            throw new UseCaseValidationException("Esta viagem já tem uma corrida em curso. Acompanhe ou cancele a que está aberta.");
        }

        try
        {
            var observacao = string.IsNullOrWhiteSpace(input.Observacao) ? null : input.Observacao.Trim();
            var situacao = await gateway.ChamarAsync(input.EmpresaId,
                new PedidoChamadaCorrida(cotacao, dados.Remetente, dados.Destinatario, observacao, corrida.Id), ct);
            corrida.ConfirmarNoProvedor(situacao.IdExterno, situacao.LinkRastreio);
        }
        catch (LogisticaRecusadaException ex)
        {
            corrida.FalharSolicitacao(Agora);
            await unitOfWork.CommitAsync();
            throw new UseCaseValidationException(ex.Message);
        }
        catch (LogisticaIndisponivelException ex)
        {
            // R4: o POST não é repetido. A corrida fica "solicitando" e a reconsulta decide (ADR-0064 D4).
            logger.LogWarning(ex, "Lalamove não respondeu ao chamar a corrida {CorridaId}; fica para a reconsulta.", corrida.Id);
            throw new UseCaseValidationException(
                "A Lalamove não respondeu. Não chame de novo: o EasyStok confere em até 2 minutos se a corrida foi criada.");
        }
        await unitOfWork.CommitAsync();
        return CorridaApiResult.De(corrida, Agora);
    }

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;
}

/// <summary>Cancela sem custo: procurando motorista, ou até 5 minutos depois do match. Depois reconsulta.</summary>
public sealed class CancelarCorridaUseCase(
    ICorridaExternaRepository corridas, ILogisticaGateway gateway, SincronizarCorridaUseCase sincronizar, TimeProvider relogio)
{
    public async Task<CorridaApiResult> ExecuteAsync(Guid empresaId, Guid viagemId, Guid corridaId, CancellationToken ct = default)
    {
        var corrida = await CorridaDaApi.ObterAsync(corridas, empresaId, viagemId, corridaId, ct);
        if (!corrida.PodeCancelar(relogio.GetUtcNow().UtcDateTime))
            throw new UseCaseValidationException(corrida.Status == StatusCorrida.Solicitando
                ? "A corrida ainda está sendo confirmada pela Lalamove. Espere um instante."
                : "A Lalamove só cancela sem custo até 5 minutos depois de achar o motorista. Fale com o motorista ou com o suporte da Lalamove.");
        await Provedor.ChamarAsync(() => gateway.CancelarAsync(empresaId, corrida.IdExterno!, ct));
        return await sincronizar.ExecuteAsync(empresaId, corrida.Id, ct);
    }
}

/// <summary>Gorjeta (priority fee) para achar motorista mais rápido: só enquanto procura motorista.</summary>
public sealed class AdicionarGorjetaCorridaUseCase(
    ICorridaExternaRepository corridas, ILogisticaGateway gateway, SincronizarCorridaUseCase sincronizar)
{
    public const int GorjetaMaximaCentavos = 10_000;

    public async Task<CorridaApiResult> ExecuteAsync(Guid empresaId, Guid viagemId, Guid corridaId, int centavos, CancellationToken ct = default)
    {
        if (centavos is <= 0 or > GorjetaMaximaCentavos)
            throw new UseCaseValidationException("Gorjeta de R$ 0,01 a R$ 100,00.");
        var corrida = await CorridaDaApi.ObterAsync(corridas, empresaId, viagemId, corridaId, ct);
        if (corrida.Status != StatusCorrida.ProcurandoMotorista)
            throw new UseCaseValidationException("Gorjeta só enquanto a Lalamove procura motorista.");
        await Provedor.ChamarAsync(() => gateway.AdicionarGorjetaAsync(empresaId, corrida.IdExterno!, centavos, ct));
        return await sincronizar.ExecuteAsync(empresaId, corrida.Id, ct);
    }
}

/// <summary>
/// Reconsulta o provedor e aplica o que ele diz na corrida, na viagem e nos pedidos (ADR-0064 D1 a D3). É o único
/// caminho que muda o estado de uma corrida da API: o webhook, a consulta de contingência e a tela chamam aqui.
/// </summary>
public sealed class SincronizarCorridaUseCase(
    ICorridaExternaRepository corridas, IViagemRepository viagens, IPedidoRepository pedidos, IPublicadorEventoIntegracao publicador,
    ILogisticaGateway gateway, IUnitOfWork unitOfWork, TimeProvider relogio, ILogger<SincronizarCorridaUseCase> logger)
{
    /// <summary>Corrida "solicitando" sem resposta por mais que isto é dada como não criada (o POST não volta).</summary>
    public static readonly TimeSpan PrazoSolicitacao = TimeSpan.FromMinutes(10);

    public async Task<CorridaApiResult> ExecuteAsync(Guid empresaId, Guid corridaId, CancellationToken ct = default)
    {
        var corrida = await corridas.ObterAsync(empresaId, corridaId, ct) ?? throw new CorridaExternaNaoEncontradaException(corridaId);
        if (corrida.Origem != OrigemCorridaExterna.Api) throw new CorridaExternaNaoEncontradaException(corridaId);

        if (corrida.Ativa && corrida.IdExterno is { } idExterno)
        {
            var situacao = await Provedor.ChamarAsync(() => gateway.ConsultarAsync(empresaId, idExterno, ct));
            if (situacao is not null) await AplicarAsync(corrida, situacao, ct);
        }
        else if (corrida.Status == StatusCorrida.Solicitando && Agora - corrida.RegistradoEm > PrazoSolicitacao)
        {
            logger.LogWarning("Corrida {CorridaId} sem confirmação da Lalamove em {Prazo}; dada como não criada. Confira o painel da Lalamove.",
                corrida.Id, PrazoSolicitacao);
            corrida.FalharSolicitacao(Agora);
        }
        await unitOfWork.CommitAsync();
        return CorridaApiResult.De(corrida, Agora);
    }

    /// <summary>ORDER_REPLACED: o suporte da Lalamove trocou o pedido. A corrida segue com o id novo.</summary>
    public async Task<CorridaApiResult> SubstituirPedidoAsync(Guid empresaId, Guid corridaId, string idAnterior, string idNovo, CancellationToken ct = default)
    {
        var corrida = await corridas.ObterAsync(empresaId, corridaId, ct) ?? throw new CorridaExternaNaoEncontradaException(corridaId);
        if (corrida.IdExterno == idAnterior) corrida.SubstituirPedidoNoProvedor(idAnterior, idNovo, Agora);
        await unitOfWork.CommitAsync();
        return await ExecuteAsync(empresaId, corridaId, ct);
    }

    private async Task AplicarAsync(CorridaExterna corrida, SituacaoCorridaProvedor situacao, CancellationToken ct)
    {
        var viagem = await viagens.ObterAsync(corrida.EmpresaId, corrida.ViagemId, ct) ?? throw new ViagemNaoEncontradaException(corrida.ViagemId);
        corrida.AplicarStatus(situacao.Status, Agora);

        if (situacao.MotoristaIdExterno is { } motoristaId && corrida.MotoristaIdExterno != motoristaId && corrida.IdExterno is { } idExterno)
        {
            var motorista = await Provedor.ChamarAsync(() => gateway.ObterMotoristaAsync(corrida.EmpresaId, idExterno, motoristaId, ct));
            corrida.AtribuirMotorista(motoristaId, motorista?.Nome, motorista?.Telefone, motorista?.Placa);
        }

        switch (corrida.Status)
        {
            case StatusCorrida.Coletada or StatusCorrida.Entregue:
                if (viagem.Situacao == SituacaoViagem.Montando)
                {
                    viagem.SairComCorrida(corrida, Agora);
                    foreach (var parada in viagem.Paradas)
                        await TransicaoSeguraAsync(corrida.EmpresaId, parada.PedidoId, StatusPedido.SaiuParaEntrega, ct);
                }
                if (corrida.Status == StatusCorrida.Entregue && viagem.Situacao == SituacaoViagem.EmRota)
                {
                    foreach (var parada in viagem.Paradas.Where(p => !p.Entregue).ToList())
                    {
                        viagem.MarcarParadaEntregue(parada.PedidoId, Agora);
                        await TransicaoSeguraAsync(corrida.EmpresaId, parada.PedidoId, StatusPedido.Entregue, ct);
                    }
                    if (situacao.ValorCentavos is { } final) corrida.AtualizarCustoDoProvedor(final);
                }
                break;
            case StatusCorrida.Cancelada or StatusCorrida.Rejeitada or StatusCorrida.Expirada:
                // Antes da coleta a viagem fica montando, para chamar de novo ou passar ao entregador próprio, e a
                // corrida não custa nada. Depois da coleta, o retorno que já existe devolve os pedidos (ADR-0064 D3).
                if (viagem.Situacao == SituacaoViagem.EmRota)
                    await RetornoViagem.AplicarAsync(pedidos, publicador, viagem, corrida.RegistradoPorUsuarioId, "corrida", Agora, ct);
                else
                    corrida.AtualizarCustoDoProvedor(0);
                break;
        }
    }

    /// <summary>R8: um pedido que não pode mudar não derruba a viagem inteira; fica registrado para a equipe.</summary>
    private async Task TransicaoSeguraAsync(Guid empresaId, Guid pedidoId, StatusPedido novo, CancellationToken ct)
    {
        try
        {
            await TransicaoPedidoViagem.AplicarAsync(pedidos, publicador, empresaId, pedidoId, novo, Agora, ct);
        }
        catch (UseCaseValidationException ex)
        {
            logger.LogWarning("Corrida mudou o status, mas o pedido {Pedido} ficou para trás: {Motivo}", CodigoCurto.De(pedidoId), ex.Message);
        }
    }

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;
}

internal static class CorridaDaApi
{
    public static async Task<CorridaExterna> ObterAsync(
        ICorridaExternaRepository corridas, Guid empresaId, Guid viagemId, Guid corridaId, CancellationToken ct)
    {
        var corrida = await corridas.ObterAsync(empresaId, corridaId, ct);
        if (corrida is null || corrida.ViagemId != viagemId || corrida.Origem != OrigemCorridaExterna.Api)
            throw new CorridaExternaNaoEncontradaException(corridaId);
        return corrida;
    }
}

/// <summary>Traduz a recusa e a indisponibilidade do provedor em mensagem para o operador.</summary>
internal static class Provedor
{
    public static async Task<T> ChamarAsync<T>(Func<Task<T>> chamada)
    {
        try { return await chamada(); }
        catch (LogisticaRecusadaException ex) { throw new UseCaseValidationException(ex.Message); }
        catch (LogisticaIndisponivelException ex) { throw new UseCaseValidationException(ex.Message); }
    }

    public static async Task ChamarAsync(Func<Task> chamada) =>
        await ChamarAsync(async () => { await chamada(); return true; });
}

/// <summary>
/// Retorno da viagem em rota à loja: os pedidos não entregues voltam a <c>Pronto</c> e a viagem se desfaz. É o
/// mesmo caminho da logística de demonstração e da corrida da API que terminou sem entrega depois da coleta.
/// </summary>
internal static class RetornoViagem
{
    public static async Task AplicarAsync(IPedidoRepository pedidos, IPublicadorEventoIntegracao publicador, Viagem viagem,
        Guid usuarioId, string origem, DateTime agora, CancellationToken ct)
    {
        foreach (var parada in viagem.Paradas.Where(p => !p.Entregue).ToList())
        {
            var pedido = await pedidos.GetByIdAsync(viagem.EmpresaId, parada.PedidoId)
                ?? throw new PedidoNaoEncontradoParaViagemException(parada.PedidoId);
            if (pedido.StatusEnum != StatusPedido.SaiuParaEntrega) continue;
            var antigo = pedido.Status;
            pedido.RegistrarRetornoEntrega();
            await pedidos.AddEventoAsync(new PedidoEvento { Id = Guid.NewGuid(), PedidoId = pedido.Id,
                Tipo = "status_changed", StatusAntigo = antigo, StatusNovo = pedido.Status,
                UsuarioId = usuarioId, Origem = origem, OcorridoEm = agora });
            await publicador.PublicarAsync(pedido.EmpresaId, "pedido.mudou_status", "pedido", pedido.Id,
                new PedidoMudouStatusEvent(pedido.Id, pedido.EmpresaId, pedido.LojaId, antigo, pedido.Status, origem, usuarioId, null, agora),
                correlationId: pedido.Id.ToString(), ct: ct);
            await pedidos.UpdateAsync(pedido);
        }
        viagem.RegistrarRetorno(agora);
    }
}
