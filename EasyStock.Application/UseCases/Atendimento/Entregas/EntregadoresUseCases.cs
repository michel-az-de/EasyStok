using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

public sealed record EntregadorResult(
    Guid Id, string Nome, TipoEntregador Tipo, EmpresaEntregador Empresa, string? Telefone, string? Veiculo,
    string? Placa, bool Ativo)
{
    internal static EntregadorResult De(Entregador e) => new(e.Id, e.Nome, e.Tipo, e.Empresa, e.Telefone, e.Veiculo, e.Placa, e.Ativo);
}

public sealed class EntregadorNaoEncontradoException(Guid id) : Exception($"Entregador {id} não encontrado.");

public sealed record CriarEntregadorCommand(
    Guid EmpresaId, string Nome, TipoEntregador Tipo, EmpresaEntregador Empresa, string? Telefone, string? Veiculo, string? Placa);

public sealed record AtualizarEntregadorCommand(
    Guid EmpresaId, Guid Id, string Nome, TipoEntregador Tipo, EmpresaEntregador Empresa, string? Telefone, string? Veiculo, string? Placa);

/// <summary>S44: cadastro manual do entregador.</summary>
public sealed class CriarEntregadorUseCase(IEntregadorRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<EntregadorResult> ExecuteAsync(CriarEntregadorCommand c, CancellationToken ct = default)
    {
        var entregador = Regra.Validar(() => Entregador.Criar(
            c.EmpresaId, c.Nome, c.Tipo, c.Empresa, c.Telefone, c.Veiculo, c.Placa, relogio.GetUtcNow().UtcDateTime));
        await repository.AddAsync(entregador, ct);
        await unitOfWork.CommitAsync();
        return EntregadorResult.De(entregador);
    }
}

/// <summary>S44: editar o cadastro não mexe no retrato das paradas já despachadas.</summary>
public sealed class AtualizarEntregadorUseCase(IEntregadorRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<EntregadorResult> ExecuteAsync(AtualizarEntregadorCommand c, CancellationToken ct = default)
    {
        var entregador = await repository.ObterAsync(c.EmpresaId, c.Id, ct) ?? throw new EntregadorNaoEncontradoException(c.Id);
        Regra.Validar(() => entregador.Atualizar(c.Nome, c.Tipo, c.Empresa, c.Telefone, c.Veiculo, c.Placa, relogio.GetUtcNow().UtcDateTime));
        await unitOfWork.CommitAsync();
        return EntregadorResult.De(entregador);
    }
}

/// <summary>S44: desativar (ou reativar) o entregador. Inativo não entra em viagem.</summary>
public sealed class AlterarAtivoEntregadorUseCase(IEntregadorRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<EntregadorResult> ExecuteAsync(Guid empresaId, Guid id, bool ativo, CancellationToken ct = default)
    {
        var entregador = await repository.ObterAsync(empresaId, id, ct) ?? throw new EntregadorNaoEncontradoException(id);
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (ativo) entregador.Reativar(agora); else entregador.Desativar(agora);
        await unitOfWork.CommitAsync();
        return EntregadorResult.De(entregador);
    }
}

public sealed class ListarEntregadoresUseCase(IEntregadorRepository repository)
{
    public async Task<IReadOnlyList<EntregadorResult>> ExecuteAsync(Guid empresaId, bool incluirInativos, CancellationToken ct = default) =>
        (await repository.ListarAsync(empresaId, incluirInativos, ct)).Select(EntregadorResult.De).ToList();
}

public sealed class ObterEntregadorUseCase(IEntregadorRepository repository)
{
    public async Task<EntregadorResult> ExecuteAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        EntregadorResult.De(await repository.ObterAsync(empresaId, id, ct) ?? throw new EntregadorNaoEncontradoException(id));
}

/// <summary>Traduz regra de domínio violada para o 400 dos use cases.</summary>
internal static class Regra
{
    public static T Validar<T>(Func<T> acao)
    {
        try { return acao(); }
        catch (RegraDeDominioVioladaException ex) { throw new UseCaseValidationException(ex.Message); }
    }

    public static void Validar(Action acao)
    {
        try { acao(); }
        catch (RegraDeDominioVioladaException ex) { throw new UseCaseValidationException(ex.Message); }
    }
}
