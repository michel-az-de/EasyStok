using Microsoft.EntityFrameworkCore.Storage;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public class ResetTokenRepository(EasyStockDbContext context) : IResetTokenRepository
{
    private readonly EasyStockDbContext _context = context;

    public Task<ResetToken?> GetByTokenAsync(string token)
    {
        var hash = TokenHashHelper.ComputeSha256Hash(token);
        return _context.ResetTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash);
    }

    public Task<ResetToken?> ObterAbertoAsync(Guid usuarioId, string finalidade, DateTime agora) =>
        _context.ResetTokens
            .Where(rt => rt.UsuarioId == usuarioId && rt.Finalidade == finalidade && !rt.Usado && rt.ExpiraEm > agora)
            .OrderByDescending(rt => rt.CriadoEm)
            .FirstOrDefaultAsync();

    public async Task<IEnumerable<ResetToken>> GetByUsuarioIdAsync(Guid usuarioId) =>
        await _context.ResetTokens.Where(rt => rt.UsuarioId == usuarioId).ToListAsync();

    public async Task AddAsync(ResetToken resetToken)
    {
        await _context.ResetTokens.AddAsync(resetToken);
    }

    public Task UpdateAsync(ResetToken resetToken)
    {
        _context.ResetTokens.Update(resetToken);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        var token = _context.ResetTokens.Find(id);
        if (token != null)
            _context.ResetTokens.Remove(token);
        return Task.CompletedTask;
    }

    public Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId) =>
        _context.ResetTokens.Where(rt => rt.UsuarioId == usuarioId).ExecuteDeleteAsync();

    public Task<int> InvalidarAbertosAsync(Guid usuarioId, DateTime agora) =>
        _context.ResetTokens
            .Where(rt => rt.UsuarioId == usuarioId && !rt.Usado
                && (rt.Finalidade == FinalidadeResetToken.Reset || rt.Finalidade == FinalidadeResetToken.ResetCodigo))
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.Usado, true));

    // N9: so Finalidade = Convite. O InvalidarAbertosAsync acima nunca encosta em convite, e este nunca em reset.
    public Task<int> InvalidarConvitesAbertosAsync(Guid usuarioId) =>
        _context.ResetTokens
            .Where(rt => rt.UsuarioId == usuarioId && !rt.Usado && rt.Finalidade == FinalidadeResetToken.Convite)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.Usado, true));

    public Task<int> ContarEmissoesDeConviteAsync(Guid usuarioId, DateTime desde) =>
        _context.ResetTokens.AsNoTracking()
            .CountAsync(rt => rt.UsuarioId == usuarioId && rt.Finalidade == FinalidadeResetToken.Convite
                              && rt.Canal == "Email" && rt.CriadoEm > desde);

    // UPDATE ... SET "Usado" = true WHERE "Id" = @id AND "Usado" = false AND "ExpiraEm" > @agora: uso unico de verdade.
    public async Task<bool> ConsumirAsync(Guid id, DateTime agora) =>
        await _context.ResetTokens
            .Where(rt => rt.Id == id && !rt.Usado && rt.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.Usado, true)) == 1;

    // UPDATE ... SET "Tentativas" = "Tentativas" + 1 WHERE ... AND "Tentativas" < 5 RETURNING "Tentativas": uma instrucao so,
    // entao cada chamada concorrente recebe o seu valor (1..5) e a sexta nao encontra linha. O EF nao compoe UPDATE com
    // RETURNING (SqlQuery embrulha em subselect), por isso o comando e ADO.NET, na transacao corrente quando ha.
    public async Task<int> RegistrarTentativaAsync(Guid id, DateTime agora)
    {
        await _context.Database.OpenConnectionAsync();
        try
        {
            await using var comando = _context.Database.GetDbConnection().CreateCommand();
            comando.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            comando.CommandText = """
                UPDATE reset_tokens SET "Tentativas" = "Tentativas" + 1
                WHERE "Id" = @id AND "Usado" = false AND "ExpiraEm" > @agora AND "Tentativas" < @maximo
                RETURNING "Tentativas"
                """;
            Parametro(comando, "id", id);
            Parametro(comando, "agora", agora);
            Parametro(comando, "maximo", ResetToken.TentativasMaximas);

            var valor = await comando.ExecuteScalarAsync();
            return valor is null or DBNull ? 0 : Convert.ToInt32(valor);
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private static void Parametro(System.Data.Common.DbCommand comando, string nome, object valor)
    {
        var parametro = comando.CreateParameter();
        parametro.ParameterName = nome;
        parametro.Value = valor;
        comando.Parameters.Add(parametro);
    }

    public async Task<ContagemPedidosReset> ContarPedidosAsync(Guid usuarioId, DateTime agora)
    {
        var umaHoraAtras = agora.AddHours(-1);
        var umDiaAtras = agora.AddHours(-24);

        // Uma ida ao banco: so as linhas Reset das ultimas 24 h (no maximo 6 por conta, pelo proprio limite).
        var instantes = await _context.ResetTokens.AsNoTracking()
            .Where(rt => rt.UsuarioId == usuarioId && rt.Finalidade == FinalidadeResetToken.Reset && rt.CriadoEm > umDiaAtras)
            .Select(rt => rt.CriadoEm)
            .ToListAsync();

        return new ContagemPedidosReset(
            instantes.Count(c => c > umaHoraAtras),
            instantes.Count,
            instantes.Count == 0 ? null : instantes.Max());
    }

    public Task<int> ApagarExpiradosAsync(DateTime expiradosAntesDe, CancellationToken ct = default) =>
        _context.ResetTokens.Where(rt => rt.ExpiraEm < expiradosAntesDe).ExecuteDeleteAsync(ct);
}
