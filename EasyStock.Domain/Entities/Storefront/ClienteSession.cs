namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Sessão server-side do cliente storefront (ADR-0012).
///
/// <para>
/// <see cref="Id"/> = <c>sid</c> embutido no JWT do cookie <c>__Host-cdb_session</c>.
/// JWT roda 24h (rotação automática pelo middleware); cookie tem Max-Age=30d.
/// Cookie vazado tem max 24h de uso útil + revogação manual via <see cref="Revogar"/>.
/// </para>
///
/// <para>
/// <strong>Sliding window de 30 dias</strong>: <see cref="EstaValida"/> retorna
/// <c>false</c> se <see cref="UltimoUsoEm"/> &gt; 30 dias atrás OU
/// <see cref="Revogada"/> = <c>true</c>. Middleware atualiza
/// <see cref="UltimoUsoEm"/> a cada request crítico via <see cref="RegistrarUso"/>.
/// </para>
///
/// <para>
/// <strong>Tempo via TimeProvider</strong> (ADR-0012): factory, RegistrarUso e
/// EstaValida recebem <see cref="TimeProvider"/> injetado.
/// </para>
/// </summary>
public class ClienteSession
{
    public static readonly TimeSpan SlidingWindow = TimeSpan.FromDays(30);

    public Guid Id { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid EmpresaId { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime UltimoUsoEm { get; private set; }

    /// <summary>IP da emissão (logging/observabilidade). Max 45 chars (IPv6).</summary>
    public string? IpInicial { get; private set; }

    /// <summary>User-Agent da emissão (logging). Max 300 chars.</summary>
    public string? UaInicial { get; private set; }

    /// <summary>
    /// SHA-256 hex de (UserAgent + Accept-Language) no momento da emissão.
    /// Validado pelo middleware a cada request — divergência força re-login (anti-hijacking).
    /// Pode ser null para sessões legadas ou quando UA/AL não disponíveis.
    /// </summary>
    public string? Fingerprint { get; private set; }

    public bool Revogada { get; private set; }
    public string? MotivoRevogacao { get; private set; }

    // EF Core ctor sem parâmetros
    private ClienteSession() { }

    public static ClienteSession Criar(
        Guid clienteId,
        Guid empresaId,
        TimeProvider time,
        string? ipInicial = null,
        string? uaInicial = null,
        string? fingerprint = null)
    {
        if (clienteId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ClienteId é obrigatório.");
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        ArgumentNullException.ThrowIfNull(time);

        var agora = time.GetUtcNow().UtcDateTime;
        return new ClienteSession
        {
            Id = Guid.NewGuid(),  // = sid no JWT
            ClienteId = clienteId,
            EmpresaId = empresaId,
            CriadoEm = agora,
            UltimoUsoEm = agora,
            IpInicial = string.IsNullOrWhiteSpace(ipInicial) ? null : ipInicial.Trim(),
            UaInicial = string.IsNullOrWhiteSpace(uaInicial) ? null : uaInicial.Trim(),
            Fingerprint = string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint.Trim(),
            Revogada = false,
            MotivoRevogacao = null,
        };
    }

    /// <summary>
    /// Atualiza <see cref="UltimoUsoEm"/> para "agora" (sliding window).
    /// No-op se sessão revogada.
    /// </summary>
    public void RegistrarUso(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        if (Revogada) return;
        UltimoUsoEm = time.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Revoga sessão (logout, suspeita de hijacking, admin force). Idempotente:
    /// segunda chamada NÃO sobrescreve <see cref="MotivoRevogacao"/> original.
    /// </summary>
    public void Revogar(string motivo)
    {
        if (Revogada) return;
        if (string.IsNullOrWhiteSpace(motivo))
            throw new RegraDeDominioVioladaException("Motivo da revogação é obrigatório.");

        Revogada = true;
        MotivoRevogacao = motivo.Trim();
    }

    /// <summary>
    /// <c>false</c> se <see cref="Revogada"/> OR último uso &gt; 30 dias atrás.
    /// </summary>
    public bool EstaValida(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        if (Revogada) return false;

        var idade = time.GetUtcNow().UtcDateTime - UltimoUsoEm;
        return idade <= SlidingWindow;
    }
}
