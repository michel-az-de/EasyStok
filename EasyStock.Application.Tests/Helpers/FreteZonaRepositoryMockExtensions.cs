using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Tests.Helpers;

/// <summary>
/// O checkout cota o frete pelo <c>CalcularFreteUseCase</c> (#1291), que pede a zona ao
/// <see cref="IFreteZonaRepository.BuscarZonaPorCepAsync"/>. Este helper faz o substitute responder como o
/// repositório real: a primeira zona de <see cref="IFreteZonaRepository.GetAtivasDoStorefrontOrdenadasAsync"/>
/// que cobre o CEP ou o bairro. Quem troca as zonas ativas no teste continua mudando a resposta.
/// </summary>
internal static class FreteZonaRepositoryMockExtensions
{
    public static IFreteZonaRepository BuscarZonaPelasAtivas(this IFreteZonaRepository repo)
    {
        repo.BuscarZonaPorCepAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var cep = ci.ArgAt<string>(1);
                var bairro = ci.ArgAt<string>(2);
                var zonas = await repo.GetAtivasDoStorefrontOrdenadasAsync(ci.ArgAt<Guid>(0), CancellationToken.None)
                            ?? new List<FreteZona>();
                return zonas.FirstOrDefault(z =>
                    z.CobreCep(cep) || (!string.IsNullOrEmpty(bairro) && z.CobreBairro(bairro)));
            });
        return repo;
    }
}
