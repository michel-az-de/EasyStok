using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Events;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Specifications;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.RegistrarSaidaEstoque
{
    public sealed record RegistrarSaidaEstoqueItemCommand(
        [property: Required] Guid ItemEstoqueId,
        [property: Range(1, int.MaxValue)] int Quantidade,
        [property: Range(0, double.MaxValue)] decimal ValorVendaUnitario,
        string? Descricao);

    public sealed record RegistrarSaidaEstoqueCommand(
        [property: Required] Guid EmpresaId,
        [property: Required][property: MinLength(1)] IReadOnlyCollection<RegistrarSaidaEstoqueItemCommand> Itens,
        DateTime DataVenda,
        DateTime DataSaida,
        DateTime? DataEnvio,
        string? NotaFiscal,
        NaturezaMovimentacaoEstoque Natureza,
        CanalVenda Canal,
        string? Observacoes);

    public sealed record RegistrarSaidaEstoqueItemResult(
        Guid ItemEstoqueId,
        Guid ProdutoId,
        Guid ItemVendaId,
        Guid MovimentacaoId,
        int QuantidadeSaida,
        int QuantidadeRestante,
        string? Motivo);

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
        IUnitOfWork unitOfWork,
        IPublicadorEventos? publicadorEventos = null)
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
                itensResult.Add(new RegistrarSaidaEstoqueItemResult(
                    item.Id,
                    item.ProdutoId,
                    itemVenda.Id,
                    movimentacao.Id,
                    quantidadeSolicitada.Value,
                    item.QuantidadeAtual.Value,
                    command.Observacoes));

                await itemEstoqueRepository.UpdateAsync(item);
                venda.AdicionarItem(itemVenda);
            }

            if (!new VendaPossuiItensValidosSpecification().EhSatisfeitaPor(venda))
                throw new VendaSemItensException(venda.Id);

            await vendaRepository.InsertAsync(venda);
            foreach (var itemVenda in itensVenda)
                await itemVendaRepository.InsertAsync(itemVenda);
            foreach (var movimentacao in movimentacoes)
                await movimentacaoEstoqueRepository.InsertAsync(movimentacao);
            await unitOfWork.CommitAsync();

            if (publicadorEventos is not null)
            {
                await publicadorEventos.PublicarAsync(new VendaRegistrada(
                    Guid.NewGuid(), agora, venda.Id, venda.EmpresaId, venda.ValorTotal.Valor));
                foreach (var r in itensResult)
                    await publicadorEventos.PublicarAsync(new SaidaEstoqueRegistrada(
                        Guid.NewGuid(), agora, r.ItemEstoqueId, r.ProdutoId, command.EmpresaId, r.QuantidadeSaida, r.Motivo));
            }

            return new RegistrarSaidaEstoqueResult(venda.Id, itensResult, venda.ValorTotal.Valor);
        }
    }
}
