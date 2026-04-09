using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AtualizarUsuarioAtual;

public sealed class AtualizarUsuarioAtualUseCase(
    IUsuarioRepository usuarioRepository,
    ICurrentUserAccessor currentUserAccessor,
    IUnitOfWork unitOfWork,
    ILogger<AtualizarUsuarioAtualUseCase> logger) : IUseCase<AtualizarUsuarioAtualCommand, AtualizarUsuarioAtualResult>
{
    public async Task<AtualizarUsuarioAtualResult> ExecuteAsync(AtualizarUsuarioAtualCommand command)
    {
        logger.LogInformation("Atualizando dados do usuario atual");

        var usuarioId = currentUserAccessor.UsuarioId;
        if (usuarioId == Guid.Empty)
        {
            throw new UsuarioNaoAutorizadoException("Usuario nao autenticado.");
        }

        var usuario = await usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario == null)
        {
            throw new RegraDeDominioVioladaException("Usuario nao encontrado.");
        }

        if (!string.Equals(usuario.Email, command.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existente = await usuarioRepository.GetByEmailAsync(command.Email);
            if (existente != null && existente.Id != usuario.Id)
            {
                throw new RegraDeDominioVioladaException("Email ja cadastrado.");
            }
        }

        usuario.Nome = command.Nome;
        usuario.Email = command.Email;
        usuario.AlteradoEm = DateTime.UtcNow;

        await usuarioRepository.UpdateAsync(usuario);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Usuario {UsuarioId} atualizado", usuario.Id);
        return new AtualizarUsuarioAtualResult(usuario.Id, usuario.Nome, usuario.Email);
    }
}