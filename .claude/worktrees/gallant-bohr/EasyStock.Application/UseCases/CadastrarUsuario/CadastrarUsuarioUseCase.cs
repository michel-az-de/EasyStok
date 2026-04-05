using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.CadastrarUsuario;

public sealed class CadastrarUsuarioUseCase(
    IUsuarioRepository usuarioRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ILogger<CadastrarUsuarioUseCase> logger) : IUseCase<CadastrarUsuarioCommand, CadastrarUsuarioResult>
{
    public async Task<CadastrarUsuarioResult> ExecuteAsync(CadastrarUsuarioCommand command)
    {
        logger.LogInformation("Iniciando cadastro de usuario com email {Email}", command.Email);

        // Verificar se email ja existe
        var usuarioExistente = await usuarioRepository.GetByEmailAsync(command.Email);
        if (usuarioExistente != null)
        {
            logger.LogWarning("Tentativa de cadastro com email ja existente: {Email}", command.Email);
            throw new RegraDeDominioVioladaException("Email ja cadastrado.");
        }

        // Criar usuario
        var senhaHash = BCrypt.Net.BCrypt.HashPassword(command.Senha);
        var usuario = Usuario.Criar(command.Nome, command.Email, senhaHash);

        await usuarioRepository.AddAsync(usuario);

        // Auditar
        var auditLog = AuditLog.Criar(
            usuario.Id,
            "register",
            true,
            "Usuario registrado com sucesso",
            null,
            null);
        await auditLogRepository.AddAsync(auditLog);

        await unitOfWork.CommitAsync();

        logger.LogInformation("Usuario cadastrado com sucesso: {UsuarioId}", usuario.Id);
        return new CadastrarUsuarioResult(usuario.Id);
    }
}}
