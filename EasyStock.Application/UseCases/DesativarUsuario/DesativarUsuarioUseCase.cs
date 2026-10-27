using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.DesativarUsuario
{
    public sealed record DesativarUsuarioCommand(Guid UsuarioId, Guid EmpresaId);

    public class DesativarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        RevogadorSessoes revogadorSessoes,
        ConvitesDeAcesso convites,
        HierarquiaDeUsuarios hierarquia,
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

            // Guarda de tenant antes da política: sem vínculo com a empresa, a mesma resposta de sempre.
            if (!usuario.Empresas.Any(e => e.EmpresaId == command.EmpresaId))
                throw new UseCaseValidationException("Associacao do usuario com a empresa nao encontrada.");

            // #2070: quem desativa quem é da política (a si mesmo nunca, a dona só pelo suporte, nunca a última dona), com
            // ator e alvo relidos depois da trava da hierarquia da empresa.
            await hierarquia.ExecutarAsync(
                new PedidoDeHierarquia(command.EmpresaId, usuario.Id, AcaoSobreUsuario.Desativar),
                async (contexto, _) =>
                {
                    var linkEmpresa = await usuarioEmpresaRepository.GetByUsuarioEEmpresaAsync(command.UsuarioId, command.EmpresaId)
                        ?? throw new UseCaseValidationException("Associacao do usuario com a empresa nao encontrada.");

                    // Repetir o pedido não muda nada nem escreve outra linha de auditoria.
                    if (!linkEmpresa.Ativo) return false;

                    linkEmpresa.Ativo = false;
                    await usuarioEmpresaRepository.UpdateAsync(linkEmpresa);

                    // #1352: desativar tira o vínculo de uma empresa, mas o corte é do usuário todo. Quem está em duas
                    // empresas entra de novo e fica só com a que continua ativa.
                    await revogadorSessoes.RevogarAsync(contexto.Alvo!);

                    // #1753: convite aberto cai junto. Sem isso a pessoa desativada ainda criava a senha pelo link e só o login recusava.
                    await convites.RevogarAbertosAsync(usuario.Id);

                    var autor = await RastroDeUsuarios.AutorAsync(usuarioRepository, currentUser);
                    await auditLogRepository.AddAsync(RastroDeUsuarios.Linha(
                        usuario.Id, RastroDeUsuarios.Desativado, $"Acesso desativado por {autor}", currentUser, command.EmpresaId));
                    await unitOfWork.CommitAsync();
                    return true;
                });
        }
    }
}
