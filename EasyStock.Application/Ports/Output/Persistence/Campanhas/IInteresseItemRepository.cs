using EasyStock.Domain.Entities.Campanhas;

namespace EasyStock.Application.Ports.Output.Persistence.Campanhas;

/// <summary>Interesses em item indisponível (S31). <c>EmpresaId</c> no WHERE (ADR-0010).</summary>
public interface IInteresseItemRepository
{
    Task AddAsync(InteresseItem interesse, CancellationToken ct = default);

    /// <summary>Quantos interesses abertos (<c>AtendidoEm</c> nulo) o item tem.</summary>
    Task<int> ContarAbertosDoItemAsync(Guid empresaId, Guid cardapioItemId, CancellationToken ct = default);

    /// <summary>Interesses abertos no item com o cliente, do mais recente para o mais antigo.</summary>
    Task<IReadOnlyList<InteresseAbertoCliente>> ListarAbertosDoItemAsync(Guid empresaId, Guid cardapioItemId, CancellationToken ct = default);

    /// <summary>Interesse rastreado, para a dona fechá-lo; nulo quando é de outra empresa ou não existe.</summary>
    Task<InteresseItem?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>Todos os interesses do cliente (abertos e atendidos), do mais recente para o mais antigo.</summary>
    Task<IReadOnlyList<InteresseItem>> ListarDoClienteAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default);

    /// <summary>Interesses abertos do cliente nos itens do cardápio informados, rastreados para o fechamento.</summary>
    Task<IReadOnlyList<InteresseItem>> ListarAbertosDoClienteNosItensAsync(
        Guid empresaId, Guid clienteId, IReadOnlyCollection<Guid> cardapioItemIds, CancellationToken ct = default);
}

/// <summary>Linha da sugestão de quem avisar quando o item volta (S31).</summary>
public sealed record InteresseAbertoCliente(
    Guid InteresseId,
    Guid ClienteId,
    string ClienteNome,
    string? ClienteTelefone,
    string? Descricao,
    string Origem,
    DateTime RegistradoEm);
