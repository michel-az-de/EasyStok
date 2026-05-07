using EasyStock.Domain.Enums.Notifications;
using System.Security.Cryptography;
using System.Text;

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
        int maxTentativas = 3)
    {
        var agora = DateTime.UtcNow;
        var idempotencyKey = ComputarIdempotencyKey(eventoId, usuarioDestinoId, canal);
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
            ShardKey = Convert.FromHexString(idempotencyKey)[0] % 4
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

    public void MarcarFalhaTentativa(string erro, TimeSpan backoff)
    {
        Tentativas++;
        ErroUltimaTentativa = erro;
        ProximaTentativaEm = DateTime.UtcNow.Add(backoff);
        Status = Tentativas >= MaxTentativas ? StatusOutbox.Falhado : StatusOutbox.Pendente;
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

    private static string ComputarIdempotencyKey(Guid eventoId, Guid? usuarioId, CanalNotificacao canal)
    {
        var raw = $"{eventoId:N}|{usuarioId?.ToString("N") ?? "_"}|{(int)canal}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
