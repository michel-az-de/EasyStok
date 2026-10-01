using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Automacoes;

/// <summary>
/// Um disparo de automática. A conversa vem pelo id quando o evento a conhece; senão é a mais recente do
/// cliente. O pedido alimenta <c>{pedido}</c> e <c>{faixa}</c>.
/// </summary>
public sealed record DisparoAutomacao(Guid EmpresaId, GatilhoAutomacao Gatilho, Guid? ConversaId, Guid? ClienteId, Guid? PedidoId);

/// <summary>O que aconteceu com o disparo. Só <see cref="Enviada"/> sai para o cliente.</summary>
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
    ClienteBloqueado = 9
}

/// <summary>
/// S42: envia a mensagem automática de um gatilho. Chamado pelos handlers do outbox (ADR-0030), então
/// nunca lança por regra de negócio: devolve o <see cref="ResultadoAutomacao"/> e loga.
///
/// <para>
/// Regras: a regra do gatilho precisa existir e estar ligada; o texto é livre, então só sai dentro da
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
    ILogger<DispararAutomacaoUseCase> logger)
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
        var regra = await regras.ObterPorGatilhoAsync(disparo.EmpresaId, disparo.Gatilho, ct);
        if (regra is null) return ResultadoAutomacao.SemRegra;
        if (!regra.Ligada) return ResultadoAutomacao.Desligada;

        var conversa = await ConversaAsync(disparo, ct);
        if (conversa is null) return Registrar(disparo, ResultadoAutomacao.SemConversa);

        var agora = relogio.GetUtcNow().UtcDateTime;
        if (!conversa.DentroDaJanela(agora)) return Registrar(disparo, ResultadoAutomacao.ForaDaJanela);

        var clienteId = disparo.ClienteId ?? conversa.ClienteId;
        if (clienteId is { } bloqueavel && (await clientes.GetByIdAsync(disparo.EmpresaId, bloqueavel))?.Bloqueado == true)
            return Registrar(disparo, ResultadoAutomacao.ClienteBloqueado);

        if (clienteId is { } c
            && !await politica.PodeEnviarAsync(disparo.EmpresaId, c, conversa.Canal, FinalidadeContato.Transacional, ct))
            return Registrar(disparo, ResultadoAutomacao.SemConsentimento);

        string texto;
        try
        {
            var valores = await variaveis.ResolverAsync(disparo.EmpresaId, conversa, clienteId, disparo.PedidoId, ct);
            texto = regra.Renderizar(valores);
        }
        catch (VariavelSemValorException ex)
        {
            logger.LogWarning("Automática {Gatilho} não saiu: {Motivo}", disparo.Gatilho, ex.Message);
            return ResultadoAutomacao.VariavelSemValor;
        }

        string idExterno;
        try
        {
            idExterno = await canais.Obter(conversa.Canal).EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Automática {Gatilho} falhou no canal da conversa {ConversaId}.", disparo.Gatilho, conversa.Id);
            return ResultadoAutomacao.Falhou;
        }

        var saida = Mensagem.Saida(disparo.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora,
            TipoConteudoMensagem.Texto, texto, idExterno);
        // A de encerramento sai com a conversa já encerrada: entra no histórico sem reabrir nada.
        if (conversa.EstaAberta) conversa.RegistrarSaida(agora);
        await conversas.AddMensagemAsync(saida, ct);
        await unitOfWork.CommitAsync();
        return ResultadoAutomacao.Enviada;
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
        return recentes.FirstOrDefault();
    }

    private ResultadoAutomacao Registrar(DisparoAutomacao disparo, ResultadoAutomacao resultado)
    {
        logger.LogInformation("Automática {Gatilho} da empresa {EmpresaId} não saiu: {Resultado}.",
            disparo.Gatilho, disparo.EmpresaId, resultado);
        return resultado;
    }
}
