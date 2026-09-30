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

    public void MarcarEmEnvio()
    {
        Status = StatusOutbox.EmEnvio;
    }

    public void MarcarEnviado(string providerUsado)
    {
        Status = StatusOutbox.Enviado;
        ProviderUsado = providerUsado;
        EnviadoEm = DateTime.UtcNow;
        ErroUltimaTentativa = null;
    }

    /// <summary>
    /// <paramref name="permanente"/> = erro que nunca vai passar (ex.: fora da janela de 24 h sem
    /// template, S09): vira <see cref="StatusOutbox.Falhado"/> sem reagendar.
    /// </summary>
    public void MarcarFalhaTentativa(string erro, TimeSpan backoff, bool permanente = false)
    {
        Tentativas++;
        ErroUltimaTentativa = erro;
        ProximaTentativaEm = DateTime.UtcNow.Add(backoff);
        Status = permanente || Tentativas >= MaxTentativas ? StatusOutbox.Falhado : StatusOutbox.Pendente;
    }

    public void Cancelar()
    {
        Status = StatusOutbox.Cancelado;
    }

    public void Suprimir(string motivo)
    {
        Status = StatusOutbox.Suprimido;
        ErroUltimaTentativa = motivo;
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
