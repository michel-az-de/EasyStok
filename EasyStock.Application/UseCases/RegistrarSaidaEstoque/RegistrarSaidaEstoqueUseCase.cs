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
        IPublicadorEventos? publicadorEventos = null,
        EasyStock.Application.Ports.Output.ICurrentUserAccessor? currentUser = null,
        IConfiguracaoLojaRepository? configuracaoLojaRepository = null)
    {
        public async Task<RegistrarSaidaEstoqueResult> ExecuteAsync(RegistrarSaidaEstoqueCommand command)
        {
            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
            if (command.Itens is null || command.Itens.Count == 0) throw new VendaSemItensException(Guid.Empty);

            logger.LogInformation("Iniciando registro de sa�da de estoque. EmpresaId: {EmpresaId}, Itens: {QuantidadeItens}, Natureza: {Natureza}",
                command.EmpresaId, command.Itens.Count, command.Natureza);

            var configuracao = configuracaoLojaRepository is not null
                ? await configuracaoLojaRepository.GetByLojaIdAsync(command.EmpresaId)
                : null;
            // FifoAtivo=true → FEFO (saída pelo lote mais próximo do vencimento).
            // FifoAtivo=false → FIFO puro (por data de entrada).
            var fefo = configuracao?.FifoAtivo ?? true;

            // Transação explícita — necessária para que o FOR UPDATE adquirido
            // em GetByIdComLockAsync/GetLotesDisponiveisParaSaidaAsync mantenha
            // o lock até o CommitAsync. Sem isso, EF abre transação implícita
            // só no SaveChanges e o lock libera ao fim de cada statement.
            await using var tx = await unitOfWork.BeginTransactionAsync();

            var agora = DateTime.UtcNow;

            var auditoria = currentUser is null ? null : new AuditoriaContexto(
                UsuarioId: currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId,
                Ip: currentUser.Ip,
                UserAgent: currentUser.UserAgent,
                DispositivoId: currentUser.DispositivoId);

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

                // Aceita 0 apenas em saídas não-comerciais (Perda, Ajuste, Doacao);
                // venda real exige preço > 0 para auditoria contábil.
                var ehVendaComercial = command.Natureza == NaturezaMovimentacaoEstoque.Venda;
                if (ehVendaComercial && comandoItem.ValorVendaUnitario <= 0)
                    throw new UseCaseValidationException(
                        "Saída do tipo Venda requer ValorVendaUnitario maior que zero.");

                // Sanidade: valor absurdo (provavelmente erro de digitação) — limite
                // configurável via appsettings, default 100 milhões.
                const decimal valorMaximoSanidade = 100_000_000m;
                if (comandoItem.ValorVendaUnitario > valorMaximoSanidade)
                    throw new UseCaseValidationException(
                        $"ValorVendaUnitario excede o teto de sanidade ({valorMaximoSanidade:C}).");

                var quantidadeSolicitada = Quantidade.From(comandoItem.Quantidade);
                var valorUnitario = Dinheiro.FromDecimal(comandoItem.ValorVendaUnitario);
                var lotes = comandoItem.ItemEstoqueId.HasValue
                    ? [await ObterItemDiretoAsync(command.EmpresaId, comandoItem.ItemEstoqueId.Value)]
                    : (await itemEstoqueRepository.GetLotesDisponiveisParaSaidaAsync(command.EmpresaId, comandoItem.ProdutoId, comandoItem.ProdutoVariacaoId, fefo)).ToArray();

                if (lotes.Length == 0 && comandoItem.ItemEstoqueId.HasValue)
                    throw new UseCaseValidationException("Item de estoque nao encontrado ou nao esta disponivel.");

                // Só faz sentido validar "exatamente 1 lote" quando o caller passou
                // ItemEstoqueId (saída direta de um lote específico). No caminho
                // FIFO/FEFO por ProdutoId, esperamos N lotes — SingleOrDefault aqui
                // estouraria com >1 elemento e quebrava a saída automática por lotes.
                ItemEstoque? item = null;
                if (comandoItem.ItemEstoqueId.HasValue)
                {
                    if (lotes.Length != 1)
                        throw new UseCaseValidationException($"Esperado exatamente 1 item, mas encontrado {lotes.Length}.");
                    item = lotes[0];
                }

                var produtoId = comandoItem.ItemEstoqueId.HasValue
                    ? item!.ProdutoId
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
                        agora,
                        auditoria);

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
            // FOR UPDATE: dois usuários dando saída do mesmo lote ao mesmo
            // tempo precisam serializar via lock pessimista. Sem isso, ambos
            // leem 5, ambos validam, ambos descontam → saldo negativo.
            var item = await itemEstoqueRepository.GetByIdComLockAsync(empresaId, itemEstoqueId)
                ?? throw new UseCaseValidationException("Item de estoque nao encontrado.");

            // Garantia adicional além do filtro WHERE — repositório já filtra
            // por EmpresaId, mas defensivo aqui.
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

            // Atualiza velocidade em memória e persiste em batch (1 round-trip)
            // em vez de UpdateAsync individual por lote.
            var lotesList = lotesTocados as IList<ItemEstoque> ?? lotesTocados.ToList();
            if (lotesList.Count == 0) return;

            foreach (var lote in lotesList)
            {
                lote.AtualizarVelocidadeSaida(velocidadeAtualizada, dataSaida);
            }

            await itemEstoqueRepository.UpdateRangeAsync(lotesList);
        }
    }
}
