using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ObterUsuarioAtual;

public sealed class ObterUsuarioAtualUseCase(
    IUsuarioRepository usuarioRepository,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<ObterUsuarioAtualUseCase> logger) : IUseCase<ObterUsuarioAtualCommand, ObterUsuarioAtualResult>
{
    public async Task<ObterUsuarioAtualResult> ExecuteAsync(ObterUsuarioAtualCommand command)
    {
        logger.LogInformation("Obtendo dados do usuario atual");

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

        return new ObterUsuarioAtualResult(
            usuario.Id,
            usuario.Nome,
            usuario.Email,
            usuario.Ativo,
            usuario.CriadoEm);
    }
}