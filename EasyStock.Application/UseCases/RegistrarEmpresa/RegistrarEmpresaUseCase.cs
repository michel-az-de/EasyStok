using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.RegistrarEmpresa
{
    public sealed record RegistrarEmpresaCommand(
        string NomeEmpresa,
        string? Documento,
        string NomeAdmin,
        string EmailAdmin,
        string SenhaAdmin);

    public sealed record RegistrarEmpresaResult(
        Guid EmpresaId,
        Guid UsuarioId,
        string NomeEmpresa,
        string NomeAdmin,
        string Email);

    public class RegistrarEmpresaUseCase(
        IUsuarioRepository usuarioRepository,
        IPlanoRepository planoRepository,
        IPerfilRepository perfilRepository,
        IAssinaturaEmpresaRepository assinaturaRepository,
        IEmpresaRepository empresaRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IUnitOfWork unitOfWork,
        ILogger<RegistrarEmpresaUseCase> logger)
    {
        public async Task<RegistrarEmpresaResult> ExecuteAsync(RegistrarEmpresaCommand command)
        {
            logger.LogInformation("Registrando nova empresa: {NomeEmpresa}", command.NomeEmpresa);

            var emailExistente = await usuarioRepository.GetByEmailAsync(command.EmailAdmin);
            if (emailExistente is not null)
                throw new UseCaseValidationException("Email ja cadastrado.");

            var planos = await planoRepository.GetAtivosAsync();
            var planoLista = planos.ToList();

            var planoStarter = planoLista.FirstOrDefault(p => p.Nome == "Starter")
                ?? planoLista.FirstOrDefault()
                ?? throw new UseCaseValidationException("Nenhum plano ativo encontrado.");

            var agora = DateTime.UtcNow;

            var empresa = Empresa.Criar(command.NomeEmpresa, command.Documento);

            await empresaRepository.AddAsync(empresa);

            var assinatura = new AssinaturaEmpresa
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresa.Id,
                PlanoId = planoStarter.Id,
                DataInicio = agora,
                Status = StatusAssinatura.Ativa,
                CriadoEm = agora,
                AlteradoEm = agora
            };

            assinatura.AtivarTrial(14);
            await assinaturaRepository.AddAsync(assinatura);

            var senhaHash = BCrypt.Net.BCrypt.HashPassword(command.SenhaAdmin);
            var usuario = Usuario.Criar(command.NomeAdmin.Trim(), command.EmailAdmin.Trim(), senhaHash);

            await usuarioRepository.AddAsync(usuario);

            var usuarioEmpresa = new UsuarioEmpresa
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                EmpresaId = empresa.Id,
                Ativo = true,
                CriadoEm = agora
            };

            await usuarioEmpresaRepository.AddAsync(usuarioEmpresa);

            var perfispadrao = await perfilRepository.GetPadroesAsync();
            var perfilAdmin = perfispadrao.FirstOrDefault(p => p.Nome == "Admin")
                ?? throw new UseCaseValidationException("Perfil 'Admin' padrao nao encontrado. Configure os planos e perfis antes de registrar empresas.");

            var usuarioPerfil = new UsuarioPerfil
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                EmpresaId = empresa.Id,
                PerfilId = perfilAdmin.Id,
                AtribuidoEm = agora
            };

            await usuarioPerfilRepository.AddAsync(usuarioPerfil);

            await unitOfWork.CommitAsync();

            logger.LogInformation("Empresa registrada com sucesso. EmpresaId: {EmpresaId}, UsuarioId: {UsuarioId}", empresa.Id, usuario.Id);

            return new RegistrarEmpresaResult(
                EmpresaId: empresa.Id,
                UsuarioId: usuario.Id,
                NomeEmpresa: empresa.Nome,
                NomeAdmin: usuario.Nome,
                Email: usuario.Email);
        }
    }
}
