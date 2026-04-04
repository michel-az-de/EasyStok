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
        int QuantidadeAnterior,
        int QuantidadeAtual);

    public class ReporEstoqueUseCase(
        IProdutoRepository produtoRepository,
        IItemEstoqueRepository itemEstoqueRepository,
        IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository,
        IUnitOfWork unitOfWork)
    {
        public async Task<ReporEstoqueResult> ExecuteAsync(ReporEstoqueCommand command)
        {
            if (command.QuantidadeAdicional <= 0) throw new QuantidadeInvalidaException(command.QuantidadeAdicional);

            var item = await itemEstoqueRepository.GetByIdAsync(command.ItemEstoqueId)
                ?? throw new UseCaseValidationException("Item de estoque nao encontrado.");

            var produto = await produtoRepository.GetByIdAsync(item.ProdutoId)
                ?? throw new UseCaseValidationException("Produto do item de estoque nao encontrado.");

            if (!new ProdutoAtivoSpecification().EhSatisfeitaPor(produto))
                throw new ProdutoInativoException(produto.Id);

            var quantidadeAnterior = item.QuantidadeAtual.Value;
            var quantidadeAdicional = Quantidade.From(command.QuantidadeAdicional);
            item.QuantidadeAtual = item.QuantidadeAtual.Add(quantidadeAdicional);
            item.Status = StatusItemEstoque.Ativo;
            item.VariacaoDescricao = command.VariacaoDescricao?.Trim() ?? item.VariacaoDescricao;
            item.Cor = command.Cor?.Trim() ?? item.Cor;
            item.Tamanho = command.Tamanho?.Trim() ?? item.Tamanho;
            item.Observacoes = command.Observacoes?.Trim() ?? item.Observacoes;
            item.DimensoesReais = command.DimensoesReais.ToValueObjectOrNull() ?? item.DimensoesReais;
            item.ValidadeEm = command.NovaValidade.HasValue ? Validade.From(command.NovaValidade.Value) : item.ValidadeEm;
            item.UltimaMovimentacaoEm = command.DataReposicao;
            item.AlteradoEm = DateTime.UtcNow;

            if (command.NovoCustoUnitario.HasValue)
                item.CustoUnitario = Dinheiro.FromDecimal(command.NovoCustoUnitario.Value);

            if (command.NovoPrecoVendaSugerido.HasValue)
                item.PrecoVendaSugerido = Dinheiro.FromDecimal(command.NovoPrecoVendaSugerido.Value);

            var movimentacao = new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = item.EmpresaId,
                ItemEstoqueId = item.Id,
                ProdutoId = item.ProdutoId,
                ProdutoVariacaoId = item.ProdutoVariacaoId,
                Tipo = TipoMovimentacaoEstoque.Entrada,
                Natureza = NaturezaMovimentacaoEstoque.Reposicao,
                Quantidade = quantidadeAdicional,
                ValorUnitario = item.CustoUnitario,
                ValorTotal = Dinheiro.FromDecimal(item.CustoUnitario.Valor * quantidadeAdicional.Value),
                DataMovimentacao = command.DataReposicao,
                Descricao = "Reposicao de estoque",
                DocumentoReferencia = command.DocumentoReferencia?.Trim(),
                CriadoEm = DateTime.UtcNow
            };

            await itemEstoqueRepository.UpdateAsync(item);
            await movimentacaoEstoqueRepository.AddAsync(movimentacao);
            await unitOfWork.CommitAsync();

            return new ReporEstoqueResult(item.Id, movimentacao.Id, quantidadeAnterior, item.QuantidadeAtual.Value);
        }
    }
}
