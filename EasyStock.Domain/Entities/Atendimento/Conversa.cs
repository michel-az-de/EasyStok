using System.Text.Json;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Conversa de atendimento com um contato de WhatsApp (S04, ADR-0050).
///
/// <para>
/// E o estado que o agente e o console precisam: se a dona assumiu (o agente cala,
/// RN-04), qual pedido esta em andamento, o contexto do carrinho e a janela de 24 h
/// da Meta (contada da ultima mensagem de entrada). Uma conversa fica aberta ate ser
/// encerrada; a proxima mensagem do contato abre outra. A unicidade "uma aberta por
/// contato e empresa" e garantida por indice parcial no banco.
/// </para>
///
/// <para>
/// O instante vem sempre por parametro (UTC): o dominio nao le relogio ambiente.
/// </para>
/// </summary>
public class Conversa
{
    public const int JanelaAtendimentoHoras = 24;
    public const int ContatoWaIdTamanhoMinimo = 8;
    public const int ContatoWaIdTamanhoMaximo = 15;
    public const int ContatoNomeTamanhoMaximo = 120;

    private static readonly TimeSpan JanelaAtendimento = TimeSpan.FromHours(JanelaAtendimentoHoras);

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid? ClienteId { get; private set; }

    /// <summary>Numero do contato como a Meta entrega (<c>wa_id</c>): so digitos E.164, sem <c>+</c>.</summary>
    public string ContatoWaId { get; private set; } = null!;

    /// <summary>Nome do perfil do WhatsApp, quando informado.</summary>
    public string? ContatoNome { get; private set; }

    public CanalConversa Canal { get; private set; }
    public SituacaoConversa Situacao { get; private set; }
    public Guid? PedidoEmAndamentoId { get; private set; }

    /// <summary>Objeto JSON com o estado do atendimento (carrinho em montagem, endereco extraido, etapa).</summary>
    public string ContextoJson { get; private set; } = "{}";

    public DateTime IniciadaEm { get; private set; }
    public DateTime UltimaMensagemEm { get; private set; }

    /// <summary>Ultima mensagem do cliente: e dela que a janela de 24 h da Meta e contada.</summary>
    public DateTime? UltimaMensagemEntradaEm { get; private set; }

    public int NaoLidas { get; private set; }
    public DateTime? EncerradaEm { get; private set; }
    public Guid? AssumidaPorUsuarioId { get; private set; }

    public bool EstaAberta => Situacao != SituacaoConversa.Encerrada;

    // EF Core ctor sem parametros
    private Conversa() { }

    public static Conversa Abrir(
        Guid empresaId,
        string contatoWaId,
        DateTime agora,
        string? contatoNome = null,
        Guid? clienteId = null,
        CanalConversa canal = CanalConversa.WhatsApp)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId e obrigatorio.");
        if (clienteId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ClienteId nao pode ser Guid.Empty; use null para conversa sem cliente.");

        var instante = Utc(agora);
        return new Conversa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ClienteId = clienteId,
            ContatoWaId = NormalizarWaId(contatoWaId),
            ContatoNome = NormalizarNome(contatoNome),
            Canal = canal,
            Situacao = SituacaoConversa.Automatica,
            ContextoJson = "{}",
            IniciadaEm = instante,
            UltimaMensagemEm = instante,
            UltimaMensagemEntradaEm = null,
            NaoLidas = 0,
        };
    }

    /// <summary>
    /// Reduz qualquer grafia (<c>+55 (11) 99999-0001</c>) aos digitos do <c>wa_id</c>.
    /// Entre 8 e 15 digitos (E.164). Usado tambem pelo repositorio para o lookup.
    /// </summary>
    public static string NormalizarWaId(string? waId)
    {
        var digitos = new string((waId ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitos.Length < ContatoWaIdTamanhoMinimo || digitos.Length > ContatoWaIdTamanhoMaximo)
            throw new RegraDeDominioVioladaException(
                $"ContatoWaId invalido: esperado entre {ContatoWaIdTamanhoMinimo} e {ContatoWaIdTamanhoMaximo} digitos (recebido '{waId}').");
        return digitos;
    }

    public void VincularCliente(Guid clienteId)
    {
        if (clienteId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ClienteId e obrigatorio para vincular.");
        ClienteId = clienteId;
    }

    public void AtualizarContatoNome(string? nome) => ContatoNome = NormalizarNome(nome);

    /// <summary>
    /// A dona assumiu (escreveu pelo console) ou o agente escalou. Idempotente.
    /// Em conversa encerrada e erro: quem quer falar de novo abre outra conversa.
    /// </summary>
    public void Assumir(DateTime agora, Guid? usuarioId = null)
    {
        GarantirAberta("assumir");
        Situacao = SituacaoConversa.Assumida;
        if (usuarioId is { } u && u != Guid.Empty)
            AssumidaPorUsuarioId = u;
        UltimaMensagemEm = Max(UltimaMensagemEm, Utc(agora));
    }

    /// <summary>Devolve ao agente por decisao da dona (D4). Nunca por tempo.</summary>
    public void LiberarAutomatico()
    {
        GarantirAberta("liberar o automatico de");
        Situacao = SituacaoConversa.Automatica;
        AssumidaPorUsuarioId = null;
    }

    /// <summary>Terminal e idempotente: preserva o primeiro carimbo de encerramento.</summary>
    public void Encerrar(DateTime agora)
    {
        if (!EstaAberta) return;
        Situacao = SituacaoConversa.Encerrada;
        EncerradaEm = Utc(agora);
    }

    /// <summary>Mensagem do cliente: move a janela de 24 h e conta como nao lida.</summary>
    public void RegistrarEntrada(DateTime em)
    {
        GarantirAberta("registrar mensagem em");
        var instante = Utc(em);
        UltimaMensagemEm = Max(UltimaMensagemEm, instante);
        UltimaMensagemEntradaEm = UltimaMensagemEntradaEm is { } ultima ? Max(ultima, instante) : instante;
        NaoLidas++;
    }

    /// <summary>Mensagem nossa (agente, dona ou sistema): nao mexe na janela nem nas nao lidas.</summary>
    public void RegistrarSaida(DateTime em)
    {
        GarantirAberta("registrar mensagem em");
        UltimaMensagemEm = Max(UltimaMensagemEm, Utc(em));
    }

    public void MarcarLida() => NaoLidas = 0;

    /// <summary>Aceita apenas objeto JSON; nulo ou vazio vira <c>{}</c>.</summary>
    public void DefinirContexto(string? contextoJson)
    {
        if (string.IsNullOrWhiteSpace(contextoJson))
        {
            ContextoJson = "{}";
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(contextoJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new RegraDeDominioVioladaException("ContextoJson precisa ser um objeto JSON.");
        }
        catch (JsonException ex)
        {
            throw new RegraDeDominioVioladaException($"ContextoJson invalido: {ex.Message}");
        }

        ContextoJson = contextoJson.Trim();
    }

    public void DefinirPedidoEmAndamento(Guid? pedidoId)
    {
        if (pedidoId == Guid.Empty)
            throw new RegraDeDominioVioladaException("PedidoId nao pode ser Guid.Empty; use null para limpar.");
        PedidoEmAndamentoId = pedidoId;
    }

    /// <summary>
    /// Janela de atendimento da Meta: texto livre so pode ser enviado ate 24 h depois
    /// da ultima mensagem do cliente; fora dela e preciso template aprovado.
    /// </summary>
    public bool DentroDaJanela24h(DateTime agora) =>
        UltimaMensagemEntradaEm is { } ultima && Utc(agora) - ultima < JanelaAtendimento;

    private void GarantirAberta(string acao)
    {
        if (!EstaAberta)
            throw new RegraDeDominioVioladaException($"Nao e possivel {acao} uma conversa encerrada.");
    }

    private static string? NormalizarNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        var limpo = nome.Trim();
        return limpo.Length > ContatoNomeTamanhoMaximo ? limpo[..ContatoNomeTamanhoMaximo] : limpo;
    }

    private static DateTime Max(DateTime a, DateTime b) => a >= b ? a : b;

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
