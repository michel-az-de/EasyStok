using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.CriarCliente;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;

namespace EasyStock.Application.UseCases.FinalizarVendaBalcao;

/// <summary>Item da venda balcao: produto existente (ProdutoId) ou produto novo (NovoProduto + CategoriaId).</summary>
public sealed record FinalizarVendaBalcaoItemInput(
    [property: Required][property: MaxLength(150)] string Nome,
    decimal Quantidade,
    decimal PrecoUnitario,
    Guid? ProdutoId = null,
    bool NovoProduto = false,
    Guid? CategoriaId = null,
    decimal? CustoReferencia = null);

public sealed record FinalizarVendaBalcaoCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId = null,
    Guid? ClienteId = null,
    [property: MaxLength(150)] string? NovoClienteNome = null,
    [property: MaxLength(32)] string? NovoClienteApt = null,
    [property: MaxLength(32)] string? NovoClienteTelefone = null,
    [property: MaxLength(150)] string? ClienteNomeAdHoc = null,
    IReadOnlyList<FinalizarVendaBalcaoItemInput>? Itens = null,
    bool Pagou = false,
    [property: MaxLength(20)] string? FormaPagamento = null,
    string? Observacoes = null,
    Guid? CriadoPorUserId = null,
    [property: MaxLength(120)] string? CriadoPorNome = null);

public sealed record FinalizarVendaBalcaoResult(Guid PedidoId, bool Pago, decimal Total, string FormaPagamento);

/// <summary>
/// Venda balcao: cria (opcionalmente) cliente novo + produtos novos (com entrada de estoque),
/// cria o pedido, FINALIZA (saida de estoque ao chegar em Pronto/Entregue) e registra o pagamento,
/// tudo numa TRANSACAO UNICA (BeginTransactionAsync + rollback-on-dispose). Se qualquer passo
/// falhar, nada fica orfao.
///
/// NAO reimplementa logica: compoe os use cases ja testados. Cada um chama uow.CommitAsync()
/// (= SaveChanges, so flush dentro da transacao aberta); o commit real e o tx.CommitAsync() no fim.
/// Nenhum dos use cases compostos abre transacao propria (so CommitAsync), entao nao ha aninhamento.
/// Usamos BeginTransactionAsync (sem retry) de proposito: ExecuteInTransactionAsync reexecutaria
/// os creates (novos Guids) em falha transitoria, duplicando produto/pedido.
///
/// Caixa: registrar pagamento ja reconcilia por agregacao (a soma de PedidoPagamento do dia)
/// e abre o caixa automaticamente no primeiro pagamento. Nao criamos MovimentoCaixa (by design).
/// </summary>
public class FinalizarVendaBalcaoUseCase(
    CriarClienteUseCase criarClienteUC,
    CadastrarProdutoUseCase cadastrarProdutoUC,
    RegistrarEntradaEstoqueUseCase registrarEntradaUC,
    CriarPedidoUseCase criarPedidoUC,
    AtualizarStatusPedidoUseCase atualizarStatusUC,
    RegistrarPagamentoPedidoUseCase registrarPagamentoUC,
    IUnitOfWork uow,
    ILogger<FinalizarVendaBalcaoUseCase> logger)
{
    private static readonly string[] PassosAteEntregue = { "preparando", "pronto", "entregue" };

    public async Task<FinalizarVendaBalcaoResult> ExecuteAsync(FinalizarVendaBalcaoCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.Itens is null || cmd.Itens.Count == 0)
            throw new UseCaseValidationException("Adicione pelo menos 1 item ao pedido.");

        const string origem = "balcao";
        var formaPagamento = string.IsNullOrWhiteSpace(cmd.FormaPagamento)
            ? "dinheiro"
            : cmd.FormaPagamento!.Trim().ToLowerInvariant();

        await using var tx = await uow.BeginTransactionAsync(ct);

        // 1. Cliente novo (opcional). Falha aqui aborta tudo (atomico).
        var clienteId = cmd.ClienteId;
        if ((clienteId is null || clienteId == Guid.Empty) && !string.IsNullOrWhiteSpace(cmd.NovoClienteNome))
        {
            var cli = await criarClienteUC.ExecuteAsync(new CriarClienteCommand(
                cmd.EmpresaId, cmd.NovoClienteNome!.Trim(), cmd.NovoClienteApt, null,
                cmd.NovoClienteTelefone, null, null, null));
            clienteId = cli.Id;
        }

        // 2. Produtos novos: cadastra produto + entrada de estoque (qtd = quantidade do item).
        //    A saida (passo 4) sai dessa mesma entrada -> saldo liquido 0 no balcao.
        var itensPedido = new List<CriarPedidoItemInput>(cmd.Itens.Count);
        foreach (var it in cmd.Itens)
        {
            var produtoId = it.ProdutoId;
            var ehNovo = (produtoId is null || produtoId == Guid.Empty) && it.NovoProduto;
            // Contrato: produto existente (ProdutoId) OU produto novo (NovoProduto + CategoriaId).
            // Sem este guard, a combinacao ProdutoId vazio + NovoProduto=false caía sem validacao
            // e o item virava avulso silenciosamente em vez de ser rejeitado.
            if (!ehNovo && (produtoId is null || produtoId == Guid.Empty))
                throw new UseCaseValidationException($"Item \"{it.Nome}\": selecione um produto existente ou marque como produto novo.");
            if (ehNovo)
            {
                if (it.CategoriaId is null || it.CategoriaId == Guid.Empty)
                    throw new UseCaseValidationException($"Item \"{it.Nome}\": selecione a categoria do produto novo.");
                if (it.Quantidade <= 0)
                    throw new UseCaseValidationException($"Item \"{it.Nome}\": quantidade deve ser maior que zero.");

                var prod = await cadastrarProdutoUC.ExecuteAsync(new CadastrarProdutoCommand(
                    EmpresaId: cmd.EmpresaId,
                    CategoriaId: it.CategoriaId.Value,
                    SubcategoriaId: null,
                    Nome: it.Nome.Trim(),
                    DescricaoBase: null,
                    Marca: null,
                    Tipo: TipoProduto.Fisico,
                    SkuBase: null,
                    CodigoBarras: null,
                    ControlaValidade: false,
                    Dimensoes: null,
                    CustoReferencia: it.CustoReferencia,
                    PrecoReferencia: it.PrecoUnitario > 0 ? it.PrecoUnitario : null,
                    MargemEstimada: null,
                    AtributosJson: null,
                    FotosJson: null,
                    Caracteristicas: null,
                    Embalagens: null,
                    Variacoes: null,
                    UsuarioId: cmd.CriadoPorUserId ?? Guid.Empty,
                    TipoEmbalagem: TipoEmbalagem.Avulso));
                produtoId = prod.ProdutoId;

                var custo = it.CustoReferencia ?? (it.PrecoUnitario > 0 ? it.PrecoUnitario : 0.01m);
                await registrarEntradaUC.ExecuteAsync(new RegistrarEntradaEstoqueCommand(
                    EmpresaId: cmd.EmpresaId,
                    ProdutoId: produtoId.Value,
                    ProdutoVariacaoId: null,
                    Quantidade: it.Quantidade,
                    CustoUnitario: custo,
                    PrecoVendaSugerido: it.PrecoUnitario > 0 ? it.PrecoUnitario : null,
                    DataEntrada: DateTime.UtcNow,
                    Natureza: NaturezaMovimentacaoEstoque.Compra,
                    CodigoInterno: null,
                    CodigoLote: null,
                    CodigoMarketplace: null,
                    VariacaoDescricao: null,
                    Cor: null,
                    Tamanho: null,
                    FornecedorNome: null,
                    Validade: null,
                    Observacoes: "Entrada inicial — produto cadastrado na venda balcao.",
                    // Passa DescricaoAnuncio (nome) de proposito: com vazio + gerador de IA
                    // registrado, RegistrarEntrada faria uma chamada de IA EXTERNA dentro da
                    // transacao (lenta, segura a transacao aberta). Evitamos isso no balcao.
                    DescricaoAnuncio: it.Nome.Trim(),
                    DocumentoReferencia: null,
                    DimensoesReais: null,
                    InstrucoesGeracaoDescricao: null,
                    LojaId: cmd.LojaId));
            }

            itensPedido.Add(new CriarPedidoItemInput(
                Nome: it.Nome.Trim(),
                Quantidade: it.Quantidade,
                PrecoUnitario: it.PrecoUnitario,
                ProdutoId: produtoId));
        }

        // 3. Cria o pedido (nasce Aguardando).
        var usaAvulso = clienteId is null || clienteId == Guid.Empty;
        var pedido = await criarPedidoUC.ExecuteAsync(new CriarPedidoCommand(
            EmpresaId: cmd.EmpresaId,
            LojaId: cmd.LojaId,
            ClienteId: usaAvulso ? null : clienteId,
            ClienteNomeAdHoc: usaAvulso ? cmd.ClienteNomeAdHoc : null,
            ClienteAptAdHoc: null,
            ClienteTelefoneAdHoc: null,
            Observacoes: cmd.Observacoes,
            Origem: origem,
            MobileOrderId: null,
            Itens: itensPedido,
            CriadoPorUserId: cmd.CriadoPorUserId,
            CriadoPorNome: cmd.CriadoPorNome,
            AgendadoParaEm: null));

        // 4. Finaliza: Aguardando -> Preparando -> Pronto (saida de estoque) -> Entregue.
        //    A state machine nao permite pular etapas; cada passo e idempotente e a saida
        //    (DescontarAsync) dispara uma vez ao chegar em Pronto.
        foreach (var status in PassosAteEntregue)
        {
            await atualizarStatusUC.ExecuteAsync(new AtualizarStatusPedidoCommand(
                cmd.EmpresaId, pedido.Id, status, cmd.CriadoPorUserId, cmd.CriadoPorNome, origem));
        }

        // 5. Pagamento (se pagou). Caixa reconcilia por agregacao + abre automatico.
        if (cmd.Pagou)
        {
            await registrarPagamentoUC.ExecuteAsync(new RegistrarPagamentoPedidoCommand(
                cmd.EmpresaId, pedido.Id, formaPagamento, pedido.Total, null,
                "Pagamento na venda balcao.", cmd.CriadoPorUserId, cmd.CriadoPorNome, origem));
        }

        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Venda balcao finalizada: pedido {Id}, total {Total}, pago={Pago} ({Forma}), itens={Itens}.",
            pedido.Id, pedido.Total, cmd.Pagou, cmd.Pagou ? formaPagamento : "-", itensPedido.Count);

        return new FinalizarVendaBalcaoResult(pedido.Id, cmd.Pagou, pedido.Total, cmd.Pagou ? formaPagamento : "");
    }
}
