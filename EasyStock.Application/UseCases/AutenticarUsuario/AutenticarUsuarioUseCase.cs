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

            if (!BCrypt.Net.BCrypt.Verify(command.Senha, usuario.SenhaHash))
                throw new CredenciaisInvalidasException();

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
