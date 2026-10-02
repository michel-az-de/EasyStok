using System.Collections.Concurrent;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.Services.Auth;

/// <summary>
/// Repositório em memória de <c>reset_tokens</c> com a mesma semântica dos UPDATEs condicionais do Postgres: o consumo
/// e a tentativa só valem uma vez por linha e nunca depois da validade.
/// </summary>
internal sealed class FakeResetTokenRepository : IResetTokenRepository
{
    private readonly object _trava = new();
    public List<ResetToken> Linhas { get; } = [];
    public DateTime? CorteDaLimpeza { get; private set; }

    /// <summary>Simula o perdedor da corrida: o UPDATE condicional afeta 0 linhas mesmo com o token legível como aberto.</summary>
    public bool PerderACorridaDoConsumo { get; set; }

    public Task<ResetToken?> GetByTokenAsync(string token)
    {
        var hash = TokenHashHelper.ComputeSha256Hash(token);
        lock (_trava) return Task.FromResult(Linhas.FirstOrDefault(l => l.TokenHash == hash));
    }

    public Task<ResetToken?> ObterAbertoAsync(Guid usuarioId, string finalidade, DateTime agora)
    {
        lock (_trava)
            return Task.FromResult(Linhas
                .Where(l => l.UsuarioId == usuarioId && l.Finalidade == finalidade && !l.Usado && l.ExpiraEm > agora)
                .OrderByDescending(l => l.CriadoEm).FirstOrDefault());
    }

    public Task<IEnumerable<ResetToken>> GetByUsuarioIdAsync(Guid usuarioId)
    {
        lock (_trava) return Task.FromResult<IEnumerable<ResetToken>>(Linhas.Where(l => l.UsuarioId == usuarioId).ToList());
    }

    public Task AddAsync(ResetToken resetToken)
    {
        lock (_trava) Linhas.Add(resetToken);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ResetToken resetToken) => Task.CompletedTask;
    public Task DeleteAsync(Guid id) => Task.CompletedTask;

    public Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId)
    {
        lock (_trava) return Task.FromResult(Linhas.RemoveAll(l => l.UsuarioId == usuarioId));
    }

    public Task<int> InvalidarAbertosAsync(Guid usuarioId, DateTime agora)
    {
        lock (_trava)
        {
            var abertos = Linhas.Where(l => l.UsuarioId == usuarioId && !l.Usado
                && l.Finalidade is FinalidadeResetToken.Reset or FinalidadeResetToken.ResetCodigo).ToList();
            abertos.ForEach(l => l.Usado = true);
            return Task.FromResult(abertos.Count);
        }
    }

    public Task<int> InvalidarConvitesAbertosAsync(Guid usuarioId)
    {
        lock (_trava)
        {
            var abertos = Linhas.Where(l => l.UsuarioId == usuarioId && !l.Usado && l.Finalidade == FinalidadeResetToken.Convite).ToList();
            abertos.ForEach(l => l.Usado = true);
            return Task.FromResult(abertos.Count);
        }
    }

    public Task<int> ContarEmissoesDeConviteAsync(Guid usuarioId, DateTime desde)
    {
        lock (_trava)
            return Task.FromResult(Linhas.Count(l => l.UsuarioId == usuarioId && l.Finalidade == FinalidadeResetToken.Convite
                                                     && l.Canal == "Email" && l.CriadoEm > desde));
    }

    public Task<bool> ConsumirAsync(Guid id, DateTime agora)
    {
        lock (_trava)
        {
            var linha = Linhas.FirstOrDefault(l => l.Id == id);
            if (PerderACorridaDoConsumo || linha is null || linha.Usado || linha.ExpiraEm <= agora) return Task.FromResult(false);
            linha.Usado = true;
            return Task.FromResult(true);
        }
    }

    public Task<int> RegistrarTentativaAsync(Guid id, DateTime agora)
    {
        lock (_trava)
        {
            var linha = Linhas.FirstOrDefault(l => l.Id == id);
            if (linha is null || linha.Usado || linha.ExpiraEm <= agora || linha.Tentativas >= ResetToken.TentativasMaximas)
                return Task.FromResult(0);
            return Task.FromResult(++linha.Tentativas);
        }
    }

    public Task<ContagemPedidosReset> ContarPedidosAsync(Guid usuarioId, DateTime agora)
    {
        lock (_trava)
        {
            var pedidos = Linhas.Where(l => l.UsuarioId == usuarioId && l.Finalidade == FinalidadeResetToken.Reset).ToList();
            return Task.FromResult(new ContagemPedidosReset(
                pedidos.Count(l => l.CriadoEm > agora.AddHours(-1)),
                pedidos.Count(l => l.CriadoEm > agora.AddHours(-24)),
                pedidos.Count == 0 ? null : pedidos.Max(l => l.CriadoEm)));
        }
    }

    public Task<int> ApagarExpiradosAsync(DateTime expiradosAntesDe, CancellationToken ct = default)
    {
        lock (_trava)
        {
            CorteDaLimpeza = expiradosAntesDe;
            return Task.FromResult(Linhas.RemoveAll(l => l.ExpiraEm < expiradosAntesDe));
        }
    }
}

/// <summary>Cache em memória que respeita o TTL pelo relógio do teste.</summary>
internal sealed class FakeCacheComRelogio(TimeProvider relogio) : ICacheService
{
    private readonly ConcurrentDictionary<string, (long Valor, DateTimeOffset? Expira)> _itens = new();

    private bool Vivo(string key) =>
        _itens.TryGetValue(key, out var item) && (item.Expira is null || item.Expira > relogio.GetUtcNow());

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) => Task.CompletedTask;
    public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
    public Task RemoveAsync(string key) { _itens.TryRemove(key, out _); return Task.CompletedTask; }
    public Task<bool> ExistsAsync(string key) => Task.FromResult(Vivo(key));
    public Task RemoveAsync(IEnumerable<string> keys) { foreach (var k in keys) _itens.TryRemove(k, out _); return Task.CompletedTask; }

    public Task<long> IncrementAsync(string key, long value = 1)
    {
        if (!Vivo(key)) _itens.TryRemove(key, out _);
        var novo = _itens.AddOrUpdate(key, (value, null), (_, atual) => (atual.Valor + value, atual.Expira));
        return Task.FromResult(novo.Valor);
    }

    public Task SetExpiryAsync(string key, TimeSpan ttl)
    {
        if (_itens.TryGetValue(key, out var item))
            _itens[key] = (item.Valor, relogio.GetUtcNow().Add(ttl));
        return Task.CompletedTask;
    }
}

/// <summary>Logger que guarda tudo o que foi escrito (mensagem renderizada e parâmetros) para provar o que NÃO vazou.</summary>
internal sealed class LoggerQueGuarda<T> : ILogger<T>
{
    public List<string> Linhas { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var valores = state is IEnumerable<KeyValuePair<string, object?>> pares
            ? string.Join(";", pares.Select(p => $"{p.Key}={p.Value}"))
            : string.Empty;
        Linhas.Add(formatter(state, exception) + "|" + valores);
    }
}
