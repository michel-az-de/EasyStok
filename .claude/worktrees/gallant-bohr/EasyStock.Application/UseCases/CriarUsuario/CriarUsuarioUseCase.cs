using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.CriarUsuario
{
    public sealed record CriarUsuarioCommand(
        Guid EmpresaId,
        string Nome,
        string Email,
        string Senha,
        Guid? PerfilId,
        Guid? LojaId);

    public sealed record CriarUsuarioResult(
        Guid UsuarioId,
        string Nome,
        string Email);

    public class CriarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IAssinaturaEmpresaRepository assinaturaRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IUnitOfWork unitOfWork,
        ILogger<CriarUsuarioUseCase> logger)
    {
        public async Task<CriarUsuarioResult> ExecuteAsync(CriarUsuarioCommand command)
        {
            logger.LogInformation("Criando usuario para empresa {EmpresaId}", command.EmpresaId);

            var emailExistente = await usuarioRepository.GetByEmailAsync(command.Email);
            if (emailExistente is not null)
                throw new UseCaseValidationException("Email ja cadastrado.");

            var assinatura = await assinaturaRepository.GetAtivaAsync(command.EmpresaId);
            var totalUsuarios = await usuarioRepository.CountByEmpresaAsync(command.EmpresaId);
            if (assinatura?.Plano is not null && !assinatura.Plano.UsuariosSaoIlimitados && totalUsuarios >= assinatura.Plano.LimiteUsuarios)
                throw new PlanoLimiteAtingidoException("usuarios");

            var agora = DateTime.UtcNow;
            var senhaHash = BCrypt.Net.BCrypt.HashPassword(command.Senha);
            var usuario = Usuario.Criar(command.Nome.Trim(), command.Email.Trim(), senhaHash);

            await usuarioRepository.AddAsync(usuario);

            var usuarioEmpresa = new UsuarioEmpresa
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                EmpresaId = command.EmpresaId,
                Ativo = true,
                CriadoEm = agora
            };

            await usuarioEmpresaRepository.AddAsync(usuarioEmpresa);

            if (command.PerfilId.HasValue)
            {
                var usuarioPerfil = new UsuarioPerfil
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = usuario.Id,
                    EmpresaId = command.EmpresaId,
                    PerfilId = command.PerfilId.Value,
                    LojaId = command.LojaId,
                    AtribuidoEm = agora
                };

                await usuarioPerfilRepository.AddAsync(usuarioPerfil);
            }

            await unitOfWork.CommitAsync();

            logger.LogInformation("Usuario criado: {UsuarioId}", usuario.Id);

            return new CriarUsuarioResult(usuario.Id, usuario.Nome, usuario.Email);
        }
    }
}
