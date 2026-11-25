using System.Diagnostics;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2885 (S75): saudação solta ("oi", "bom dia") em conversa parada recebe a regra <see cref="GatilhoAutomacao.SaudacaoSolta"/>
/// sem chamar o modelo. Só responde quando todas as condições valem; qualquer uma falsa devolve <c>false</c> sem efeito e o
/// turno segue para o modelo como hoje. No molde de <see cref="OptOutPorPalavra"/>.
/// </summary>
public sealed class RespostaSemIa(
    IRegraAutomaticaRepository regras,
    IConfiguracaoAtendimentoRepository configuracoes,
    IConversaRepository conversas,
    IClienteRepository clientes,
    ITurnoAgenteRepository turnos,
    IAgenteLlmClient llm,
    ResolvedorCanal canais,
    IUnitOfWork unitOfWork,
    ILogger<RespostaSemIa> logger,
    IOptions<LimiteRespostasAgenteOptions> limiteRespostas,
    IContatoDaEquipeAvisada? equipe = null)
{
    /// <summary>Modelo gravado no turno registrado sem chamar o modelo.</summary>
    public const string Modelo = "sem-ia";

    /// <summary>D4: "conversa parada" é nenhuma saída nesta janela.</summary>
    public static readonly TimeSpan JanelaConversaParada = TimeSpan.FromMinutes(30);

    /// <summary>Só texto puro: "oi" na legenda de uma foto vai para o modelo, que precisa ver a imagem.</summary>
    private static bool EhSaudacaoSolta(Mensagem entrada) =>
        entrada.TipoConteudo == TipoConteudoMensagem.Texto
        && ClassificadorDeMensagemCurta.Classificar(entrada.Texto) == TipoMensagemCurta.Saudacao;

    /// <summary>
    /// Conversa nova: a saudação já sai pelo webhook (RN-01 ou a automática de entrada), então só checa a classificação e os
    /// portões. Não depende da regra estar ligada nem de conversa parada.
    /// </summary>
    public async Task<bool> PodeDispensarOModeloAsync(Guid empresaId, Conversa conversa, Mensagem entrada, DateTime agoraUtc, CancellationToken ct = default)
    {
        if (!EhSaudacaoSolta(entrada)) return false;
        var (passa, _) = await PortoesPassamAsync(empresaId, conversa, agoraUtc, ct);
        return passa;
    }

    /// <summary>Conversa nova: a entrada já foi respondida sem modelo. Marca processada e registra o turno.</summary>
    public async Task RegistrarSemRespostaPropriaAsync(Guid empresaId, Conversa conversa, Mensagem entrada, DateTime agoraUtc, CancellationToken ct = default)
    {
        entrada.MarcarProcessada(agoraUtc);
        await turnos.AddAsync(TurnoAgente.Registrar(empresaId, conversa.Id, entrada.Id, null, Modelo, 0, 0, 0, null, [], 0,
            DesfechoTurnoAgente.SemIa, 0, agoraUtc), ct);
        await unitOfWork.CommitAsync();
    }

    /// <returns><c>true</c> quando respondeu sem o modelo (o chamador não enfileira o turno).</returns>
    public async Task<bool> TentarAsync(Guid empresaId, Conversa conversa, Mensagem entrada, DateTime agoraUtc, CancellationToken ct = default)
    {
        var cronometro = Stopwatch.StartNew();
        if (!EhSaudacaoSolta(entrada)) return false;

        var regra = await regras.ObterPorGatilhoAsync(empresaId, GatilhoAutomacao.SaudacaoSolta, ct)
            ?? RegraAutomatica.Padrao(empresaId, GatilhoAutomacao.SaudacaoSolta, agoraUtc);
        if (regra is not { Ligada: true } || string.IsNullOrWhiteSpace(regra.Texto)) return false;

        var dados = await conversas.ObterComMensagensAsync(empresaId, conversa.Id, AgenteAtendimentoService.UltimasMensagens, ct);
        if (dados is null) return false;

        var pendentes = AgenteAtendimentoService.EntradasPendentes(dados.Mensagens);
        if (pendentes.Count != 1 || pendentes[0].Id != entrada.Id) return false;

        if (dados.Mensagens.Any(m => m.Direcao == DirecaoMensagem.Saida && m.EnviadaEm > agoraUtc - JanelaConversaParada))
            return false;

        var (passa, cliente) = await PortoesPassamAsync(empresaId, dados.Conversa, agoraUtc, ct);
        if (!passa) return false;

        var nome = NomesDeContato.PrimeiroNome(cliente?.Nome) ?? NomesDeContato.PrimeiroNome(dados.Conversa.ContatoNome);
        string texto;
        try
        {
            texto = regra.Renderizar(new Dictionary<string, string?>(StringComparer.Ordinal) { ["nome"] = nome });
        }
        catch (VariavelSemValorException)
        {
            // {nome} sem valor: a regra não sai com a variável crua. O modelo responde como hoje.
            return false;
        }

        Mensagem saida;
        var enviada = false;
        try
        {
            var id = await canais.Obter(dados.Conversa.Canal).EnviarTextoAsync(dados.Conversa.ContatoIdExterno, texto, ct);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Agente, agoraUtc, TipoConteudoMensagem.Texto, texto, id);
            enviada = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Assistente: a resposta sem IA não saiu na conversa {ConversaId}; o modelo assume.", conversa.Id);
            saida = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Agente, agoraUtc, TipoConteudoMensagem.Texto, texto);
            saida.RegistrarFalhaEnvio(ClassificadorFalhaEnvio.Motivo(ex), ClassificadorFalhaEnvio.Classificar(ex), saida.EnviadaEm);
        }

        dados.Conversa.RegistrarSaida(saida.EnviadaEm);
        await conversas.AddMensagemAsync(saida, ct);
        if (!enviada)
        {
            await unitOfWork.CommitAsync();
            return false;
        }

        entrada.MarcarProcessada(agoraUtc);
        await turnos.AddAsync(TurnoAgente.Registrar(empresaId, conversa.Id, entrada.Id, saida.Id, Modelo, 0, 0, 0, null, [], 0,
            DesfechoTurnoAgente.SemIa, (int)Math.Min(cronometro.ElapsedMilliseconds, int.MaxValue), agoraUtc), ct);
        await unitOfWork.CommitAsync();
        return true;
    }

    /// <summary>
    /// Os mesmos portões do turno do modelo, na mesma ordem (<see cref="AgenteAtendimentoService.ProcessarTurnoAsync"/>), só
    /// leitura. Barrando, nada é escalado aqui: o turno do modelo trata como hoje.
    /// </summary>
    private async Task<(bool Passa, Cliente? Cliente)> PortoesPassamAsync(Guid empresaId, Conversa conversa, DateTime agoraUtc, CancellationToken ct)
    {
        if (PortoesDoAssistente.DaConversa(conversa, agoraUtc).Any(e => e.Barra)) return (false, null);

        var configuracao = await configuracoes.GetByEmpresaIdAsync(empresaId) ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);
        var portoes = new PortoesDoAssistente(llm, conversas, turnos, limiteRespostas, equipe);
        if ((await portoes.DaLojaAsync(empresaId, configuracao, conversa, ct)).Any(e => e.Barra)) return (false, null);

        var cliente = conversa.ClienteId is { } clienteId ? await clientes.GetByIdAsync(empresaId, clienteId) : null;
        var barrou = await portoes.PrimeiraQueBarraDoClienteEDoCustoAsync(empresaId, conversa.Id, configuracao, cliente, agoraUtc, ct);
        return (barrou is null, cliente);
    }
}
