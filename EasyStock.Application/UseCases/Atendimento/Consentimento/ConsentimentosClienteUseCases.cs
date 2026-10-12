using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Consentimento;

/// <summary>Uma linha por canal e finalidade; <see cref="Situacao"/> nula = nunca registrado.</summary>
public sealed record ConsentimentoCanalResult(
    CanalConversa Canal,
    FinalidadeContato Finalidade,
    SituacaoConsentimento? Situacao,
    string? Origem,
    DateTime? AtualizadoEm,
    bool PodeEnviar);

public sealed class ClienteNaoEncontradoParaConsentimentoException(Guid clienteId)
    : Exception($"Cliente {clienteId} não encontrado nesta empresa.");

/// <summary>S38: o console vê, por canal, o que pode e o que não pode ser enviado ao cliente.</summary>
public sealed class ListarConsentimentosClienteUseCase(
    IClienteRepository clientes, IConsentimentoContatoRepository repository)
{
    public async Task<IReadOnlyList<ConsentimentoCanalResult>> ExecuteAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default)
    {
        _ = await clientes.GetByIdAsync(empresaId, clienteId) ?? throw new ClienteNaoEncontradoParaConsentimentoException(clienteId);
        return Montar(await repository.ListarDoClienteAsync(empresaId, clienteId, ct));
    }

    internal static IReadOnlyList<ConsentimentoCanalResult> Montar(IReadOnlyList<ConsentimentoContato> registrados) =>
        (from canal in Enum.GetValues<CanalConversa>()
         from finalidade in Enum.GetValues<FinalidadeContato>()
         let atual = registrados.FirstOrDefault(c => c.Canal == canal && c.Finalidade == finalidade)
         select new ConsentimentoCanalResult(canal, finalidade, atual?.Situacao, atual?.Origem, atual?.AtualizadoEm,
             PoliticaConsentimento.PodeEnviar(registrados, canal, finalidade)))
        .ToList();
}

public sealed record ConsentimentoItem(CanalConversa Canal, FinalidadeContato Finalidade, SituacaoConsentimento Situacao);

public sealed record DefinirConsentimentosClienteCommand(
    Guid EmpresaId, Guid ClienteId, Guid UsuarioId, IReadOnlyList<ConsentimentoItem> Itens);

/// <summary>S38: a dona registra o opt-in (ou a revogação) que o cliente deu fora do sistema.</summary>
public sealed class DefinirConsentimentosClienteUseCase(
    IClienteRepository clientes, IConsentimentoContatoRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<IReadOnlyList<ConsentimentoCanalResult>> ExecuteAsync(DefinirConsentimentosClienteCommand command, CancellationToken ct = default)
    {
        _ = await clientes.GetByIdAsync(command.EmpresaId, command.ClienteId)
            ?? throw new ClienteNaoEncontradoParaConsentimentoException(command.ClienteId);

        var agora = relogio.GetUtcNow().UtcDateTime;
        var origem = $"console:{command.UsuarioId:N}";
        var registrados = (await repository.ListarDoClienteAsync(command.EmpresaId, command.ClienteId, ct)).ToList();

        try
        {
            foreach (var item in command.Itens)
            {
                var atual = registrados.FirstOrDefault(c => c.Canal == item.Canal && c.Finalidade == item.Finalidade);
                if (atual is not null)
                {
                    atual.Alterar(item.Situacao, origem, agora);
                    continue;
                }
                var novo = ConsentimentoContato.Registrar(command.EmpresaId, command.ClienteId, item.Canal, item.Finalidade, item.Situacao, origem, agora);
                await repository.AddAsync(novo, ct);
                registrados.Add(novo);
            }
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await unitOfWork.CommitAsync();
        return ListarConsentimentosClienteUseCase.Montar(registrados);
    }
}
