namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Persistência de cupons no contexto Admin SaaS (CRUD completo). Encapsula o
/// que vivia no AdminCuponsController (F7), preservando a ordem de validações
/// (existência antes do parse de tipo no patch; regra de TotalUsos no delete).
/// </summary>
public interface ICupomAdminRepository
{
    Task<IReadOnlyList<CupomAdminItem>> ListarAsync(CancellationToken ct = default);

    Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default);

    Task<CupomResumo> CriarAsync(NovoCupom dados, CancellationToken ct = default);

    /// <summary>Patch parcial. O parse de TipoDesconto ocorre após confirmar a existência.</summary>
    Task<AtualizacaoCupomResultado> AtualizarAsync(Guid id, PatchCupom patch, CancellationToken ct = default);

    Task<CupomAtivoResultado?> AlternarAtivoAsync(Guid id, CancellationToken ct = default);

    Task<ExclusaoCupomResultado> ExcluirAsync(Guid id, CancellationToken ct = default);
}

public sealed record CupomAdminItem(
    Guid Id,
    string Codigo,
    string TipoDesconto,
    decimal Valor,
    int? LimiteUsos,
    int TotalUsos,
    DateTime? ValidoAte,
    Guid? PlanoId,
    bool Ativo,
    DateTime CriadoEm);

public sealed record NovoCupom(
    string Codigo, TipoDesconto Tipo, decimal Valor, int? LimiteUsos, DateTime? ValidoAte, Guid? PlanoId);

public sealed record PatchCupom(
    string? Codigo, string? TipoDesconto, decimal? Valor, int? LimiteUsos, DateTime? ValidoAte, Guid? PlanoId);

public sealed record CupomResumo(Guid Id, string Codigo);

public sealed record CupomAtivoResultado(Guid Id, bool Ativo);

public sealed record AtualizacaoCupomResultado(AtualizacaoCupomStatus Status, CupomResumo? Resumo);

public enum AtualizacaoCupomStatus { NaoEncontrado, TipoInvalido, Atualizado }

public sealed record ExclusaoCupomResultado(ExclusaoCupomStatus Status, string? Codigo);

public enum ExclusaoCupomStatus { NaoEncontrado, EmUso, Excluido }
