namespace EasyStock.Application.Ports.Output.Atendimento;

public sealed record CredencialWppConnect(string Sessao, string Token, DateTimeOffset InicioDaCaptura = default)
{
    public const string ProviderKey = "wppconnect";
    public static string NomeSessao(Guid empresaId) => $"ez-{empresaId:N}";
    public static Guid? EmpresaDaSessao(string sessao) =>
        sessao.StartsWith("ez-", StringComparison.Ordinal) && Guid.TryParseExact(sessao[3..], "N", out var id)
            && id != Guid.Empty ? id : null;
    public string IdExterno(string id) => $"wpp:{Sessao}:{id}";
}

/// <summary>Mensagem recente de uma conversa, lida em <c>get-messages</c> (#2590).</summary>
public sealed record MensagemRecenteWppConnect(string Id, long Quando, bool DeMim, string? Tipo, string? Corpo);

/// <summary>#3117: a última mensagem recebida que a ponte enxerga numa conversa individual, lida em <c>list-chats</c>.</summary>
public sealed record UltimaRecebidaWppConnect(string Chat, string MensagemId);

public sealed record EstadoWppConnect(bool Habilitado, bool Vinculado, bool Conectado, string Estado, string? QrCode = null);

public interface IWppConnectSessaoClient
{
    bool Habilitado { get; }
    Task<string> GerarTokenAsync(string sessao, CancellationToken ct = default);
    Task IniciarAsync(CredencialWppConnect sessao, CancellationToken ct = default);
    Task FecharAsync(CredencialWppConnect sessao, bool sair, CancellationToken ct = default);
    Task<EstadoWppConnect> ObterEstadoAsync(CredencialWppConnect sessao, CancellationToken ct = default);
    Task<string> ResolverTelefoneAsync(CredencialWppConnect sessao, string lid, CancellationToken ct = default);
    Task<IReadOnlyList<MensagemRecenteWppConnect>> ListarMensagensRecentesAsync(CredencialWppConnect sessao, string chat,
        CancellationToken ct = default);
    Task<IReadOnlyList<UltimaRecebidaWppConnect>> ListarUltimasRecebidasAsync(CredencialWppConnect sessao, int maximo,
        CancellationToken ct = default);
}
