using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Automacoes;

public sealed record RegraAutomaticaResult(GatilhoAutomacao Gatilho, bool Ligada, string? Texto, DateTime? AlteradaEm)
{
    internal static RegraAutomaticaResult De(RegraAutomatica r) => new(r.Gatilho, r.Ligada, r.Texto, r.AlteradaEm);
}

public sealed record SalvarAutomacaoCommand(Guid EmpresaId, GatilhoAutomacao Gatilho, string Texto, bool Ligada);

/// <summary>
/// S42: cadastro das mensagens automáticas. A lista traz todos os gatilhos do enum; o que ainda não tem
/// regra aparece desligado e sem texto. Salvar cria ou altera a regra do gatilho.
/// </summary>
public sealed class AutomacoesUseCases(IRegraAutomaticaRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<IReadOnlyList<RegraAutomaticaResult>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        var porGatilho = (await repository.ListarAsync(empresaId, ct)).ToDictionary(r => r.Gatilho);
        return Enum.GetValues<GatilhoAutomacao>()
            .Select(g => porGatilho.TryGetValue(g, out var r) ? RegraAutomaticaResult.De(r) : new RegraAutomaticaResult(g, false, null, null))
            .ToList();
    }

    public async Task<RegraAutomaticaResult> SalvarAsync(SalvarAutomacaoCommand command, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        try
        {
            var regra = await repository.ObterPorGatilhoAsync(command.EmpresaId, command.Gatilho, ct);
            if (regra is null)
            {
                regra = RegraAutomatica.Criar(command.EmpresaId, command.Gatilho, command.Texto, command.Ligada, agora);
                await repository.AddAsync(regra, ct);
            }
            else
            {
                regra.Alterar(command.Texto, command.Ligada, agora);
            }

            await unitOfWork.CommitAsync();
            return RegraAutomaticaResult.De(regra);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}
