using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Atendimento.Automacoes;

/// <summary>
/// Um disparo de automática. A conversa vem pelo id quando o evento a conhece; senão é a mais recente do
/// cliente. O pedido alimenta <c>{pedido}</c> e <c>{faixa}</c>.
/// </summary>
public sealed record DisparoAutomacao(Guid EmpresaId, GatilhoAutomacao Gatilho, Guid? ConversaId, Guid? ClienteId, Guid? PedidoId);

/// <summary>
/// O que aconteceu com o disparo. Só <see cref="Enviada"/> sai para o cliente; <see cref="EnvioIncerto"/> pode ter saído
/// (o canal não respondeu), então quem decide um aviso de reserva não deve tratá-lo como "não saiu".
/// </summary>
public enum ResultadoAutomacao
{
    Enviada = 1,
    SemRegra = 2,
    Desligada = 3,
    SemConversa = 4,
    ForaDaJanela = 5,
    SemConsentimento = 6,
    VariavelSemValor = 7,
    Falhou = 8,
    ClienteBloqueado = 9,
    ConversaReaberta = 10,
    PedidoCancelado = 11,
    EnvioIncerto = 12,
    PedidoJaAdiante = 13
}

/// <summary>
/// S42: envia a mensagem automática de um gatilho. Chamado pelos handlers do outbox (ADR-0030), então
/// nunca lança por regra de negócio: devolve o <see cref="ResultadoAutomacao"/> e loga.
///
/// <para>
/// Regras: a regra do gatilho precisa existir (ou ter padrão, <see cref="RegraAutomatica.Padrao"/>) e estar ligada; o texto é livre, então só sai dentro da
/// janela do canal (S09/S34), sem modelo aprovado; o cliente não pode ter revogado o contato
/// transacional no canal (S38); cliente bloqueado (S24) não recebe nenhuma; variável sem valor não sai.
/// A saída entra no histórico como <see cref="AutorMensagem.Sistema"/>.
/// </para>
/// </summary>
public sealed class DispararAutomacaoUseCase(
    IRegraAutomaticaRepository regras,
    IConversaRepository conversas,
    IClienteRepository clientes,
    IExpedienteLojaRepository expedientes,
    VariaveisAtendimento variaveis,
    PoliticaEnvioCliente politica,
    ResolvedorCanal canais,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<DispararAutomacaoUseCase> logger,
    IPedidoRepository? pedidos = null)
{
    /// <summary>
    /// Primeira mensagem de uma conversa: sai só uma automática. Loja fechada na mão vence fora do
    /// horário, que vence o primeiro contato (<see cref="RegraAutomatica.GatilhoDaPrimeiraEntrada"/>).
    /// A regra escolhida desligada não cai para outra.
    /// </summary>
    public async Task<ResultadoAutomacao> DispararPrimeiraEntradaAsync(Guid empresaId, Guid conversaId, CancellationToken ct = default)
    {
        var expediente = await expedientes.GetByEmpresaIdAsync(empresaId, ct) ?? ExpedienteLoja.CriarPadrao(empresaId);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var gatilho = RegraAutomatica.GatilhoDaPrimeiraEntrada(
            expediente.EstaAberta(agora), expediente.ControleManual == ControleManualLoja.ForcarFechada);
        return await ExecuteAsync(new DisparoAutomacao(empresaId, gatilho, conversaId, null, null), ct);
    }

    public async Task<ResultadoAutomacao> ExecuteAsync(DisparoAutomacao disparo, CancellationToken ct = default)
    {
        var preparo = await PrepararAsync(disparo, ct);
        if (preparo is not Pronta(var conversa, var texto)) return ((NaoSai)preparo).Resultado;
        var agora = relogio.GetUtcNow().UtcDateTime;

        string idExterno;
        try
        {
            idExterno = await canais.Obter(conversa.Canal).EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Automática {Gatilho} falhou no canal da conversa {ConversaId}.", disparo.Gatilho, conversa.Id);
            var tipo = ClassificadorFalhaEnvio.Classificar(ex);
            await RegistrarFalhaAsync(conversa, texto, ex, tipo, agora);
            return tipo == TipoFalhaEnvio.Incerta ? ResultadoAutomacao.EnvioIncerto : ResultadoAutomacao.Falhou;
        }

        var saida = Mensagem.Saida(disparo.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora,
            TipoConteudoMensagem.Texto, texto, idExterno);
        // A de encerramento sai com a conversa já encerrada: entra no histórico sem reabrir nada.
        if (conversa.EstaAberta) conversa.RegistrarSaida(agora);
        await conversas.AddMensagemAsync(saida, ct);
        await unitOfWork.CommitAsync();
        return ResultadoAutomacao.Enviada;
    }

    /// <summary>
    /// #2004: a automática que não saiu fica no histórico como mensagem com falha, como a saudação e o aviso de cobrança
    /// (#1724): a atendente vê no console que o cliente não recebeu. Sem reenvio automático (o texto pode estar vencido,
    /// #1933) e só com a conversa aberta; a despedida de uma conversa encerrada não suja o histórico. Best-effort: quem
    /// chama é um handler do outbox e este caminho nunca lança.
    /// </summary>
    private async Task RegistrarFalhaAsync(Conversa conversa, string texto, Exception ex, TipoFalhaEnvio tipo, DateTime agora)
    {
        if (!conversa.EstaAberta) return;
        try
        {
            var falhou = Mensagem.Saida(conversa.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora, TipoConteudoMensagem.Texto, texto);
            falhou.RegistrarFalhaEnvio(ClassificadorFalhaEnvio.Motivo(ex), tipo, agora, reenvioAutomatico: false);
            await conversas.AddMensagemAsync(falhou);
            await unitOfWork.CommitAsync();
        }
        catch (Exception erroAoGravar)
        {
            logger.LogWarning(erroAoGravar, "Automática falhou e o registro da falha também: conversa {ConversaId}.", conversa.Id);
            unitOfWork.DescartarAlteracoesPendentes();
        }
    }

    /// <summary>O que a automática vai mandar (<see cref="Pronta"/>; falta só o canal aceitar), ou por que não sai.</summary>
    private abstract record Preparo;
    private sealed record NaoSai(ResultadoAutomacao Resultado) : Preparo;
    private sealed record Pronta(Conversa Conversa, string Texto) : Preparo;

    private async Task<Preparo> PrepararAsync(DisparoAutomacao disparo, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        // #1991-2: sem regra gravada vale o padrão ("Pagamento confirmado" nasce ligada).
        var regra = await regras.ObterPorGatilhoAsync(disparo.EmpresaId, disparo.Gatilho, ct)
            ?? RegraAutomatica.Padrao(disparo.EmpresaId, disparo.Gatilho, agora);
        if (regra is null) return new NaoSai(ResultadoAutomacao.SemRegra);
        if (!regra.Ligada) return new NaoSai(ResultadoAutomacao.Desligada);

        if (disparo.Gatilho == GatilhoAutomacao.PagamentoConfirmado && disparo.PedidoId is { } pedidoId && pedidos is not null)
        {
            var status = (await pedidos.GetByIdAsync(disparo.EmpresaId, pedidoId))?.StatusEnum;
            // Pagamento que chegou depois do cancelamento é devolvido: não dizer que o pedido está garantido (como o S13).
            if (status == StatusPedido.Cancelado) return new NaoSai(Registrar(disparo, ResultadoAutomacao.PedidoCancelado));
            // #2004: dinheiro recebido na porta é quitado depois de o pedido sair ou ser entregue; "recebemos seu pagamento
            // e seu pedido está confirmado" não faz mais sentido (como o S13).
            if (status is StatusPedido.SaiuParaEntrega or StatusPedido.Entregue)
                return new NaoSai(Registrar(disparo, ResultadoAutomacao.PedidoJaAdiante));
        }

        var conversa = await ConversaAsync(disparo, ct);
        if (conversa is null) return new NaoSai(Registrar(disparo, ResultadoAutomacao.SemConversa));

        if (!conversa.DentroDaJanela(agora)) return new NaoSai(Registrar(disparo, ResultadoAutomacao.ForaDaJanela));

        var clienteId = disparo.ClienteId ?? conversa.ClienteId;
        if (clienteId is { } bloqueavel && (await clientes.GetByIdAsync(disparo.EmpresaId, bloqueavel))?.Bloqueado == true)
            return new NaoSai(Registrar(disparo, ResultadoAutomacao.ClienteBloqueado));

        // O encerramento pode estar no outbox quando o operador reabre. Não despedir de uma conversa retomada.
        if (disparo.Gatilho == GatilhoAutomacao.Encerramento && conversa.EstaAberta)
            return new NaoSai(Registrar(disparo, ResultadoAutomacao.ConversaReaberta));

        if (clienteId is { } c
            && !await politica.PodeEnviarAsync(disparo.EmpresaId, c, conversa.Canal, FinalidadeContato.Transacional, ct))
            return new NaoSai(Registrar(disparo, ResultadoAutomacao.SemConsentimento));

        try
        {
            var valores = await variaveis.ResolverAsync(disparo.EmpresaId, conversa, clienteId, disparo.PedidoId, ct);
            return new Pronta(conversa, regra.Renderizar(valores));
        }
        catch (VariavelSemValorException ex)
        {
            logger.LogWarning("Automática {Gatilho} não saiu: {Motivo}", disparo.Gatilho, ex.Message);
            return new NaoSai(ResultadoAutomacao.VariavelSemValor);
        }
    }

    private async Task<Conversa?> ConversaAsync(DisparoAutomacao disparo, CancellationToken ct)
    {
        if (disparo.ConversaId is { } id)
        {
            var conversa = await conversas.ObterPorIdAsync(disparo.EmpresaId, id, ct);
            if (conversa is not null) return conversa;
        }

        if (disparo.ClienteId is not { } clienteId) return null;
        var recentes = await conversas.ListarPorClienteAsync(disparo.EmpresaId, clienteId, 1, ct);
        // #1787: a lista vem sem rastreio; relida pelo id, a saída atualiza UltimaMensagemEm e a conversa sobe na inbox.
        return recentes.FirstOrDefault() is { } recente
            ? await conversas.ObterPorIdAsync(disparo.EmpresaId, recente.Id, ct)
            : null;
    }

    private ResultadoAutomacao Registrar(DisparoAutomacao disparo, ResultadoAutomacao resultado)
    {
        logger.LogInformation("Automática {Gatilho} da empresa {EmpresaId} não saiu: {Resultado}.",
            disparo.Gatilho, disparo.EmpresaId, resultado);
        return resultado;
    }
}
