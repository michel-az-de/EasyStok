using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AtribuirPerfilUsuario
{
    public sealed record AtribuirPerfilUsuarioCommand(Guid UsuarioId, Guid EmpresaId, Guid PerfilId, Guid? LojaId);

    public class AtribuirPerfilUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IUnitOfWork unitOfWork,
        ILogger<AtribuirPerfilUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(AtribuirPerfilUsuarioCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

            logger.LogInformation("Atribuindo perfil {PerfilId} ao usuario {UsuarioId}", command.PerfilId, command.UsuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            var perfilExistente = await usuarioPerfilRepository.GetByUsuarioEmpresaEPerfilAsync(command.UsuarioId, command.EmpresaId, command.PerfilId);

            if (perfilExistente is not null)
            {
                perfilExistente.LojaId = command.LojaId;
                perfilExistente.AtribuidoEm = DateTime.UtcNow;
                await usuarioPerfilRepository.UpdateAsync(perfilExistente);
            }
            else
            {
                var usuarioPerfil = new UsuarioPerfil
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = command.UsuarioId,
                    EmpresaId = command.EmpresaId,
                    PerfilId = command.PerfilId,
                    LojaId = command.LojaId,
                    AtribuidoEm = DateTime.UtcNow
                };

                await usuarioPerfilRepository.AddAsync(usuarioPerfil);
            }

            await unitOfWork.CommitAsync();
        }
    }
}
