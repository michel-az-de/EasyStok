using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>listar_janelas</c> (S16, RN-21): janelas com vaga cujo início não é antes de agora + prazo mínimo
/// do pedido (<see cref="EasyStock.Application.Services.Pedidos.CalculadoraPrazoPedido"/>: maior preparo dos itens + respiro da configuração).
/// As primeiras vão ao cliente como botões <c>acao:escolher_janela:&lt;id&gt;:&lt;data&gt;</c> (até
/// <see cref="MaximoBotoes"/>, limite do WhatsApp); a lista inteira volta para o agente. #2515: o menu sai no fim do
/// turno, depois do texto do agente, e o histórico o guarda numerado.
///
/// <para>
/// Os itens chegam na entrada da ferramenta, como em <c>criar_pedido</c>: nada grava carrinho em
/// <c>Conversa.ContextoJson</c>. Sem itens vale o preparo padrão da empresa.
/// </para>
/// </summary>
public sealed class ListarJanelasFerramenta(
    ListarJanelasAtendimentoUseCase listarJanelas,
    IWhatsAppCloudClient cloudClient,
    IConversaRepository conversaRepository,
    ILogger<ListarJanelasFerramenta> logger) : IFerramentaAgente
{
    public const int MaximoBotoes = 3;
    public const int MaximoJanelas = ListarJanelasAtendimentoUseCase.MaximoPadrao;
    public const string CorpoBotoes = "Escolha a janela de entrega:";

    public string Nome => "listar_janelas";

    public string Descricao =>
        "Lista as janelas de entrega com vaga que respeitam o prazo mínimo do pedido (maior tempo de preparo dos itens " +
        "mais o respiro). Informe os itens (ids de consultar_cardapio) para o prazo sair certo e, se o cliente pediu, a data. " +
        "As primeiras vão ao cliente como opções numeradas logo depois da sua mensagem: não as repita, só convide a escolher. " +
        "Use o janelaId e a data escolhidos em criar_pedido.";

    public string SchemaJson => """
        {"type":"object","properties":{
          "data":{"type":"string","description":"AAAA-MM-DD; sem data, os próximos dias"},
          "itens":{"type":"array","items":{"type":"string"},"description":"cardapio_item_id dos itens do pedido"}},
         "additionalProperties":false}
        """;

    /// <summary>Id do botão que o <see cref="EscolherJanelaAcaoBotao"/> recebe.</summary>
    public static string IdBotao(Guid janelaId, DateOnly data) =>
        $"{RoteadorAcoesBotao.Prefixo}{EscolherJanelaAcaoBotao.NomeAcao}:{janelaId}:{data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        DateOnly? data = null;
        if (FerramentaJson.LerTexto(entrada, "data") is { } textoData)
        {
            if (!DateOnly.TryParseExact(textoData, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var informada))
                return FerramentaJson.Serializar(new { erro = "data_invalida" });
            data = informada;
        }

        var resultado = await listarJanelas.ExecuteAsync(
            new ListarJanelasAtendimentoInput(contexto.EmpresaId, data, data, LerItens(entrada)), ct);
        if (!resultado.LojaDisponivel)
            return FerramentaJson.Serializar(new { erro = "janelas_indisponiveis" });
        var prazo = resultado.PrazoMinimoMinutos;
        var janelas = resultado.Janelas;

        if (janelas.Count == 0)
        {
            return FerramentaJson.Serializar(new
            {
                prazoMinimoMinutos = prazo,
                janelas = Array.Empty<object>(),
                orientacao = "Nenhuma janela com vaga respeita o prazo nesse período. Ofereça outra data."
            });
        }

        var opcoes = janelas.Take(MaximoBotoes).Select(j => (Id: IdBotao(j.JanelaId, j.Data), Titulo: Titulo(j))).ToList();
        contexto.AdiarEnvio(token => EnviarMenuAsync(contexto, opcoes, token));

        return FerramentaJson.Serializar(new
        {
            prazoMinimoMinutos = prazo,
            opcoesNoMenu = opcoes.Count,
            janelas = janelas.Select(j => new
            {
                janelaId = j.JanelaId,
                data = j.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                janela = Titulo(j),
                vagas = j.VagasRestantes
            })
        });
    }

    private async Task<bool> EnviarMenuAsync(ContextoTurnoAgente contexto, IReadOnlyList<(string Id, string Titulo)> opcoes, CancellationToken ct)
    {
        var conversa = contexto.Conversa;
        var texto = MenuNumeradoWhatsApp.Texto(CorpoBotoes, opcoes.Select(o => o.Titulo));
        // O texto do agente sai no mesmo instante do turno; 1 ms a mais mantém o menu depois dele no histórico.
        var enviadaEm = contexto.Agora.AddMilliseconds(1);
        Mensagem saida;
        var enviou = false;
        try
        {
            var envio = await cloudClient.EnviarBotoesAsync(conversa.ContatoIdExterno, CorpoBotoes, opcoes, ct);
            saida = Mensagem.Saida(contexto.EmpresaId, conversa.Id, AutorMensagem.Agente, enviadaEm,
                TipoConteudoMensagem.Texto, texto, envio.Wamid);
            enviou = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // O texto do agente já convidou a escolher: a falha fica no histórico para a equipe ver e reenviar.
            logger.LogWarning(ex, "listar_janelas: falha ao enviar o menu de janelas da conversa {ConversaId}.", conversa.Id);
            saida = Mensagem.Saida(contexto.EmpresaId, conversa.Id, AutorMensagem.Agente, enviadaEm, TipoConteudoMensagem.Texto, texto);
            saida.RegistrarFalhaEnvio(ClassificadorFalhaEnvio.Motivo(ex), ClassificadorFalhaEnvio.Classificar(ex), enviadaEm);
        }

        if (conversa.EstaAberta) conversa.RegistrarSaida(enviadaEm);
        await conversaRepository.AddMensagemAsync(saida, ct);
        await contexto.GravarSaidaAsync(ct);
        return enviou;
    }

    /// <summary>"02/06 13:00-14:00": cabe nos 20 caracteres do título de botão.</summary>
    private static string Titulo(JanelaDisponivelDto j) =>
        string.Create(CultureInfo.InvariantCulture, $"{j.Data:dd/MM} {j.HoraInicio:HH:mm}-{j.HoraFim:HH:mm}");

    private static List<Guid> LerItens(JsonElement entrada)
    {
        var itens = new List<Guid>();
        if (entrada.ValueKind != JsonValueKind.Object
            || !entrada.TryGetProperty("itens", out var lista)
            || lista.ValueKind != JsonValueKind.Array)
            return itens;

        foreach (var item in lista.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id) && !itens.Contains(id))
                itens.Add(id);
        }
        return itens;
    }
}
