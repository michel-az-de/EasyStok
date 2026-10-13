using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.DesativarUsuario
{
    public sealed record DesativarUsuarioCommand(Guid UsuarioId, Guid EmpresaId);

    public class DesativarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        RevogadorSessoes revogadorSessoes,
        ICurrentUserAccessor currentUser,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ILogger<DesativarUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(DesativarUsuarioCommand command)
        {
            logger.LogInformation("Desativando usuario {UsuarioId} da empresa {EmpresaId}", command.UsuarioId, command.EmpresaId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            // #1753: só administrador chega aqui, então barrar a própria desativação garante que sempre sobra
            // um administrador ativo na empresa. O suporte (SuperAdmin) não tem vínculo a perder.
            if (currentUser.Nivel != NivelAcesso.SuperAdmin && command.UsuarioId == currentUser.UsuarioId)
                throw new UseCaseValidationException(
                    "USUARIO_PROPRIO", "Você não pode desativar o seu próprio acesso. Peça a outro administrador.");

            var linkEmpresa = await usuarioEmpresaRepository.GetByUsuarioEEmpresaAsync(command.UsuarioId, command.EmpresaId);
            if (linkEmpresa is null)
                throw new UseCaseValidationException("Associacao do usuario com a empresa nao encontrada.");

            // Repetir o pedido não muda nada nem escreve outra linha de auditoria.
            if (!linkEmpresa.Ativo) return;

            linkEmpresa.Ativo = false;
            await usuarioEmpresaRepository.UpdateAsync(linkEmpresa);

            // #1352: desativar tira o vínculo de uma empresa, mas o corte é do usuário todo. Quem está em duas
            // empresas entra de novo e fica só com a que continua ativa.
            await revogadorSessoes.RevogarAsync(usuario);

            var autor = await RastroDeUsuarios.AutorAsync(usuarioRepository, currentUser);
            await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(
                usuario.Id, RastroDeUsuarios.Desativado, $"Acesso desativado por {autor}", currentUser));
            await unitOfWork.CommitAsync();
        }
    }
}
