using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Caderno;

public sealed record TrechoCadernoResult(
    Guid Id, string Codigo, string Titulo, string Texto, string PalavrasChave, bool Nucleo, bool Arquivado, DateTime AlteradoEm)
{
    internal static TrechoCadernoResult De(TrechoCaderno t) =>
        new(t.Id, t.Codigo, t.Titulo, t.Texto, t.PalavrasChave, t.Nucleo, t.Arquivado, t.AlteradoEm);
}

public sealed record SalvarTrechoCadernoCommand(Guid EmpresaId, string Titulo, string Texto, string? PalavrasChave, bool Nucleo);

public sealed class TrechoCadernoNaoEncontradoException(Guid id) : Exception($"Trecho do caderno {id} não encontrado.");

/// <summary>
/// S54: cadastro do caderno da loja pelo console. O núcleo vai em toda chamada ao modelo, por isso a
/// soma do texto dos trechos ativos do núcleo tem teto (<see cref="TrechoCaderno.NucleoTamanhoMaximoTotal"/>).
/// </summary>
public sealed class CadernoUseCases(ICadernoRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<IReadOnlyList<TrechoCadernoResult>> ListarAsync(Guid empresaId, bool incluirArquivados, CancellationToken ct = default) =>
        (await repository.ListarAsync(empresaId, incluirArquivados, ct)).Select(TrechoCadernoResult.De).ToList();

    public async Task<TrechoCadernoResult> CriarAsync(SalvarTrechoCadernoCommand command, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var trecho = Validar(() => TrechoCaderno.Criar(
            command.EmpresaId, command.Titulo, command.Texto, command.PalavrasChave, command.Nucleo, agora));
        await GarantirTetoDoNucleoAsync(trecho, ct);

        await repository.AddAsync(trecho, ct);
        await unitOfWork.CommitAsync();
        return TrechoCadernoResult.De(trecho);
    }

    public async Task<TrechoCadernoResult> EditarAsync(Guid id, SalvarTrechoCadernoCommand command, CancellationToken ct = default)
    {
        var trecho = await ObterAsync(command.EmpresaId, id, ct);
        var agora = relogio.GetUtcNow().UtcDateTime;
        Validar(() => { trecho.Editar(command.Titulo, command.Texto, command.PalavrasChave, command.Nucleo, agora); return trecho; });
        await GarantirTetoDoNucleoAsync(trecho, ct);

        await unitOfWork.CommitAsync();
        return TrechoCadernoResult.De(trecho);
    }

    public async Task<TrechoCadernoResult> ArquivarAsync(Guid empresaId, Guid id, bool arquivado, CancellationToken ct = default)
    {
        var trecho = await ObterAsync(empresaId, id, ct);
        trecho.Arquivar(arquivado, relogio.GetUtcNow().UtcDateTime);
        await GarantirTetoDoNucleoAsync(trecho, ct);

        await unitOfWork.CommitAsync();
        return TrechoCadernoResult.De(trecho);
    }

    private async Task<TrechoCaderno> ObterAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        await repository.ObterAsync(empresaId, id, ct) ?? throw new TrechoCadernoNaoEncontradoException(id);

    /// <summary>Soma o núcleo ativo já gravado, sem o próprio trecho, mais o trecho como ficou.</summary>
    private async Task GarantirTetoDoNucleoAsync(TrechoCaderno trecho, CancellationToken ct)
    {
        if (!trecho.Nucleo || trecho.Arquivado) return;

        var outros = (await repository.ListarAsync(trecho.EmpresaId, incluirArquivados: false, ct))
            .Where(t => t.Nucleo && t.Id != trecho.Id)
            .Sum(t => t.Texto.Length);
        if (outros + trecho.Texto.Length > TrechoCaderno.NucleoTamanhoMaximoTotal)
            throw new UseCaseValidationException(
                $"O núcleo do caderno passaria de {TrechoCaderno.NucleoTamanhoMaximoTotal} caracteres. " +
                "Tire este trecho do núcleo: ele continua disponível para o agente pelo índice.");
    }

    private static T Validar<T>(Func<T> acao)
    {
        try
        {
            return acao();
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}
