using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Specifications;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.RegistrarSaidaEstoque
{
    public sealed record RegistrarSaidaEstoqueItemCommand(
        Guid ItemEstoqueId,
        int Quantidade,
        decimal ValorVendaUnitario,
        string? Descricao);

    public sealed record RegistrarSaidaEstoqueCommand(
        Guid EmpresaId,
        IReadOnlyCollection<RegistrarSaidaEstoqueItemCommand> Itens,
        DateTime DataVenda,
        DateTime DataSaida,
        DateTime? DataEnvio,
        string? NotaFiscal,
        NaturezaMovimentacaoEstoque Natureza,
        CanalVenda Canal,
        string? Observacoes);

    public sealed record RegistrarSaidaEstoqueItemResult(
        Guid ItemEstoqueId,
        Guid ItemVendaId,
        Guid MovimentacaoId,
        int QuantidadeRestante);

    public sealed record RegistrarSaidaEstoqueResult(
        Guid VendaId,
        IReadOnlyCollection<RegistrarSaidaEstoqueItemResult> Itens,
        decimal ValorTotal);

    public class RegistrarSaidaEstoqueUseCase(
        IProdutoRepository produtoRepository,
        IItemEstoqueRepository itemEstoqueRepository,
        IVendaRepository vendaRepository,
        IItemVendaRepository itemVendaRepository,
        IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository,
        IUnitOfWork unitOfWork)
    {
        public async Task<RegistrarSaidaEstoqueResult> ExecuteAsync(RegistrarSaidaEstoqueCommand command)
        {
            if (command.EmpresaId == Guid.Empty) throw new UseCaseValidationException("EmpresaId e obrigatorio.");
            if (command.Itens is null || command.Itens.Count == 0) throw new VendaSemItensException(Guid.Empty);

            var agora = DateTime.UtcNow;

            var venda = Venda.Criar(
                Guid.NewGuid(),
                command.EmpresaId,
                command.Canal,
                command.Natureza,
                command.DataVenda,
                command.DataEnvio,
                command.NotaFiscal,
                command.Observacoes,
                agora);

            var itensVenda = new List<ItemVenda>();
            var movimentacoes = new List<MovimentacaoEstoque>();
            var itensResult = new List<RegistrarSaidaEstoqueItemResult>();

            foreach (var comandoItem in command.Itens)
            {
                if (comandoItem.Quantidade <= 0) throw new QuantidadeInvalidaException(comandoItem.Quantidade);

                var item = await itemEstoqueRepository.GetByIdAsync(comandoItem.ItemEstoqueId)
                    ?? throw new UseCaseValidationException("Item de estoque nao encontrado.");

                if (item.EmpresaId != command.EmpresaId)
                    throw new UseCaseValidationException("O item de estoque nao pertence a empresa.");

                var produto = await produtoRepository.GetByIdAsync(item.ProdutoId)
                    ?? throw new UseCaseValidationException("Produto do item de estoque nao encontrado.");

                if (produto.EmpresaId != command.EmpresaId)
                    throw new UseCaseValidationException("O produto do item de estoque nao pertence a empresa.");

                if (!new ProdutoAtivoSpecification().EhSatisfeitaPor(produto))
                    throw new ProdutoInativoException(produto.Id);

                var quantidadeSolicitada = Quantidade.From(comandoItem.Quantidade);
                var valorUnitario = Dinheiro.FromDecimal(comandoItem.ValorVendaUnitario);
                var valorTotal = Dinheiro.FromDecimal(valorUnitario.Valor * quantidadeSolicitada.Value);
                item.RegistrarSaida(quantidadeSolicitada, command.DataSaida, agora);

                var itemVenda = new ItemVenda
                {
                    Id = Guid.NewGuid(),
                    VendaId = venda.Id,
                    ItemEstoqueId = item.Id,
                    ProdutoId = item.ProdutoId,
                    ProdutoVariacaoId = item.ProdutoVariacaoId,
                    DescricaoSnapshot = comandoItem.Descricao?.Trim() ?? item.DescricaoAnuncio ?? produto.DescricaoBase,
                    VariacaoSnapshot = item.VariacaoDescricao,
                    Quantidade = quantidadeSolicitada,
                    PrecoUnitario = valorUnitario,
                    PrecoTotal = valorTotal,
                    CriadoEm = agora,
                    Produto = produto
                };

                var movimentacao = MovimentacaoEstoque.CriarSaida(
                    Guid.NewGuid(),
                    item.EmpresaId,
                    item,
                    venda.Id,
                    command.Natureza,
                    quantidadeSolicitada,
                    valorUnitario,
                    command.DataSaida,
                    itemVenda.DescricaoSnapshot,
                    command.NotaFiscal,
                    agora);

                itensVenda.Add(itemVenda);
                movimentacoes.Add(movimentacao);
                itensResult.Add(new RegistrarSaidaEstoqueItemResult(item.Id, itemVenda.Id, movimentacao.Id, item.QuantidadeAtual.Value));

                await itemEstoqueRepository.UpdateAsync(item);
                venda.AdicionarItem(itemVenda);
            }

            if (!new VendaPossuiItensValidosSpecification().EhSatisfeitaPor(venda))
                throw new VendaSemItensException(venda.Id);

            await vendaRepository.AddAsync(venda);
            foreach (var itemVenda in itensVenda)
                await itemVendaRepository.AddAsync(itemVenda);
            foreach (var movimentacao in movimentacoes)
                await movimentacaoEstoqueRepository.AddAsync(movimentacao);
            await unitOfWork.CommitAsync();

            return new RegistrarSaidaEstoqueResult(venda.Id, itensResult, venda.ValorTotal.Valor);
        }
    }
}
