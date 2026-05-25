using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AlterarSenhaUsuario
{
    public sealed record AlterarSenhaCommand(
        Guid UsuarioId,
        string SenhaAtual,
        string NovaSenha);

    public class AlterarSenhaUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ILogger<AlterarSenhaUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(AlterarSenhaCommand command)
        {
            logger.LogInformation("Alterando senha do usuario {UsuarioId}", command.UsuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            if (!passwordHasher.Verify(command.SenhaAtual, usuario.SenhaHash))
                throw new UseCaseValidationException("Senha atual incorreta.");

            usuario.SenhaHash = passwordHasher.Hash(command.NovaSenha);
            usuario.AlteradoEm = DateTime.UtcNow;

            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();
        }
    }
}
