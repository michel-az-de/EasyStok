using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AutenticarUsuario
{
    public sealed record AutenticarUsuarioCommand(
        string Email,
        string Senha,
        Guid? EmpresaId);

    public sealed record AutenticarUsuarioResult(
        Guid UsuarioId,
        Guid? EmpresaId,
        string Nome,
        string Email,
        NivelAcesso Nivel,
        IReadOnlyCollection<Permissao> Permissoes);

    public class AutenticarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUnitOfWork unitOfWork,
        ILogger<AutenticarUsuarioUseCase> logger)
    {
        public async Task<AutenticarUsuarioResult> ExecuteAsync(AutenticarUsuarioCommand command)
        {
            logger.LogDebug("Tentativa de autenticacao para o email: {Email}", command.Email);

            var usuario = await usuarioRepository.GetByEmailAsync(command.Email);

            if (usuario is null || !usuario.Ativo)
                throw new CredenciaisInvalidasException();

            // Verificar lockout
            if (usuario.EstaBloqueado())
            {
                logger.LogWarning("Tentativa de login para usuario bloqueado: {Email}", command.Email);
                throw new CredenciaisInvalidasException("Conta bloqueada temporariamente.");
            }

            if (!BCrypt.Net.BCrypt.Verify(command.Senha, usuario.SenhaHash))
            {
                usuario.IncrementarTentativasFalha();
                if (usuario.FailedLoginAttempts >= 5)
                {
                    usuario.BloquearPorTentativas(15);
                    logger.LogWarning("Usuario bloqueado apos 5 tentativas falhidas: {Email}", command.Email);
                }
                await usuarioRepository.UpdateAsync(usuario);
                await unitOfWork.CommitAsync();
                throw new CredenciaisInvalidasException();
            }

            // Resetar tentativas em login bem-sucedido
            usuario.ResetarTentativasFalha();

            if (command.EmpresaId.HasValue)
            {
                var linkEmpresa = usuario.Empresas?.FirstOrDefault(
                    e => e.EmpresaId == command.EmpresaId.Value && e.Ativo);

                if (linkEmpresa is null)
                    throw new CredenciaisInvalidasException();
            }

            var nivel = NivelAcesso.Visualizador;
            IReadOnlyCollection<Permissao> permissoes = [];

            if (command.EmpresaId.HasValue && usuario.Perfis is not null)
            {
                var perfilDaEmpresa = usuario.Perfis
                    .Where(p => p.EmpresaId == command.EmpresaId.Value)
                    .OrderBy(p => p.Perfil != null ? (int)p.Perfil.Nivel : int.MaxValue)
                    .FirstOrDefault();

                if (perfilDaEmpresa?.Perfil is not null)
                {
                    nivel = perfilDaEmpresa.Perfil.Nivel;
                    permissoes = perfilDaEmpresa.Perfil.Permissoes?
                        .Select(p => p.Permissao)
                        .Distinct()
                        .ToArray() ?? [];
                }
            }

            usuario.AtualizarUltimoAcesso();
            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();

            logger.LogInformation("Autenticacao bem-sucedida para o usuario: {UsuarioId}", usuario.Id);

            return new AutenticarUsuarioResult(
                UsuarioId: usuario.Id,
                EmpresaId: command.EmpresaId,
                Nome: usuario.Nome,
                Email: usuario.Email,
                Nivel: nivel,
                Permissoes: permissoes);
        }
    }
}
