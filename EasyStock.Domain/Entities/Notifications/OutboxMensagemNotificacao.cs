using EasyStock.Domain.Enums.Notifications;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyStock.Domain.Entities.Notifications;

public class OutboxMensagemNotificacao
{
    public Guid Id { get; set; }
    public Guid EventoId { get; set; }
    public Guid? RotinaId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid? UsuarioDestinoId { get; set; }
    public CanalNotificacao Canal { get; set; }
    public string Destinatario { get; set; } = null!;
    public string AssuntoRenderizado { get; set; } = string.Empty;
    public string CorpoRenderizado { get; set; } = null!;
    public StatusOutbox Status { get; set; } = StatusOutbox.Pendente;
    public int Tentativas { get; set; }
    public int MaxTentativas { get; set; } = 3;
    public DateTime ProximaTentativaEm { get; set; }
    public DateTime? EnviadoEm { get; set; }
    public string? ProviderUsado { get; set; }

    /// <summary>
    /// Id da mensagem no provider (o <c>wamid</c> da Meta), gravado no mesmo commit do resultado do envio (N1). A N6
    /// o usa para casar o webhook de status com a mensagem do outbox.
    /// </summary>
    public string? ProviderMensagemId { get; set; }

    public string? ErroUltimaTentativa { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public string TenantTimezone { get; set; } = "America/Sao_Paulo";
    public string CanaisFallbackRestantesJson { get; set; } = "[]";
    public CategoriaConteudoNotificacao Categoria { get; set; }
    public DateTime CriadoEm { get; set; }
    public int ShardKey { get; set; }

    /// <summary>
    /// Dados do envio já renderizados que o canal interpreta (S13, fecha a pendência da S09): no WhatsApp da
    /// Meta, <c>template</c>, <c>idioma</c> e <c>param1..N</c> para o envio fora da janela de 24 h. Objeto JSON
    /// de strings; nulo quando o template não declara metadados.
    /// </summary>
    public string? MetadadosJson { get; set; }

    public EventoNotificacao? Evento { get; set; }
    public RotinaNotificacao? Rotina { get; set; }
    public TemplateNotificacao? Template { get; set; }
    public Empresa? Empresa { get; set; }
    public Usuario? UsuarioDestino { get; set; }

    public static OutboxMensagemNotificacao Criar(
        Guid eventoId,
        Guid templateId,
        Guid empresaId,
        CanalNotificacao canal,
        string destinatario,
        string assuntoRenderizado,
        string corpoRenderizado,
        CategoriaConteudoNotificacao categoria,
        Guid? rotinaId = null,
        Guid? usuarioDestinoId = null,
        string canaisFallbackRestantesJson = "[]",
        string tenantTimezone = "America/Sao_Paulo",
        int maxTentativas = 3,
        string? metadadosJson = null,
        string? chaveIdempotencia = null)
    {
        var agora = DateTime.UtcNow;
        // S13: com chave do negócio (ex.: pedido + status), reprocessar o fato gera a mesma chave mesmo vindo de
        // outro EventoNotificacao; o índice único da coluna barra a segunda linha.
        var idempotencyKey = string.IsNullOrWhiteSpace(chaveIdempotencia)
            ? ComputarIdempotencyKey(eventoId, usuarioDestinoId, canal)
            : ComputarIdempotencyKey(chaveIdempotencia.Trim(), canal);
        return new OutboxMensagemNotificacao
        {
            Id = Guid.NewGuid(),
            EventoId = eventoId,
            RotinaId = rotinaId,
            TemplateId = templateId,
            EmpresaId = empresaId,
            UsuarioDestinoId = usuarioDestinoId,
            Canal = canal,
            Destinatario = destinatario,
            AssuntoRenderizado = assuntoRenderizado,
            CorpoRenderizado = corpoRenderizado,
            Categoria = categoria,
            Status = StatusOutbox.Pendente,
            Tentativas = 0,
            MaxTentativas = maxTentativas,
            ProximaTentativaEm = agora,
            IdempotencyKey = idempotencyKey,
            TenantTimezone = tenantTimezone,
            CanaisFallbackRestantesJson = canaisFallbackRestantesJson,
            CriadoEm = agora,
            ShardKey = Convert.FromHexString(idempotencyKey)[0] % 4,
            MetadadosJson = string.IsNullOrWhiteSpace(metadadosJson) ? null : metadadosJson
        };
    }

    /// <summary>
    /// Adia o primeiro envio para <paramref name="instanteUtc"/> (S26: pedido de avaliação 30 min após a
    /// entrega). Instante no passado não antecipa nada: a mensagem já está elegível.
    /// </summary>
    public void AgendarPara(DateTime instanteUtc)
    {
        var instante = instanteUtc.Kind == DateTimeKind.Utc ? instanteUtc : instanteUtc.ToUniversalTime();
        if (instante > ProximaTentativaEm) ProximaTentativaEm = instante;
    }

    /// <summary>
    /// Lease do estado <see cref="StatusOutbox.EmEnvio"/> (N1): ao reservar a mensagem, <see cref="ProximaTentativaEm"/>
    /// vira agora + este prazo. Se o processo cair antes de gravar o resultado, o claim da rodada seguinte reclama a
    /// mensagem depois do lease. 5 minutos, como o <c>OutboxEventoIntegracao</c>.
    /// </summary>
    public static readonly TimeSpan LeaseEmEnvio = TimeSpan.FromMinutes(5);

    /// <summary>
    /// O claim do dispatcher reservou a mensagem (N1): <c>EmEnvio</c> com lease em <see cref="ProximaTentativaEm"/>.
    /// Não conta tentativa: quem conta é o desfecho do envio. No WhatsApp e no SMS o <c>EmEnvio</c> já está gravado
    /// antes de o canal ser chamado, e é isso que impede o reenvio depois de uma queda.
    /// </summary>
    public void MarcarEmEnvio(TimeSpan? lease = null)
    {
        Status = StatusOutbox.EmEnvio;
        ProximaTentativaEm = DateTime.UtcNow.Add(lease ?? LeaseEmEnvio);
    }

    public void MarcarEnviado(string providerUsado)
    {
        Status = StatusOutbox.Enviado;
        ProviderUsado = providerUsado;
        EnviadoEm = DateTime.UtcNow;
        ErroUltimaTentativa = null;
        AoTerminar();
    }

    /// <summary>
    /// Stub ou console (N2): o provider disse que não enviou nada. Terminal e sem <see cref="EnviadoEm"/>: nada
    /// saiu. Só o dispatcher chama; <see cref="ProviderUsado"/> guarda o provider real (<c>stub</c>, <c>console</c>).
    /// </summary>
    public void MarcarSimulado(string providerUsado)
    {
        Status = StatusOutbox.Simulado;
        ProviderUsado = providerUsado;
        ErroUltimaTentativa = null;
        AoTerminar();
    }

    /// <summary>
    /// Não há como saber se o provider entregou (N2): timeout, queda de conexão ou 5xx no WhatsApp e no SMS, que
    /// saem no máximo uma vez, e, na N1, o lease vencido. Terminal: conta a tentativa, nunca reagenda, e o
    /// dispatcher não abre fallback de canal, para não duplicar. <paramref name="providerUsado"/> fica como estava
    /// quando não é informado.
    /// </summary>
    public void MarcarIndeterminado(string erro, string? providerUsado = null)
    {
        Tentativas++;
        ErroUltimaTentativa = erro;
        if (providerUsado is not null) ProviderUsado = providerUsado;
        Status = StatusOutbox.Indeterminado;
        AoTerminar();
    }

    /// <summary>
    /// <paramref name="permanente"/> = erro que nunca vai passar (ex.: fora da janela de 24 h sem
    /// template, S09; SMTP 550; HTTP 4xx): vira <see cref="StatusOutbox.Falhado"/> sem reagendar.
    /// </summary>
    public void MarcarFalhaTentativa(string erro, TimeSpan backoff, bool permanente = false)
    {
        Tentativas++;
        ErroUltimaTentativa = erro;
        ProximaTentativaEm = DateTime.UtcNow.Add(backoff);
        Status = permanente || Tentativas >= MaxTentativas ? StatusOutbox.Falhado : StatusOutbox.Pendente;
        // Com tentativa sobrando a mensagem segue aberta e o corpo ainda é necessário para reenviar.
        if (Status == StatusOutbox.Falhado) AoTerminar();
    }

    /// <summary>
    /// A mensagem passou do prazo de validade do tipo antes de sair (N1, quarentena): terminal, sem
    /// <see cref="EnviadoEm"/> e sem contar tentativa, porque nada foi tentado. Termina pelo mesmo caminho das demais
    /// transições terminais (<see cref="AoTerminar"/>), então a categoria de segurança apaga o segredo.
    /// </summary>
    public void Expirar(string motivo)
    {
        Status = StatusOutbox.Expirado;
        ErroUltimaTentativa = motivo;
        AoTerminar();
    }

    /// <summary>
    /// O lease do <see cref="StatusOutbox.EmEnvio"/> venceu: o processo caiu, ou o commit do resultado falhou, depois
    /// da reserva (N1). Em e-mail, in-app e push (ao menos uma vez) a mensagem volta a <c>Pendente</c> contando a
    /// tentativa, elegível na hora (e <c>Falhado</c> se as tentativas acabaram, para não repetir para sempre). No
    /// WhatsApp e no SMS (no máximo uma vez) vira <see cref="StatusOutbox.Indeterminado"/> e nunca volta à fila:
    /// o provider pode ter sido chamado antes da queda.
    /// </summary>
    public void ReclamarLeaseVencido()
    {
        const string motivo = "Lease de envio vencido: o processo caiu depois de reservar a mensagem";
        if (Canal is CanalNotificacao.WhatsApp or CanalNotificacao.Sms)
            MarcarIndeterminado(motivo);
        else
            MarcarFalhaTentativa(motivo, TimeSpan.Zero);
    }

    /// <summary>Limite da coluna <c>ProviderMensagemId</c> (<c>varchar(128)</c>).</summary>
    public const int ProviderMensagemIdMaxLength = 128;

    /// <summary>
    /// Guarda o id da mensagem no provider (o <c>wamid</c> da Meta) para a N6 casar o webhook de status. Vazio é
    /// ignorado e o que passa do limite da coluna é cortado: um id grande demais derrubaria o commit do resultado do
    /// envio, depois de a mensagem já ter saído.
    /// </summary>
    public void RegistrarProviderMensagemId(string? idExterno)
    {
        if (string.IsNullOrWhiteSpace(idExterno)) return;
        var id = idExterno.Trim();
        ProviderMensagemId = id.Length <= ProviderMensagemIdMaxLength ? id : id[..ProviderMensagemIdMaxLength];
    }

    public void Cancelar()
    {
        Status = StatusOutbox.Cancelado;
        AoTerminar();
    }

    public void Suprimir(string motivo)
    {
        Status = StatusOutbox.Suprimido;
        ErroUltimaTentativa = motivo;
        AoTerminar();
    }

    /// <summary>
    /// Texto que substitui o corpo de uma mensagem <see cref="CategoriaConteudoNotificacao.Seguranca"/> quando ela
    /// termina (N2). A coluna do corpo é obrigatória, então o corpo não fica nulo.
    /// </summary>
    public const string CorpoApagado = "[apagado]";

    /// <summary>
    /// Apaga o que a categoria <see cref="CategoriaConteudoNotificacao.Seguranca"/> não pode guardar depois do
    /// envio (o token ou o código vai no corpo e nos metadados): o corpo vira <see cref="CorpoApagado"/> e assunto e
    /// metadados zeram. O destinatário fica até o anonimizador (90 dias). Idempotente. O payload do evento é
    /// apagado à parte (<see cref="EventoNotificacao.PurgarPayload"/>), porque o fallback de canal ainda o lê.
    /// </summary>
    public void PurgarSegredos()
    {
        CorpoRenderizado = CorpoApagado;
        AssuntoRenderizado = string.Empty;
        MetadadosJson = null;
    }

    /// <summary>
    /// Toda transição para um status terminal (qualquer um fora de <see cref="StatusOutbox.Pendente"/> e
    /// <see cref="StatusOutbox.EmEnvio"/>) passa por aqui. Quem criar uma transição terminal nova (a N1 traz
    /// <c>Expirado</c>) chama este método, para a categoria de segurança não guardar segredo em nenhum desfecho.
    /// </summary>
    private void AoTerminar()
    {
        // Terminal não agenda mais nada: ProximaTentativaEm passa a guardar o momento em que a mensagem terminou, que o
        // health de backlog (N1) usa para contar "na última hora" sem coluna nova.
        ProximaTentativaEm = DateTime.UtcNow;
        if (Categoria == CategoriaConteudoNotificacao.Seguranca)
            PurgarSegredos();
    }

    public bool TentativasEsgotadas() => Tentativas >= MaxTentativas;

    /// <summary>Chave de idempotência do outbox para uma chave de negócio no canal (S13).</summary>
    public static string ComputarIdempotencyKey(string chaveIdempotencia, CanalNotificacao canal)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"negocio|{chaveIdempotencia}|{(int)canal}"));
        return Convert.ToHexString(hash);
    }

    /// <summary><see cref="MetadadosJson"/> como dicionário; nulo quando ausente ou inválido.</summary>
    public IReadOnlyDictionary<string, string>? LerMetadados()
    {
        if (string.IsNullOrWhiteSpace(MetadadosJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(MetadadosJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ComputarIdempotencyKey(Guid eventoId, Guid? usuarioId, CanalNotificacao canal)
    {
        var raw = $"{eventoId:N}|{usuarioId?.ToString("N") ?? "_"}|{(int)canal}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
