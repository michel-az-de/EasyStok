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
/// regra aparece como a <see cref="RegraAutomatica.Padrao"/> (o lembrete do link, #1610) ou desligado e sem texto.
/// Salvar cria ou altera a regra do gatilho.
/// </summary>
public sealed class AutomacoesUseCases(IRegraAutomaticaRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<IReadOnlyList<RegraAutomaticaResult>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        var porGatilho = (await repository.ListarAsync(empresaId, ct)).ToDictionary(r => r.Gatilho);
        var agora = relogio.GetUtcNow().UtcDateTime;
        return Enum.GetValues<GatilhoAutomacao>()
            .Select(g => porGatilho.TryGetValue(g, out var r) ? RegraAutomaticaResult.De(r)
                : RegraAutomatica.Padrao(empresaId, g, agora) is { } padrao ? RegraAutomaticaResult.De(padrao) with { AlteradaEm = null }
                : new RegraAutomaticaResult(g, false, null, null))
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
