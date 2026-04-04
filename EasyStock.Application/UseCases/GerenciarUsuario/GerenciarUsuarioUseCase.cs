using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.GerenciarUsuario
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

    public sealed record AtualizarUsuarioCommand(
        Guid UsuarioId,
        string Nome,
        string? Email);

    public sealed record AlterarSenhaCommand(
        Guid UsuarioId,
        string SenhaAtual,
        string NovaSenha);

    public class GerenciarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IAssinaturaEmpresaRepository assinaturaRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IUnitOfWork unitOfWork,
        ILogger<GerenciarUsuarioUseCase> logger)
    {
        public async Task<CriarUsuarioResult> CriarAsync(CriarUsuarioCommand command)
        {
            logger.LogInformation("Criando usuario para empresa {EmpresaId}", command.EmpresaId);

            var emailExistente = await usuarioRepository.GetByEmailAsync(command.Email);
            if (emailExistente is not null)
                throw new UseCaseValidationException("Email ja cadastrado.");

            var assinatura = await assinaturaRepository.GetAtivaAsync(command.EmpresaId);
            var totalUsuarios = await usuarioRepository.CountByEmpresaAsync(command.EmpresaId);
            if (assinatura?.Plano is not null && assinatura.Plano.LimiteUsuarios != -1 && totalUsuarios >= assinatura.Plano.LimiteUsuarios)
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

        public async Task AtualizarAsync(AtualizarUsuarioCommand command)
        {
            logger.LogInformation("Atualizando usuario {UsuarioId}", command.UsuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            if (!string.IsNullOrWhiteSpace(command.Email) && command.Email != usuario.Email)
            {
                var emailExistente = await usuarioRepository.GetByEmailAsync(command.Email);
                if (emailExistente is not null)
                    throw new UseCaseValidationException("Email ja cadastrado.");
                usuario.Email = command.Email.Trim();
            }

            usuario.Nome = command.Nome.Trim();
            usuario.AlteradoEm = DateTime.UtcNow;

            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();
        }

        public async Task AlterarSenhaAsync(AlterarSenhaCommand command)
        {
            logger.LogInformation("Alterando senha do usuario {UsuarioId}", command.UsuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            if (!BCrypt.Net.BCrypt.Verify(command.SenhaAtual, usuario.SenhaHash))
                throw new UseCaseValidationException("Senha atual incorreta.");

            usuario.SenhaHash = BCrypt.Net.BCrypt.HashPassword(command.NovaSenha);
            usuario.AlteradoEm = DateTime.UtcNow;

            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();
        }

        public async Task DesativarAsync(Guid usuarioId, Guid empresaId)
        {
            logger.LogInformation("Desativando usuario {UsuarioId} da empresa {EmpresaId}", usuarioId, empresaId);

            var usuario = await usuarioRepository.GetByIdAsync(usuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            var linkEmpresa = usuario.Empresas?.FirstOrDefault(e => e.EmpresaId == empresaId);
            if (linkEmpresa is null)
                throw new UseCaseValidationException("Associacao do usuario com a empresa nao encontrada.");

            linkEmpresa.Ativo = false;

            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();
        }

        public async Task<(IEnumerable<Usuario> Usuarios, int Total)> ListarAsync(Guid empresaId, int page, int pageSize)
        {
            return await usuarioRepository.GetByEmpresaAsync(empresaId, page, pageSize);
        }

        public async Task AtribuirPerfilAsync(Guid usuarioId, Guid empresaId, Guid perfilId, Guid? lojaId)
        {
            logger.LogInformation("Atribuindo perfil {PerfilId} ao usuario {UsuarioId}", perfilId, usuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(usuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            var perfilExistente = usuario.Perfis?.FirstOrDefault(
                p => p.EmpresaId == empresaId && p.PerfilId == perfilId);

            if (perfilExistente is not null)
            {
                perfilExistente.LojaId = lojaId;
                perfilExistente.AtribuidoEm = DateTime.UtcNow;
            }
            else
            {
                var usuarioPerfil = new UsuarioPerfil
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = usuarioId,
                    EmpresaId = empresaId,
                    PerfilId = perfilId,
                    LojaId = lojaId,
                    AtribuidoEm = DateTime.UtcNow
                };

                await usuarioPerfilRepository.AddAsync(usuarioPerfil);
            }

            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();
        }
    }
}
