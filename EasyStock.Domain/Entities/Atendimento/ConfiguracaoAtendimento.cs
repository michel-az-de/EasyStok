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

    /// <summary>
    /// Enviada antes do agente, na primeira mensagem de uma conversa nova (S05). Sem link (#2544, D1): o Assistente
    /// conduz e oferece a comanda só a quem quer escolher sozinho. <c>{link}</c> no texto da loja ainda vira o cardápio.
    /// </summary>
    public string SaudacaoPrimeiroContato { get; set; } = SaudacaoPrimeiroContatoPadrao;

    /// <summary>Com <c>{nome}</c> — cliente já conhecido, identificado pelo telefone (S05). Sem link (#2544).</summary>
    public string SaudacaoRetorno { get; set; } = SaudacaoRetornoPadrao;

    public const string SaudacaoPrimeiroContatoPadrao = "Oi! Aqui é a Casa da Baba.";
    public const string SaudacaoRetornoPadrao = "Oi, {nome}! Que bom ter você de volta.";

    /// <summary>Frase de espera enviada junto da saudação, antes do agente responder (RN-01).</summary>
    public string FraseEspera { get; set; } = "Um instante, a Casa já responde.";

    public string MensagemForaArea { get; set; } = "Por enquanto a gente só entrega em algumas regiões.";

    public const int MoldeTamanhoMaximo = 500;
    /// <summary>
    /// Texto padrão gravado da escalada. Fica sem marcador para um rollback só de código (<c>vps-deploy.sh --rollback</c>)
    /// nunca mandar "{dona}" ao cliente; a escalada com ele sai como <see cref="MensagemEscaladaComDona"/> (S68, #2597).
    /// </summary>
    public const string MensagemEscaladaPadrao = "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.";
    /// <summary>S68 (#2597): <c>{dona}</c> vira <see cref="NomeDona"/>; sem nome, "a equipe" (o mesmo texto do padrão).</summary>
    public const string MensagemEscaladaComDona = "Vou encaminhar sua mensagem para {dona} conferir e responder por aqui.";
    public const string MensagemEncaminhamentoPadrao =
        "Oi, {nome}! Eu sou o atendimento automático da Casa da Baba. Já passei sua mensagem para a responsável, que te responde por aqui.";

    /// <summary>#2249: o que o robô diz ao passar a conversa de cliente para a equipe. Com <c>{nome}</c>.</summary>
    public string MensagemEscalada { get; private set; } = MensagemEscaladaPadrao;

    /// <summary>S68 (#2597): o molde que a escalada usa; o texto padrão gravado passa a citar a dona.</summary>
    public string MoldeEscalada() => MensagemEscalada == MensagemEscaladaPadrao ? MensagemEscaladaComDona : MensagemEscalada;

    /// <summary>#2249: o que o robô diz a quem não é cliente (fornecedor, pessoal) ao encaminhar à responsável. Com <c>{nome}</c>.</summary>
    public string MensagemEncaminhamento { get; private set; } = MensagemEncaminhamentoPadrao;

    /// <summary>#2249: vazio mantém o molde atual.</summary>
    public void DefinirMoldes(string? escalada, string? encaminhamento)
    {
        var escaladaLimpa = string.IsNullOrWhiteSpace(escalada) ? null : escalada.Trim();
        var encaminhamentoLimpo = string.IsNullOrWhiteSpace(encaminhamento) ? null : encaminhamento.Trim();
        if (escaladaLimpa?.Length > MoldeTamanhoMaximo || encaminhamentoLimpo?.Length > MoldeTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"A mensagem passa de {MoldeTamanhoMaximo} caracteres.");
        if (escaladaLimpa is not null) MensagemEscalada = escaladaLimpa;
        if (encaminhamentoLimpo is not null) MensagemEncaminhamento = encaminhamentoLimpo;
        AlteradoEm = DateTime.UtcNow;
    }

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

    /// <summary>
    /// Agente automático da loja. Nasce desligado (#1889, decisão do Felipe em 20/10/2026): a dona liga em
    /// Atendimento › Horários quando quiser que a IA responda sozinha.
    /// </summary>
    public bool Ativo { get; set; }

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

    public const int NomeDonaTamanhoMaximo = 60;
    public const int ModeloAgenteTamanhoMaximo = 200;
    public const int ObservacoesLojaTamanhoMaximo = 500;
    public const int RoteiroTamanhoMaximo = 20_000;

    /// <summary>S68 (#2597): como o robô chama a dona na escalada e no prompt. Nulo: "a dona" e "a equipe".</summary>
    public string? NomeDona { get; private set; }

    /// <summary>S68 (ADR-0066): modelo do Assistente da loja, conferido na lista aprovada pelo caso de uso. Nulo: o padrão.</summary>
    public string? ModeloAgente { get; private set; }

    /// <summary>S68: limite mensal de tokens do Assistente, aplicado pela etapa da cota (S69, #2853). Nulo: sem cota.</summary>
    public long? CotaTokensMes { get; private set; }

    /// <summary>S68: etapas do roteiro em JSON, no formato da aplicação. Nulo: o roteiro padrão.</summary>
    public string? RoteiroJson { get; private set; }

    /// <summary>S68: orientação livre da loja para o Assistente, abaixo das regras. Nulo: nenhuma.</summary>
    public string? ObservacoesLoja { get; private set; }

    public const int FerramentasDesligadasTamanhoMaximo = 4_000;

    /// <summary>#2853: nomes das ferramentas do Assistente que a loja desligou, em JSON. Nulo: todas ligadas.</summary>
    public string? FerramentasDesligadasJson { get; private set; }

    /// <summary>#2853: nulo mantém; vazio liga todas de novo. Quem valida os nomes é o caso de uso.</summary>
    public void DefinirFerramentasDesligadas(string? json)
    {
        if (json is null) return;
        if (json.Length > FerramentasDesligadasTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"A lista de ferramentas desligadas passa de {FerramentasDesligadasTamanhoMaximo} caracteres.");
        FerramentasDesligadasJson = Vazio(json);
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// S68 (#2597): nulo mantém o valor; vazio (ou cota 0) volta ao padrão. Valida tudo antes de mudar qualquer campo.
    /// </summary>
    public void DefinirAssistente(string? nomeDona, string? modeloAgente, long? cotaTokensMes, string? roteiroJson,
        string? observacoesLoja)
    {
        var nome = nomeDona?.Trim();
        var modelo = modeloAgente?.Trim();
        var roteiro = roteiroJson?.Trim();
        var observacoes = observacoesLoja?.Trim();
        // Sem chaves: um "{nome}" no nome da dona seria trocado pelo nome do cliente no molde.
        if (nome?.Length > NomeDonaTamanhoMaximo || nome?.Any(c => char.IsControl(c) || c is '{' or '}') == true)
            throw new RegraDeDominioVioladaException(
                $"O nome da dona tem de caber em uma linha de até {NomeDonaTamanhoMaximo} caracteres, sem chaves.");
        if (modelo?.Length > ModeloAgenteTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"O modelo passa de {ModeloAgenteTamanhoMaximo} caracteres.");
        if (cotaTokensMes is < 0)
            throw new RegraDeDominioVioladaException("A cota mensal de tokens não pode ser negativa.");
        if (roteiro?.Length > RoteiroTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"O roteiro passa de {RoteiroTamanhoMaximo} caracteres.");
        if (observacoes?.Length > ObservacoesLojaTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"As observações passam de {ObservacoesLojaTamanhoMaximo} caracteres.");

        if (nome is not null) NomeDona = Vazio(nome);
        if (modelo is not null) ModeloAgente = Vazio(modelo);
        if (cotaTokensMes.HasValue) CotaTokensMes = cotaTokensMes == 0 ? null : cotaTokensMes;
        if (roteiro is not null) RoteiroJson = Vazio(roteiro);
        if (observacoes is not null) ObservacoesLoja = Vazio(observacoes);
        AlteradoEm = DateTime.UtcNow;
    }

    private static string? Vazio(string texto) => texto.Length == 0 ? null : texto;

    /// <summary>Avisos da operação para a dona e o gerente (#2096). Linha antiga sem o documento lê o padrão.</summary>
    public AvisosOperacao Avisos { get; private set; } = AvisosOperacao.Padrao;

    public void DefinirAvisos(AvisosOperacao avisos)
    {
        ArgumentNullException.ThrowIfNull(avisos);
        Avisos = avisos.Validado();
        AlteradoEm = DateTime.UtcNow;
    }

    public void RegistrarWebhookVerificado(DateTime quando) => WebhookVerificadoEm = quando;

    public void RegistrarMensagemRecebida(DateTime quando) => UltimaMensagemRecebidaEm = quando;
}

/// <summary>S58: modelo aprovado que reabre a conversa fora da janela de 24 h.</summary>
public sealed record ModeloRetomada(string Nome, string Idioma);
