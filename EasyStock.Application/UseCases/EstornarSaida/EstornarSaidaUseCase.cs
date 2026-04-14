using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.EstornarSaida
{
    public sealed record EstornarSaidaCommand(
        [property: Required] Guid EmpresaId,
        [property: Required] Guid MovimentacaoId,
        string? Motivo);

    public sealed record EstornarSaidaResult(
        Guid EstornoId,
        Guid MovimentacaoOriginalId,
        int QuantidadeRestaurada);

    public class EstornarSaidaUseCase(
        IMovimentacaoEstoqueRepository movimentacaoRepository,
        IItemEstoqueRepository itemEstoqueRepository,
        IUnitOfWork unitOfWork,
        ILogger<EstornarSaidaUseCase> logger)
    {
        public async Task<EstornarSaidaResult> ExecuteAsync(EstornarSaidaCommand command)
        {
            if (command.EmpresaId == Guid.Empty)
                throw new UseCaseValidationException("EmpresaId é obrigatório.");
            if (command.MovimentacaoId == Guid.Empty)
                throw new UseCaseValidationException("MovimentacaoId é obrigatório.");

            // FOR UPDATE evita duplo estorno: requests concorrentes aguardam o lock
            var original = await movimentacaoRepository.GetByIdComLockAsync(command.MovimentacaoId)
                ?? throw new UseCaseValidationException("Movimentacao nao encontrada.");

            if (original.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("A movimentacao nao pertence a empresa informada.");

            if (original.Tipo != TipoMovimentacaoEstoque.Saida)
                throw new UseCaseValidationException("Somente movimentacoes de saida podem ser estornadas.");

            if (original.EstornadaEm.HasValue)
                throw new MovimentacaoJaEstornadaException(original.Id);

            var agora = DateTime.UtcNow;

            var itemEstoque = await itemEstoqueRepository.GetByIdAsync(original.ItemEstoqueId)
                ?? throw new UseCaseValidationException("Item de estoque da movimentacao nao encontrado.");

            itemEstoque.RestaurarQuantidade(original.Quantidade, agora);

            var estorno = MovimentacaoEstoque.CriarEstorno(
                Guid.NewGuid(),
                original,
                agora,
                command.Motivo,
                agora);

            original.MarcarComoEstornada(agora);

            await movimentacaoRepository.InsertAsync(estorno);
            await movimentacaoRepository.UpdateAsync(original);
            await itemEstoqueRepository.UpdateAsync(itemEstoque);
            await unitOfWork.CommitAsync();

            logger.LogWarning("AUDIT: Estorno registrado. EstornoId: {EstornoId}, OriginalId: {OriginalId}, EmpresaId: {EmpresaId}, Quantidade: {Quantidade}",
                estorno.Id, original.Id, command.EmpresaId, original.Quantidade.Value);

            return new EstornarSaidaResult(estorno.Id, original.Id, original.Quantidade.Value);
        }
    }
}
