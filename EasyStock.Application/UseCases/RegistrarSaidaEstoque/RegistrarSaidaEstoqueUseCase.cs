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
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.RegistrarSaidaEstoque
{
    public sealed record RegistrarSaidaEstoqueItemCommand(
        Guid? ItemEstoqueId,
        [property: Required] Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        [property: Range(1, int.MaxValue)] int Quantidade,
        [property: Range(0, double.MaxValue)] decimal ValorVendaUnitario,
        string? Descricao)
    {
        public RegistrarSaidaEstoqueItemCommand(
            Guid itemEstoqueId,
            int quantidade,
            decimal valorVendaUnitario,
            string? descricao)
            : this(itemEstoqueId, Guid.Empty, null, quantidade, valorVendaUnitario, descricao)
        {
        }

        public RegistrarSaidaEstoqueItemCommand(
            Guid produtoId,
            Guid? produtoVariacaoId,
            int quantidade,
            decimal valorVendaUnitario,
            string? descricao)
            : this(null, produtoId, produtoVariacaoId, quantidade, valorVendaUnitario, descricao)
        {
        }
    }

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
        ILogger<RegistrarSaidaEstoqueUseCase> logger,
        IPublicadorEventos? publicadorEventos = null)
    {
        public async Task<RegistrarSaidaEstoqueResult> ExecuteAsync(RegistrarSaidaEstoqueCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
            if (command.Itens is null || command.Itens.Count == 0) throw new VendaSemItensException(Guid.Empty);

            logger.LogInformation("Iniciando registro de sa�da de estoque. EmpresaId: {EmpresaId}, Itens: {QuantidadeItens}, Natureza: {Natureza}",
                command.EmpresaId, command.Itens.Count, command.Natureza);

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

                var quantidadeSolicitada = Quantidade.From(comandoItem.Quantidade);
                var valorUnitario = Dinheiro.FromDecimal(comandoItem.ValorVendaUnitario);
                var lotes = comandoItem.ItemEstoqueId.HasValue
                    ? [await ObterItemDiretoAsync(command.EmpresaId, comandoItem.ItemEstoqueId.Value)]
                    : (await itemEstoqueRepository.GetLotesDisponiveisParaSaidaAsync(command.EmpresaId, comandoItem.ProdutoId, comandoItem.ProdutoVariacaoId)).ToArray();

                var produtoId = comandoItem.ItemEstoqueId.HasValue
                    ? lotes.Single().ProdutoId
                    : comandoItem.ProdutoId;

                if (produtoId == Guid.Empty)
                    throw new UseCaseValidationException("ProdutoId é obrigatório para registrar a saída.");

                var produto = await produtoRepository.GetByIdAsync(produtoId)
                    ?? throw new UseCaseValidationException("Produto do item de estoque nao encontrado.");

                if (produto.EmpresaId != command.EmpresaId)
                    throw new UseCaseValidationException("O produto do item de estoque nao pertence a empresa.");

                if (!new ProdutoAtivoSpecification().EhSatisfeitaPor(produto))
                    throw new ProdutoInativoException(produto.Id);

                var disponivel = lotes.Sum(i => i.QuantidadeAtual.Value);
                if (disponivel < quantidadeSolicitada.Value)
                    throw new EstoqueInsuficienteException(produto.Id, quantidadeSolicitada.Value, disponivel);

                var restante = quantidadeSolicitada.Value;
                var totalSaidoNoComando = 0;
                var lotesTocados = new List<ItemEstoque>();

                foreach (var lote in lotes)
                {
                    if (restante <= 0)
                        break;

                    lote.GarantirDisponivelParaSaida(command.DataSaida);
                    var quantidadeConsumida = Math.Min(lote.QuantidadeAtual.Value, restante);
                    if (quantidadeConsumida <= 0)
                        continue;

                    var quantidadeDoLote = Quantidade.From(quantidadeConsumida);
                    lote.RegistrarSaida(quantidadeDoLote, command.DataSaida, agora);

                    var itemVenda = new ItemVenda
                    {
                        Id = Guid.NewGuid(),
                        VendaId = venda.Id,
                        ItemEstoqueId = lote.Id,
                        ProdutoId = lote.ProdutoId,
                        ProdutoVariacaoId = lote.ProdutoVariacaoId,
                        DescricaoSnapshot = comandoItem.Descricao?.Trim() ?? lote.DescricaoAnuncio ?? produto.DescricaoBase,
                        VariacaoSnapshot = lote.VariacaoDescricao,
                        Quantidade = quantidadeDoLote,
                        PrecoUnitario = valorUnitario,
                        PrecoTotal = Dinheiro.FromDecimal(valorUnitario.Valor * quantidadeConsumida),
                        CriadoEm = agora,
                        Produto = produto
                    };

                    var movimentacao = MovimentacaoEstoque.CriarSaida(
                        Guid.NewGuid(),
                        lote.EmpresaId,
                        lote,
                        venda.Id,
                        command.Natureza,
                        quantidadeDoLote,
                        valorUnitario,
                        command.DataSaida,
                        itemVenda.DescricaoSnapshot,
                        command.NotaFiscal,
                        agora);

                    itensVenda.Add(itemVenda);
                    movimentacoes.Add(movimentacao);
                    itensResult.Add(new RegistrarSaidaEstoqueItemResult(
                        lote.Id,
                        lote.ProdutoId,
                        itemVenda.Id,
                        movimentacao.Id,
                        quantidadeConsumida,
                        lote.QuantidadeAtual.Value,
                        command.Observacoes));

                    venda.AdicionarItem(itemVenda);
                    lotesTocados.Add(lote);

                    restante -= quantidadeConsumida;
                    totalSaidoNoComando += quantidadeConsumida;
                }

                await AtualizarIndicadoresFifoAsync(command.EmpresaId, produto.Id, lotesTocados, totalSaidoNoComando, command.DataSaida);
            }

            if (!new VendaPossuiItensValidosSpecification().EhSatisfeitaPor(venda))
                throw new VendaSemItensException(venda.Id);

            await vendaRepository.InsertAsync(venda);
            await itemVendaRepository.InsertRangeAsync(itensVenda);
            await movimentacaoEstoqueRepository.InsertRangeAsync(movimentacoes);
            await unitOfWork.CommitAsync();

            logger.LogWarning("AUDIT: Sa�da de estoque registrada. VendaId: {VendaId}, EmpresaId: {EmpresaId}, ValorTotal: {ValorTotal}, Itens: {QuantidadeItens}",
                venda.Id, command.EmpresaId, venda.ValorTotal.Valor, itensResult.Count);

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

        private async Task<ItemEstoque> ObterItemDiretoAsync(Guid empresaId, Guid itemEstoqueId)
        {
            var item = await itemEstoqueRepository.GetByIdAsync(itemEstoqueId)
                ?? throw new UseCaseValidationException("Item de estoque nao encontrado.");

            if (item.EmpresaId != empresaId)
                throw new UseCaseValidationException("O item de estoque nao pertence a empresa.");

            return item;
        }

        private async Task AtualizarIndicadoresFifoAsync(Guid empresaId, Guid produtoId, IEnumerable<ItemEstoque> lotesTocados, int quantidadeSaidaAtual, DateTime dataSaida)
        {
            const int janelaDias = 30;
            var inicioJanela = dataSaida.AddDays(-janelaDias);
            var taxaAnterior = await movimentacaoEstoqueRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, inicioJanela, dataSaida);
            var totalAnterior = taxaAnterior * janelaDias;
            var velocidadeAtualizada = (totalAnterior + quantidadeSaidaAtual) / janelaDias;

            foreach (var lote in lotesTocados)
            {
                lote.AtualizarVelocidadeSaida(velocidadeAtualizada, dataSaida);
                await itemEstoqueRepository.UpdateAsync(lote);
            }
        }
    }
}
