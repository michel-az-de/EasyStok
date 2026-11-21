namespace EasyStock.Application.Services.Auth;

/// <summary>O IP passou do teto de pedidos de acesso (N8): o controller responde 429 com <c>Retry-After</c>.</summary>
public sealed class LimitePedidosAcessoExcedidoException(int retryAfterSeconds)
    : Exception("Muitas tentativas. Tente de novo em alguns minutos.")
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// Teto por IP dos pedidos de acesso (N8): 5 em 15 min, somando <c>forgot-password</c>, <c>reset-password</c> e
/// <c>reset-password-code</c>. Conta em <see cref="ICacheService"/> (<c>IncrementAsync</c> e <c>SetExpiryAsync</c>), com a
/// chave no hash do IP, nunca o IP. O balde <c>auth</c> do ASP.NET (20 por minuto) segue como teto grosso. Sem IP
/// (jobs, testes) não limita.
/// </summary>
public sealed class LimitePedidosAcesso(ICacheService cache)
{
    public const int PedidosPorJanela = 5;

    public static readonly TimeSpan Janela = TimeSpan.FromMinutes(15);

    /// <summary>Balde padrão, somado entre os pedidos de acesso.</summary>
    public const string BaldePedidos = "pedido-acesso";

    /// <summary>#2201: a chave de acesso conta à parte, para forgot-password no Wi-Fi da loja não travar a entrada.</summary>
    public const string BaldeChaveDeAcesso = "chave-acesso";

    public static string Chave(string ip, string balde = BaldePedidos) =>
        $"auth:{balde}:v1:{TokenHashHelper.ComputeSha256Hash(ip)}";

    /// <summary>Conta este pedido. <c>false</c> quando já passou de <see cref="PedidosPorJanela"/> na janela.</summary>
    public async Task<bool> TentarAsync(string? ip, CancellationToken ct = default, string balde = BaldePedidos)
    {
        if (string.IsNullOrWhiteSpace(ip)) return true;

        var chave = Chave(ip, balde);
        var contagem = await cache.IncrementAsync(chave);
        if (contagem == 1) await cache.SetExpiryAsync(chave, Janela);
        return contagem <= PedidosPorJanela;
    }

    /// <summary>Conta o pedido e lança <see cref="LimitePedidosAcessoExcedidoException"/> quando o IP passou do teto.</summary>
    public async Task ExigirAsync(string? ip, CancellationToken ct = default, string balde = BaldePedidos)
    {
        if (!await TentarAsync(ip, ct, balde))
            throw new LimitePedidosAcessoExcedidoException((int)Janela.TotalSeconds);
    }
}
