using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>listar_janelas</c> (S16, RN-21): janelas com vaga cujo início não é antes de agora + prazo mínimo
/// do pedido (<see cref="CalculadoraPrazoPedido"/>: maior preparo dos itens + respiro da configuração).
/// As primeiras vão ao cliente como botões <c>acao:escolher_janela:&lt;id&gt;:&lt;data&gt;</c> (até
/// <see cref="MaximoBotoes"/>, limite do WhatsApp); a lista inteira volta para o agente.
///
/// <para>
/// Os itens chegam na entrada da ferramenta, como em <c>criar_pedido</c>: nada grava carrinho em
/// <c>Conversa.ContextoJson</c>. Sem itens vale o preparo padrão da empresa.
/// </para>
/// </summary>
public sealed class ListarJanelasFerramenta(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioItemRepository,
    IConfiguracaoAtendimentoRepository configuracaoRepository,
    ListarJanelasDisponiveisUseCase listarJanelas,
    IWhatsAppCloudClient cloudClient,
    IConversaRepository conversaRepository,
    ILogger<ListarJanelasFerramenta> logger) : IFerramentaAgente
{
    public const int MaximoBotoes = 3;
    public const int MaximoJanelas = 10;
    public const string CorpoBotoes = "Escolha a janela de entrega:";

    public string Nome => "listar_janelas";

    public string Descricao =>
        "Lista as janelas de entrega com vaga que respeitam o prazo mínimo do pedido (maior tempo de preparo dos itens " +
        "mais o respiro). Informe os itens (ids de consultar_cardapio) para o prazo sair certo e, se o cliente pediu, a data. " +
        "As primeiras vão ao cliente como botões: não as repita, só convide a escolher. Use o janelaId e a data escolhidos em criar_pedido.";

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
        var storefront = await storefrontRepository.GetByEmpresaAsync(contexto.EmpresaId, ct);
        if (storefront is null || !storefront.Ativo)
            return FerramentaJson.Serializar(new { erro = "janelas_indisponiveis" });

        DateOnly? data = null;
        if (FerramentaJson.LerTexto(entrada, "data") is { } textoData)
        {
            if (!DateOnly.TryParseExact(textoData, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var informada))
                return FerramentaJson.Serializar(new { erro = "data_invalida" });
            data = informada;
        }

        var prazo = await PrazoMinimoAsync(contexto.EmpresaId, storefront.Id, LerItens(entrada), ct);
        var janelas = (await listarJanelas.ExecuteAsync(
                new ListarJanelasDisponiveisInput(storefront.Slug, data, data, null, prazo), ct))
            .Where(j => !j.Esgotado)
            .Take(MaximoJanelas)
            .ToList();

        if (janelas.Count == 0)
        {
            return FerramentaJson.Serializar(new
            {
                prazoMinimoMinutos = prazo,
                janelas = Array.Empty<object>(),
                orientacao = "Nenhuma janela com vaga respeita o prazo nesse período. Ofereça outra data."
            });
        }

        var botoesEnviados = await EnviarBotoesAsync(contexto, janelas.Take(MaximoBotoes).ToList(), ct);

        return FerramentaJson.Serializar(new
        {
            prazoMinimoMinutos = prazo,
            botoesEnviados,
            janelas = janelas.Select(j => new
            {
                janelaId = j.JanelaId,
                data = j.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                janela = Titulo(j),
                vagas = j.VagasRestantes
            })
        });
    }

    private async Task<int> PrazoMinimoAsync(Guid empresaId, Guid storefrontId, IReadOnlyCollection<Guid> itens, CancellationToken ct)
    {
        var configuracao = await configuracaoRepository.GetOrDefaultAsync(empresaId);

        var tempos = new List<int?>();
        foreach (var id in itens)
        {
            var item = await cardapioItemRepository.GetByIdAsync(storefrontId, id, ct);
            if (item is not null) tempos.Add(item.TempoPreparoMinutos);
        }
        if (tempos.Count == 0) tempos.Add(null); // sem itens: vale o preparo padrão

        return CalculadoraPrazoPedido.PrazoMinimo(tempos, configuracao.TempoPreparoPadraoMinutos, configuracao.RespiroMinutos);
    }

    private async Task<int> EnviarBotoesAsync(ContextoTurnoAgente contexto, IReadOnlyList<JanelaDisponivelDto> janelas, CancellationToken ct)
    {
        var conversa = contexto.Conversa;
        var botoes = janelas.Select(j => (Id: IdBotao(j.JanelaId, j.Data), Titulo: Titulo(j))).ToList();
        try
        {
            var envio = await cloudClient.EnviarBotoesAsync(
                conversa.ContatoIdExterno, CorpoBotoes, botoes.Select(b => (b.Id, b.Titulo)).ToList(), ct);

            if (conversa.EstaAberta) conversa.RegistrarSaida(contexto.Agora);
            var texto = $"{CorpoBotoes} {string.Join(" | ", botoes.Select(b => b.Titulo))}";
            await conversaRepository.AddMensagemAsync(
                Mensagem.Saida(contexto.EmpresaId, conversa.Id, AutorMensagem.Agente, contexto.Agora,
                    TipoConteudoMensagem.Texto, texto, envio.Wamid),
                ct);
            return botoes.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sem botões o agente lista as janelas em texto.
            logger.LogWarning(ex, "listar_janelas: falha ao enviar os botões da conversa {ConversaId}.", conversa.Id);
            return 0;
        }
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
