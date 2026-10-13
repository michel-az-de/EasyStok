using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.ReativarUsuario
{
    public sealed record ReativarUsuarioCommand(Guid UsuarioId, Guid EmpresaId);

    /// <summary>#1753: desfaz a desativação. Antes só existia o caminho de ida, e um engano pedia acesso ao banco.</summary>
    public class ReativarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        IAssinaturaEmpresaRepository assinaturaRepository,
        ICurrentUserAccessor currentUser,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ILogger<ReativarUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(ReativarUsuarioCommand command)
        {
            logger.LogInformation("Reativando usuario {UsuarioId} na empresa {EmpresaId}", command.UsuarioId, command.EmpresaId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");
            // Mesma mensagem do não encontrado: não confirma a existência de usuário de outra empresa.
            var linkEmpresa = await usuarioEmpresaRepository.GetByUsuarioEEmpresaAsync(command.UsuarioId, command.EmpresaId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            if (linkEmpresa.Ativo) return;

            // Mesmo limite do convite (CriarUsuario): reativar não pode passar do que o plano permite.
            var assinatura = await assinaturaRepository.GetAtivaAsync(command.EmpresaId);
            var totalUsuarios = await usuarioRepository.CountByEmpresaAsync(command.EmpresaId);
            if (assinatura?.Plano is not null && !assinatura.Plano.UsuariosSaoIlimitados && totalUsuarios >= assinatura.Plano.LimiteUsuarios)
                throw new PlanoLimiteAtingidoException("usuarios");

            linkEmpresa.Ativo = true;
            await usuarioEmpresaRepository.UpdateAsync(linkEmpresa);

            var autor = await RastroDeUsuarios.AutorAsync(usuarioRepository, currentUser);
            await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(
                usuario.Id, RastroDeUsuarios.Reativado, $"Acesso reativado por {autor}", currentUser));
            await unitOfWork.CommitAsync();
        }
    }
}
