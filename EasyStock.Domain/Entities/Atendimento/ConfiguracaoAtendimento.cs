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

    /// <summary>Carimbo do status de integração (S01/S03) — nulo até o webhook confirmar.</summary>
    public DateTime? WebhookVerificadoEm { get; set; }
    public DateTime? UltimaMensagemRecebidaEm { get; set; }

    public bool Ativo { get; set; } = true;

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
        bool? ativo)
    {
        if (respiroMinutos is < 0)
            throw new ArgumentOutOfRangeException(nameof(respiroMinutos), "RespiroMinutos não pode ser negativo.");
        if (tempoPreparoPadraoMinutos is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tempoPreparoPadraoMinutos), "TempoPreparoPadraoMinutos deve ser maior que zero.");

        if (!string.IsNullOrWhiteSpace(tom)) Tom = tom.Trim();
        if (nivelSugestao.HasValue) NivelSugestao = nivelSugestao.Value;
        if (!string.IsNullOrWhiteSpace(saudacaoPrimeiroContato)) SaudacaoPrimeiroContato = saudacaoPrimeiroContato.Trim();
        if (!string.IsNullOrWhiteSpace(saudacaoRetorno)) SaudacaoRetorno = saudacaoRetorno.Trim();
        if (!string.IsNullOrWhiteSpace(fraseEspera)) FraseEspera = fraseEspera.Trim();
        if (!string.IsNullOrWhiteSpace(mensagemForaArea)) MensagemForaArea = mensagemForaArea.Trim();
        if (respiroMinutos.HasValue) RespiroMinutos = respiroMinutos.Value;
        if (tempoPreparoPadraoMinutos.HasValue) TempoPreparoPadraoMinutos = tempoPreparoPadraoMinutos.Value;
        if (ativo.HasValue) Ativo = ativo.Value;
        AlteradoEm = DateTime.UtcNow;
    }

    public void RegistrarWebhookVerificado(DateTime quando) => WebhookVerificadoEm = quando;

    public void RegistrarMensagemRecebida(DateTime quando) => UltimaMensagemRecebidaEm = quando;
}
