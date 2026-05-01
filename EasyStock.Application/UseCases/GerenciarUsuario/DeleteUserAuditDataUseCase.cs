using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.GerenciarUsuario
{
    public sealed record DeleteUserAuditDataCommand(Guid UsuarioId);

    public sealed record DeleteUserAuditDataResult(
        int AuditoriaDeletedCount,
        bool Success);

    public class DeleteUserAuditDataUseCase(
        IMovimentacaoEstoqueAlteracaoRepository alteracaoRepository,
        IUnitOfWork unitOfWork,
        ILogger<DeleteUserAuditDataUseCase> logger)
    {
        public async Task<DeleteUserAuditDataResult> ExecuteAsync(DeleteUserAuditDataCommand command)
        {
            UseCaseGuards.EnsureNotEmpty(command.UsuarioId, nameof(command.UsuarioId));

            logger.LogInformation("Iniciando deleção de dados de auditoria do usuário {UsuarioId} (GDPR/LGPD)", command.UsuarioId);

            var deletedCount = await alteracaoRepository.DeleteByUsuarioAsync(command.UsuarioId);

            if (deletedCount > 0)
            {
                await unitOfWork.CommitAsync();
                logger.LogWarning("AUDIT: Dados de auditoria deletados por solicitação GDPR/LGPD. UsuarioId: {UsuarioId}, Count: {DeletedCount}",
                    command.UsuarioId, deletedCount);
            }

            return new DeleteUserAuditDataResult(deletedCount, Success: true);
        }
    }
}
