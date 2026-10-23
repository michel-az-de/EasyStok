using EasyStock.Domain.Services;

namespace EasyStock.Application.UseCases.EstornarSaida
{
    public sealed record EstornarSaidaCommand(
        [property: Required] Guid EmpresaId,
        [property: Required] Guid MovimentacaoId,
        [property: Required(ErrorMessage = "Motivo do estorno e obrigatorio.")]
        [property: MinLength(3, ErrorMessage = "Motivo deve ter ao menos 3 caracteres.")]
        [property: MaxLength(300)]
        string Motivo);

    public sealed record EstornarSaidaResult(
        Guid EstornoId,
        Guid MovimentacaoOriginalId,
        decimal QuantidadeRestaurada);

    public class EstornarSaidaUseCase(
        IMovimentacaoEstoqueRepository movimentacaoRepository,
        IItemEstoqueRepository itemEstoqueRepository,
        IUnitOfWork unitOfWork,
        ILogger<EstornarSaidaUseCase> logger,
        ICurrentUserAccessor? currentUser = null)
    {
        public async Task<EstornarSaidaResult> ExecuteAsync(EstornarSaidaCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
            UseCaseGuards.EnsureNotEmpty(command.MovimentacaoId, "MovimentacaoId");
            if (string.IsNullOrWhiteSpace(command.Motivo) || command.Motivo.Trim().Length < 3)
                throw new UseCaseValidationException("Motivo do estorno e obrigatorio (minimo 3 caracteres).");

            // ExecuteInTransactionAsync usa IExecutionStrategy do Npgsql (compativel
            // com EnableRetryOnFailure). FOR UPDATE em GetByIdComLockAsync mantem
            // o lock pessimistico ate o commit final.
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // FOR UPDATE evita duplo estorno: requests concorrentes aguardam o lock.
                // empresaId no raw SQL aplica defesa-em-profundidade (o repo filtra
                // explicitamente — nao depende apenas do global query filter do EF).
                var original = await movimentacaoRepository.GetByIdComLockAsync(command.EmpresaId, command.MovimentacaoId, ct)
                    ?? throw new UseCaseValidationException("Movimentacao nao encontrada.");

                if (original.EmpresaId != command.EmpresaId)
                    throw new UseCaseValidationException("A movimentacao nao pertence a empresa informada.");

                if (original.Tipo != TipoMovimentacaoEstoque.Saida)
                    throw new UseCaseValidationException("Somente movimentacoes de saida podem ser estornadas.");

                // Re-validação dentro da transação (com lock) previne race condition de duplo estorno
                if (original.EstornadaEm.HasValue)
                    throw new MovimentacaoJaEstornadaException(original.Id);

                var agora = DateTime.UtcNow;

                var itemEstoque = await itemEstoqueRepository.GetByIdAsync(original.ItemEstoqueId)
                    ?? throw new UseCaseValidationException("Item de estoque da movimentacao nao encontrado.");

                // SEGURANÇA: Validar que itemEstoque pertence à empresa solicitada (multi-tenant isolation)
                if (itemEstoque.EmpresaId != command.EmpresaId)
                    throw new UseCaseValidationException("O item de estoque nao pertence a empresa informada.");

                var auditoria = currentUser is null ? null : new AuditoriaContexto(
                    UsuarioId: currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId,
                    Ip: currentUser.Ip,
                    UserAgent: currentUser.UserAgent,
                    DispositivoId: currentUser.DispositivoId);

                // #2003 (estoque-17): a devolução é a regra única de DevolucaoDeSaida (abate o descoberto, repõe o
                // saldo, marca a saída e cria o estorno).
                var estorno = DevolucaoDeSaida.Aplicar(original, itemEstoque, agora, command.Motivo, command.Motivo, auditoria);

                await movimentacaoRepository.InsertAsync(estorno);
                await movimentacaoRepository.UpdateAsync(original);
                await itemEstoqueRepository.UpdateAsync(itemEstoque);
                await unitOfWork.CommitAsync();

                logger.LogWarning("AUDIT: Estorno registrado. EstornoId: {EstornoId}, OriginalId: {OriginalId}, EmpresaId: {EmpresaId}, Quantidade: {Quantidade}, UsuarioId: {UsuarioId}, Ip: {Ip}",
                    estorno.Id, original.Id, command.EmpresaId, original.Quantidade.Value, estorno.UsuarioId, estorno.Ip);

                return new EstornarSaidaResult(estorno.Id, original.Id, original.Quantidade.Value);
            });
        }
    }
}
