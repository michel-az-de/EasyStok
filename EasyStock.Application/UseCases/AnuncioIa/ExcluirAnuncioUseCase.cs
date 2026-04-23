using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.AnuncioIa
{
    public sealed record ExcluirAnuncioCommand(Guid EmpresaId, Guid AnuncioId);

    public class ExcluirAnuncioUseCase(
        IAnuncioIaRepository anuncioIaRepository,
        IUnitOfWork unitOfWork)
    {
        public async Task ExecuteAsync(ExcluirAnuncioCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

            var anuncio = await anuncioIaRepository.GetByIdAsync(command.EmpresaId, command.AnuncioId)
                ?? throw new UseCaseValidationException("Anuncio nao encontrado.");

            await anuncioIaRepository.RemoveAsync(anuncio);
            await unitOfWork.CommitAsync();
        }
    }
}
