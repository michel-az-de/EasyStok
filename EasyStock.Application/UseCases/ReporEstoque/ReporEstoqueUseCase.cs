using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Specifications;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.ReporEstoque
{
    public sealed record ReporEstoqueCommand(
        Guid EmpresaId,
        Guid ItemEstoqueId,
        int QuantidadeAdicional,
        decimal? NovoCustoUnitario,
        decimal? NovoPrecoVendaSugerido,
        DateTime DataReposicao,
        string? VariacaoDescricao,
        string? Cor,
        string? Tamanho,
        string? Observacoes,
        string? DocumentoReferencia,
        DimensoesInput? DimensoesReais,
        DateTime? NovaValidade);

    public sealed record ReporEstoqueResult(
        Guid ItemEstoqueId,
        Guid MovimentacaoId,
        decimal QuantidadeAnterior,
        decimal QuantidadeAtual);

    public class ReporEstoqueUseCase(
        IProdutoRepository produtoRepository,
        IItemEstoqueRepository itemEstoqueRepository,
        IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository,
        IUnitOfWork unitOfWork)
    {
        public async Task<ReporEstoqueResult> ExecuteAsync(ReporEstoqueCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
            if (command.QuantidadeAdicional <= 0) throw new QuantidadeInvalidaException(command.QuantidadeAdicional);
            if (command.NovoCustoUnitario is < 0)
                throw new UseCaseValidationException("Custo unitário não pode ser negativo.");
            if (command.NovoPrecoVendaSugerido is < 0)
                throw new UseCaseValidationException("Preço de venda sugerido não pode ser negativo.");

            var item = await itemEstoqueRepository.GetByIdAsync(command.ItemEstoqueId)
                ?? throw new UseCaseValidationException("Item de estoque nao encontrado.");

            if (item.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("O item de estoque nao pertence a empresa.");

            var produto = await produtoRepository.GetByIdAsync(item.ProdutoId)
                ?? throw new UseCaseValidationException("Produto do item de estoque nao encontrado.");

            if (produto.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("O produto do item de estoque nao pertence a empresa.");

            if (!new ProdutoAtivoSpecification().EhSatisfeitaPor(produto))
                throw new ProdutoInativoException(produto.Id);

            var quantidadeAnterior = item.QuantidadeAtual.Value;
            var agora = DateTime.UtcNow;
            var quantidadeAdicional = Quantidade.From(command.QuantidadeAdicional);
            item.RegistrarReposicao(
                quantidadeAdicional,
                command.DataReposicao,
                command.VariacaoDescricao,
                command.Cor,
                command.Tamanho,
                command.Observacoes,
                command.DimensoesReais.ToValueObjectOrNull(),
                command.NovaValidade.HasValue ? Validade.From(command.NovaValidade.Value) : null,
                command.NovoCustoUnitario.HasValue ? Dinheiro.FromDecimal(command.NovoCustoUnitario.Value) : null,
                command.NovoPrecoVendaSugerido.HasValue ? Dinheiro.FromDecimal(command.NovoPrecoVendaSugerido.Value) : null,
                agora);

            var movimentacao = MovimentacaoEstoque.CriarEntrada(
                Guid.NewGuid(),
                item.EmpresaId,
                item,
                NaturezaMovimentacaoEstoque.Reposicao,
                quantidadeAdicional,
                item.CustoUnitario,
                command.DataReposicao,
                "Reposicao de estoque",
                command.DocumentoReferencia,
                agora);

            await itemEstoqueRepository.UpdateAsync(item);
            await movimentacaoEstoqueRepository.InsertAsync(movimentacao);
            await unitOfWork.CommitAsync();

            return new ReporEstoqueResult(item.Id, movimentacao.Id, quantidadeAnterior, item.QuantidadeAtual.Value);
        }
    }
}
