using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Specifications;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.RegistrarSaidaEstoque
{
    public sealed record RegistrarSaidaEstoqueCommand(
        Guid ItemEstoqueId,
        int Quantidade,
        decimal ValorVendaUnitario,
        string? Descricao,
        DateTime DataVenda,
        DateTime DataSaida,
        DateTime? DataEnvio,
        string? NotaFiscal,
        NaturezaMovimentacaoEstoque Natureza,
        CanalVenda Canal,
        string? Observacoes);

    public sealed record RegistrarSaidaEstoqueResult(
        Guid VendaId,
        Guid ItemVendaId,
        Guid MovimentacaoId,
        int QuantidadeRestante);

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
            if (command.Quantidade <= 0) throw new QuantidadeInvalidaException(command.Quantidade);

            var item = await itemEstoqueRepository.GetByIdAsync(command.ItemEstoqueId)
                ?? throw new UseCaseValidationException("Item de estoque nao encontrado.");

            var produto = await produtoRepository.GetByIdAsync(item.ProdutoId)
                ?? throw new UseCaseValidationException("Produto do item de estoque nao encontrado.");

            if (!new ProdutoAtivoSpecification().EhSatisfeitaPor(produto))
                throw new ProdutoInativoException(produto.Id);

            var itemDisponivelSpec = new ItemEstoqueDisponivelParaSaidaSpecification(command.DataSaida);
            if (!itemDisponivelSpec.EhSatisfeitaPor(item))
                throw new UseCaseValidationException("Item de estoque nao esta disponivel para saida.");

            var quantidadeSolicitada = Quantidade.From(command.Quantidade);
            if (!new EstoqueSuficienteParaSaidaSpecification(quantidadeSolicitada).EhSatisfeitaPor(item))
                throw new EstoqueInsuficienteException(item.ProdutoId, command.Quantidade, item.QuantidadeAtual.Value);

            var valorUnitario = Dinheiro.FromDecimal(command.ValorVendaUnitario);
            var valorTotal = Dinheiro.FromDecimal(valorUnitario.Valor * quantidadeSolicitada.Value);
            var agora = DateTime.UtcNow;

            item.QuantidadeAtual = item.QuantidadeAtual.Subtract(quantidadeSolicitada);
            item.UltimaMovimentacaoEm = command.DataSaida;
            item.AlteradoEm = agora;
            item.Status = item.QuantidadeAtual.Value == 0 ? StatusItemEstoque.Esgotado : StatusItemEstoque.Ativo;

            var venda = new Venda
            {
                Id = Guid.NewGuid(),
                EmpresaId = item.EmpresaId,
                Canal = command.Canal,
                Natureza = command.Natureza,
                DataVenda = command.DataVenda,
                DataEnvio = command.DataEnvio,
                NumeroNotaFiscal = command.NotaFiscal?.Trim(),
                ValorTotal = valorTotal,
                Observacoes = command.Observacoes?.Trim(),
                CriadoEm = agora
            };

            var itemVenda = new ItemVenda
            {
                Id = Guid.NewGuid(),
                VendaId = venda.Id,
                ItemEstoqueId = item.Id,
                ProdutoId = item.ProdutoId,
                ProdutoVariacaoId = item.ProdutoVariacaoId,
                DescricaoSnapshot = command.Descricao?.Trim() ?? item.DescricaoAnuncio ?? produto.DescricaoBase,
                VariacaoSnapshot = item.VariacaoDescricao,
                Quantidade = quantidadeSolicitada,
                PrecoUnitario = valorUnitario,
                PrecoTotal = valorTotal,
                CriadoEm = agora,
                Produto = produto
            };

            venda.ItensVenda = [itemVenda];

            if (!new VendaPossuiItensValidosSpecification().EhSatisfeitaPor(venda))
                throw new VendaSemItensException(venda.Id);

            var movimentacao = new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = item.EmpresaId,
                ItemEstoqueId = item.Id,
                ProdutoId = item.ProdutoId,
                ProdutoVariacaoId = item.ProdutoVariacaoId,
                VendaId = venda.Id,
                Tipo = TipoMovimentacaoEstoque.Saida,
                Natureza = command.Natureza,
                Quantidade = quantidadeSolicitada,
                ValorUnitario = valorUnitario,
                ValorTotal = valorTotal,
                DataMovimentacao = command.DataSaida,
                Descricao = itemVenda.DescricaoSnapshot,
                DocumentoReferencia = command.NotaFiscal?.Trim(),
                CriadoEm = agora
            };

            await itemEstoqueRepository.UpdateAsync(item);
            await vendaRepository.AddAsync(venda);
            await itemVendaRepository.AddAsync(itemVenda);
            await movimentacaoEstoqueRepository.AddAsync(movimentacao);
            await unitOfWork.CommitAsync();

            return new RegistrarSaidaEstoqueResult(venda.Id, itemVenda.Id, movimentacao.Id, item.QuantidadeAtual.Value);
        }
    }
}
