using EasyStock.Application.Services.Auth;

namespace EasyStock.Application.UseCases.DesativarUsuario
{
    public sealed record DesativarUsuarioCommand(Guid UsuarioId, Guid EmpresaId);

    public class DesativarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        RevogadorSessoes revogadorSessoes,
        IUnitOfWork unitOfWork,
        ILogger<DesativarUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(DesativarUsuarioCommand command)
        {
            logger.LogInformation("Desativando usuario {UsuarioId} da empresa {EmpresaId}", command.UsuarioId, command.EmpresaId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            var linkEmpresa = await usuarioEmpresaRepository.GetByUsuarioEEmpresaAsync(command.UsuarioId, command.EmpresaId);
            if (linkEmpresa is null)
                throw new UseCaseValidationException("Associacao do usuario com a empresa nao encontrada.");

            linkEmpresa.Ativo = false;
            await usuarioEmpresaRepository.UpdateAsync(linkEmpresa);

            // #1352: desativar tira o vínculo de uma empresa, mas o corte é do usuário todo. Quem está em duas
            // empresas entra de novo e fica só com a que continua ativa.
            await revogadorSessoes.RevogarAsync(usuario);
            await unitOfWork.CommitAsync();
        }
    }
}
