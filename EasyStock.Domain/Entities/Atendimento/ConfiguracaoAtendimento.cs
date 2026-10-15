using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Configuração do atendimento por WhatsApp de UMA empresa (ADR-0050) — tom, autonomia do agente
/// (S06), saudações (S05) e tempo de preparo padrão. <see cref="EmpresaId"/> é a chave primária:
/// uma linha por tenant, criada sob demanda (ver <see cref="CriarPadrao"/>) — <c>GET</c> sem
/// registro persistido devolve o padrão em memória, nunca 404.
/// </summary>
public class ConfiguracaoAtendimento
{
    public Guid EmpresaId { get; set; }

    /// <summary>Tom curto para o system prompt do agente, ex.: "acolhedor, direto, sem gíria".</summary>
    public string Tom { get; set; } = "acolhedor, direto, sem gíria";

    public NivelSugestaoAtendimento NivelSugestao { get; set; } = NivelSugestaoAtendimento.Discreto;

    /// <summary>Enviada antes do agente, na primeira mensagem de uma conversa nova (S05).</summary>
    public string SaudacaoPrimeiroContato { get; set; } =
        "Oi! Seja bem-vindo(a) à Casa da Baba 🍝 Dá uma olhada no nosso cardápio: {link}";

    /// <summary>Com <c>{nome}</c> — cliente já conhecido, identificado pelo telefone (S05).</summary>
    public string SaudacaoRetorno { get; set; } =
        "Oi, {nome}! Que bom te ver de novo por aqui. Segue nosso cardápio: {link}";

    /// <summary>Frase de espera enviada junto da saudação, antes do agente responder (RN-01).</summary>
    public string FraseEspera { get; set; } = "Só um instante que já te ajudo!";

    public string MensagemForaArea { get; set; } = "Por enquanto a gente só entrega em algumas regiões.";

    /// <summary>Minutos de respiro entre uma sugestão e outra do agente — evita insistência.</summary>
    public int RespiroMinutos { get; set; } = 40;

    public int TempoPreparoPadraoMinutos { get; set; } = 60;

    public const int SlaRespostaPadraoMinutos = 5;
    public const int SlaRespostaMinimoMinutos = 1;
    public const int SlaRespostaMaximoMinutos = 240;

    /// <summary>
    /// Prazo de primeira resposta da loja (#1427): minutos que o cliente pode esperar depois de falar por
    /// último. Um só por loja, não por canal. Estourado, o cartão pisca no console e nasce o lembrete
    /// <c>ClienteSemResposta</c>. Console e avaliador pausam a contagem fora do expediente.
    /// </summary>
    public int SlaRespostaMinutos { get; set; } = SlaRespostaPadraoMinutos;

    /// <summary>Carimbo do status de integração (S01/S03) — nulo até o webhook confirmar.</summary>
    public DateTime? WebhookVerificadoEm { get; set; }
    public DateTime? UltimaMensagemRecebidaEm { get; set; }

    public bool Ativo { get; set; } = true;

    public const int ModeloNomeTamanhoMaximo = 512;
    public const string IdiomaModeloPadrao = "pt_BR";

    /// <summary>
    /// S58 (#1391): modelo aprovado na Meta que reabre a conversa quando a janela de 24 h venceu. Precisa ter
    /// exatamente uma variável no corpo, o primeiro nome do cliente. Nulo: sem retomada.
    /// </summary>
    public string? ModeloRetomadaNome { get; private set; }
    public string ModeloRetomadaIdioma { get; private set; } = IdiomaModeloPadrao;

    public ModeloRetomada? ModeloRetomada =>
        ModeloRetomadaNome is null ? null : new ModeloRetomada(ModeloRetomadaNome, ModeloRetomadaIdioma);

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public static ConfiguracaoAtendimento CriarPadrao(Guid empresaId)
    {
        var agora = DateTime.UtcNow;
        return new ConfiguracaoAtendimento
        {
            EmpresaId = empresaId,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    public void Atualizar(
        string? tom,
        NivelSugestaoAtendimento? nivelSugestao,
        string? saudacaoPrimeiroContato,
        string? saudacaoRetorno,
        string? fraseEspera,
        string? mensagemForaArea,
        int? respiroMinutos,
        int? tempoPreparoPadraoMinutos,
        bool? ativo,
        int? slaRespostaMinutos = null)
    {
        if (respiroMinutos is < 0)
            throw new ArgumentOutOfRangeException(nameof(respiroMinutos), "RespiroMinutos não pode ser negativo.");
        if (tempoPreparoPadraoMinutos is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tempoPreparoPadraoMinutos), "TempoPreparoPadraoMinutos deve ser maior que zero.");
        if (slaRespostaMinutos is < SlaRespostaMinimoMinutos or > SlaRespostaMaximoMinutos)
            throw new ArgumentOutOfRangeException(nameof(slaRespostaMinutos),
                $"SlaRespostaMinutos deve ficar entre {SlaRespostaMinimoMinutos} e {SlaRespostaMaximoMinutos}.");

        if (!string.IsNullOrWhiteSpace(tom)) Tom = tom.Trim();
        if (nivelSugestao.HasValue) NivelSugestao = nivelSugestao.Value;
        if (!string.IsNullOrWhiteSpace(saudacaoPrimeiroContato)) SaudacaoPrimeiroContato = saudacaoPrimeiroContato.Trim();
        if (!string.IsNullOrWhiteSpace(saudacaoRetorno)) SaudacaoRetorno = saudacaoRetorno.Trim();
        if (!string.IsNullOrWhiteSpace(fraseEspera)) FraseEspera = fraseEspera.Trim();
        if (!string.IsNullOrWhiteSpace(mensagemForaArea)) MensagemForaArea = mensagemForaArea.Trim();
        if (respiroMinutos.HasValue) RespiroMinutos = respiroMinutos.Value;
        if (tempoPreparoPadraoMinutos.HasValue) TempoPreparoPadraoMinutos = tempoPreparoPadraoMinutos.Value;
        if (ativo.HasValue) Ativo = ativo.Value;
        if (slaRespostaMinutos.HasValue) SlaRespostaMinutos = slaRespostaMinutos.Value;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>S58: nome vazio desliga a retomada. O nome segue o formato da Meta (minúsculas, dígitos e _).</summary>
    public void DefinirModeloRetomada(string? nome, string? idioma)
    {
        var limpo = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();
        if (limpo is not null && (limpo.Length > ModeloNomeTamanhoMaximo || !limpo.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')))
            throw new RegraDeDominioVioladaException("Nome do modelo deve usar só letras minúsculas, dígitos e _ (formato da Meta).");
        ModeloRetomadaNome = limpo;
        ModeloRetomadaIdioma = string.IsNullOrWhiteSpace(idioma) ? IdiomaModeloPadrao : idioma.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    public void RegistrarWebhookVerificado(DateTime quando) => WebhookVerificadoEm = quando;

    public void RegistrarMensagemRecebida(DateTime quando) => UltimaMensagemRecebidaEm = quando;
}

/// <summary>S58: modelo aprovado que reabre a conversa fora da janela de 24 h.</summary>
public sealed record ModeloRetomada(string Nome, string Idioma);
