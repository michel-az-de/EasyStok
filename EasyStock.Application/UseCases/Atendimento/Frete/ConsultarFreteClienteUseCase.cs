using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Frete;

// Resolve somente o cadastro e a vitrine do tenant autenticado. A conta continua no checkout.
public sealed class ConsultarFreteClienteUseCase(
    IClienteRepository clientes,
    IStorefrontRepository vitrines,
    CalcularFreteUseCase calcularFrete)
{
    public async Task<FreteCalculadoDto?> ExecuteAsync(Guid empresaId, Guid clienteId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var cliente = await clientes.GetByIdWithDetailsAsync(empresaId, clienteId);
        if (cliente is null || cliente.EmpresaId != empresaId) return null;
        var endereco = cliente.Enderecos.FirstOrDefault(e => e.Padrao)
            ?? (cliente.Enderecos.Count == 1 ? cliente.Enderecos.Single() : null);
        if (endereco is null || string.IsNullOrWhiteSpace(endereco.Cep))
            throw new UseCaseValidationException("Cadastre um endereço com CEP e defina o endereço padrão para calcular o frete.");
        var vitrine = await vitrines.GetByEmpresaAsync(empresaId, ct);
        if (vitrine is null || !vitrine.Ativo || vitrine.EmpresaId != empresaId)
            throw new StorefrontNaoEncontradoException();
        return await calcularFrete.ExecuteAsync(new CalcularFreteInput(vitrine.Slug, endereco.Cep, endereco.Numero), ct);
    }
}
