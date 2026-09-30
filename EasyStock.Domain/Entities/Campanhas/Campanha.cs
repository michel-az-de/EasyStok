using EasyStock.Domain.Enums.Campanhas;

namespace EasyStock.Domain.Entities.Campanhas;

/// <summary>Conteúdo editável da campanha, o mesmo na criação e na edição.</summary>
public sealed record DadosCampanha(
    string Nome,
    string Mensagem,
    string? ImagemUrl,
    string? TemplateMeta,
    FiltroCampanha Filtro,
    IReadOnlyList<string> TagsRestricaoExcluidas,
    DateTime? EncerramentoEm,
    bool EnviarLembreteEncerramento,
    int? TamanhoOnda);

/// <summary>
/// Campanha de relacionamento da dona (S28, US-052 a US-059): mensagem Scriban com <c>{{nome}}</c>,
/// arte opcional, template de marketing aprovado para fora da janela de 24 h, filtro de público e as
/// tags de restrição que tiram o cliente da lista. O envio real (S30) reusa o outbox de notificações,
/// um <c>OutboxMensagemNotificacao</c> por <see cref="CampanhaDestinatario"/> com
/// <c>ProximaTentativaEm = DisparoEm</c>. Rascunho aceita conteúdo incompleto; agendar exige tudo.
/// </summary>
public class Campanha
{
    public const int NomeTamanhoMaximo = 120;
    public const int MensagemTamanhoMaximo = 4096;
    public const int ImagemUrlTamanhoMaximo = 1000;
    public const int TemplateMetaTamanhoMaximo = 120;
    public const int TagsRestricaoTamanhoMaximo = 1000;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Nome { get; private set; } = null!;

    /// <summary>Texto Scriban; <c>{{nome}}</c> vira o nome do cliente no envio.</summary>
    public string Mensagem { get; private set; } = string.Empty;

    public string? ImagemUrl { get; private set; }

    /// <summary>Nome do template de marketing aprovado na Meta (fora da janela de 24 h só template sai).</summary>
    public string? TemplateMeta { get; private set; }

    /// <summary><see cref="FiltroCampanha"/> serializado; o público é calculado na S29.</summary>
    public string FiltroJson { get; private set; } = "{}";

    /// <summary>Tags de restrição (csv, normalizadas) que excluem o cliente, ex.: <c>sem_gluten</c>.</summary>
    public string TagsRestricaoExcluidas { get; private set; } = string.Empty;

    public StatusCampanha Status { get; private set; }
    public DateTime? DisparoEm { get; private set; }
    public DateTime? EncerramentoEm { get; private set; }
    public bool EnviarLembreteEncerramento { get; private set; }

    /// <summary>Clientes por onda; <c>null</c> = todos de uma vez. Onda seguinte só por decisão da dona.</summary>
    public int? TamanhoOnda { get; private set; }

    /// <summary>0 enquanto nenhuma onda saiu.</summary>
    public int OndaAtual { get; private set; }

    public DateTime CriadaEm { get; private set; }
    public Guid CriadaPorUsuarioId { get; private set; }

    public FiltroCampanha Filtro => FiltroCampanha.DeJson(FiltroJson);

    public IReadOnlyList<string> TagsRestricao =>
        TagsRestricaoExcluidas.Split(',', StringSplitOptions.RemoveEmptyEntries);

    // EF Core ctor sem parâmetros
    private Campanha() { }

    public static Campanha Criar(Guid empresaId, Guid criadaPorUsuarioId, DadosCampanha dados, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (criadaPorUsuarioId == Guid.Empty) throw new RegraDeDominioVioladaException("Usuário criador é obrigatório.");

        var campanha = new Campanha
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Status = StatusCampanha.Rascunho,
            OndaAtual = 0,
            CriadaEm = Utc(agora),
            CriadaPorUsuarioId = criadaPorUsuarioId,
        };
        campanha.Aplicar(Normalizar(dados));
        return campanha;
    }

    /// <summary>Edita o conteúdo. Agendada continua obedecendo às regras do agendamento.</summary>
    public void Atualizar(DadosCampanha dados)
    {
        if (Status is not (StatusCampanha.Rascunho or StatusCampanha.Agendada))
            throw new RegraDeDominioVioladaException($"Campanha {Status} não pode ser editada.");

        var normalizados = Normalizar(dados);
        if (Status == StatusCampanha.Agendada) ValidarAgendamento(normalizados, DisparoEm!.Value);
        Aplicar(normalizados);
    }

    /// <summary>Agenda (ou reagenda) o disparo. Exige disparo futuro, mensagem, público e onda positiva.</summary>
    public void Agendar(DateTime disparoEm, DateTime agora)
    {
        if (Status is not (StatusCampanha.Rascunho or StatusCampanha.Agendada))
            throw new RegraDeDominioVioladaException($"Campanha {Status} não pode ser agendada.");

        var disparo = Utc(disparoEm);
        if (disparo <= Utc(agora))
            throw new RegraDeDominioVioladaException("O disparo precisa ser no futuro.");

        ValidarAgendamento(DadosAtuais(), disparo);
        DisparoEm = disparo;
        Status = StatusCampanha.Agendada;
    }

    /// <summary>Volta a agendada para rascunho, sem data de disparo.</summary>
    public void Desagendar()
    {
        if (Status != StatusCampanha.Agendada)
            throw new RegraDeDominioVioladaException($"Campanha {Status} não está agendada.");
        Status = StatusCampanha.Rascunho;
        DisparoEm = null;
    }

    /// <summary>
    /// Começa a próxima onda: a primeira a partir da agendada, as seguintes (só com
    /// <see cref="TamanhoOnda"/>) a partir da onda concluída. Devolve o número da onda.
    /// </summary>
    public int IniciarOnda(DateTime agora)
    {
        if (Status == StatusCampanha.Enviada)
        {
            if (TamanhoOnda is null)
                throw new RegraDeDominioVioladaException("Campanha sem ondas já saiu para todos.");
        }
        else if (Status != StatusCampanha.Agendada)
        {
            throw new RegraDeDominioVioladaException($"Campanha {Status} não inicia onda.");
        }

        if (Utc(agora) < DisparoEm)
            throw new RegraDeDominioVioladaException("Ainda não chegou a hora do disparo.");

        OndaAtual++;
        Status = StatusCampanha.Enviando;
        return OndaAtual;
    }

    /// <summary>
    /// A onda em curso terminou (S30): nenhum destinatário dela segue na fila do outbox. Com
    /// <see cref="TamanhoOnda"/>, a próxima fica à espera da dona (RN-42).
    /// </summary>
    public void ConcluirOnda()
    {
        if (Status != StatusCampanha.Enviando)
            throw new RegraDeDominioVioladaException($"Campanha {Status} não tem onda em curso.");
        Status = StatusCampanha.Enviada;
    }

    /// <summary>O público (S29) só é recalculado enquanto a campanha pode mandar mais alguma onda.</summary>
    public void GarantirPublicoRecalculavel()
    {
        if (Status is StatusCampanha.Encerrada or StatusCampanha.Cancelada)
            throw new RegraDeDominioVioladaException($"Campanha {Status} não recalcula o público.");
    }

    public void Encerrar()
    {
        if (Status is not (StatusCampanha.Enviando or StatusCampanha.Enviada))
            throw new RegraDeDominioVioladaException($"Campanha {Status} não pode ser encerrada: ainda não saiu.");
        Status = StatusCampanha.Encerrada;
    }

    /// <summary>
    /// Cancela. Destinatários ainda <see cref="StatusCampanhaDestinatario.Pendente"/> viram
    /// <see cref="StatusCampanhaDestinatario.Excluido"/> com motivo <see cref="MotivoExclusaoCampanha.Cancelada"/>;
    /// os demais (enviados, já excluídos, enfileirados) ficam como estão. Devolve quantos excluiu.
    /// </summary>
    public int Cancelar(IEnumerable<CampanhaDestinatario> destinatarios)
    {
        if (Status is StatusCampanha.Encerrada or StatusCampanha.Cancelada)
            throw new RegraDeDominioVioladaException($"Campanha {Status} não pode ser cancelada.");

        var lista = destinatarios.ToList();
        if (lista.Any(d => d.CampanhaId != Id))
            throw new RegraDeDominioVioladaException("Destinatário de outra campanha.");

        var excluidos = 0;
        foreach (var destinatario in lista.Where(d => d.Status == StatusCampanhaDestinatario.Pendente))
        {
            destinatario.Excluir(MotivoExclusaoCampanha.Cancelada);
            excluidos++;
        }

        Status = StatusCampanha.Cancelada;
        return excluidos;
    }

    internal static IReadOnlyList<string> NormalizarTags(IEnumerable<string>? tags) =>
        (tags ?? []).Select(ClienteTag.NormalizarValidando).Distinct(StringComparer.Ordinal).ToList();

    private static DadosCampanha Normalizar(DadosCampanha dados)
    {
        var nome = dados.Nome?.Trim() ?? string.Empty;
        if (nome.Length == 0) throw new RegraDeDominioVioladaException("Nome da campanha é obrigatório.");
        if (nome.Length > NomeTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Nome acima de {NomeTamanhoMaximo} caracteres.");

        var mensagem = dados.Mensagem?.Trim() ?? string.Empty;
        if (mensagem.Length > MensagemTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Mensagem acima de {MensagemTamanhoMaximo} caracteres.");

        var imagem = Opcional(dados.ImagemUrl, ImagemUrlTamanhoMaximo, "URL da arte");
        var template = Opcional(dados.TemplateMeta, TemplateMetaTamanhoMaximo, "Nome do template");

        var tags = NormalizarTags(dados.TagsRestricaoExcluidas);
        if (string.Join(',', tags).Length > TagsRestricaoTamanhoMaximo)
            throw new RegraDeDominioVioladaException("Tags de restrição demais.");

        return dados with
        {
            Nome = nome,
            Mensagem = mensagem,
            ImagemUrl = imagem,
            TemplateMeta = template,
            Filtro = (dados.Filtro ?? FiltroCampanha.Vazio).Normalizado(),
            TagsRestricaoExcluidas = tags,
            EncerramentoEm = dados.EncerramentoEm is { } fim ? Utc(fim) : null,
        };
    }

    private static void ValidarAgendamento(DadosCampanha dados, DateTime disparo)
    {
        if (string.IsNullOrWhiteSpace(dados.Mensagem))
            throw new RegraDeDominioVioladaException("Mensagem vazia: escreva o texto antes de agendar.");
        if (dados.TamanhoOnda is <= 0)
            throw new RegraDeDominioVioladaException("Tamanho da onda precisa ser maior que zero.");
        if (!dados.Filtro.TemPublico)
            throw new RegraDeDominioVioladaException("Sem público: escolha todos, uma tag ou um item comprado.");
        if (dados.EncerramentoEm is { } fim && fim <= disparo)
            throw new RegraDeDominioVioladaException("A data de encerramento precisa ser depois do disparo.");
        if (dados.EnviarLembreteEncerramento && dados.EncerramentoEm is null)
            throw new RegraDeDominioVioladaException("Sem data de encerramento não há lembrete de encerramento.");
    }

    private void Aplicar(DadosCampanha dados)
    {
        Nome = dados.Nome;
        Mensagem = dados.Mensagem;
        ImagemUrl = dados.ImagemUrl;
        TemplateMeta = dados.TemplateMeta;
        FiltroJson = dados.Filtro.ParaJson();
        TagsRestricaoExcluidas = string.Join(',', dados.TagsRestricaoExcluidas);
        EncerramentoEm = dados.EncerramentoEm;
        EnviarLembreteEncerramento = dados.EnviarLembreteEncerramento;
        TamanhoOnda = dados.TamanhoOnda;
    }

    private DadosCampanha DadosAtuais() => new(
        Nome, Mensagem, ImagemUrl, TemplateMeta, Filtro, TagsRestricao, EncerramentoEm, EnviarLembreteEncerramento, TamanhoOnda);

    private static string? Opcional(string? valor, int maximo, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var aparado = valor.Trim();
        if (aparado.Length > maximo)
            throw new RegraDeDominioVioladaException($"{campo} acima de {maximo} caracteres.");
        return aparado;
    }

    internal static DateTime Utc(DateTime valor) => valor.Kind switch
    {
        DateTimeKind.Utc => valor,
        DateTimeKind.Local => valor.ToUniversalTime(),
        _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc),
    };
}
