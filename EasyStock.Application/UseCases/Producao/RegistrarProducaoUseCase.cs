using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.FinalizarLote;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;

namespace EasyStock.Application.UseCases.Producao;

/// <summary>Item produzido: a porcao e a unidade de estoque (RN-44 a RN-47).</summary>
public sealed record RegistrarProducaoItemInput(
    [property: Required] Guid ProdutoId,
    int Porcoes,
    int? PesoPorPorcaoG,
    int ValidadeDias,
    decimal? CustoUnitario,
    // M2.2 (#1491, RN-45): peso real produzido; a sobra é o que passa das porções.
    int? PesoRealG = null);

public sealed record RegistrarProducaoCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId,
    DateTime? DataProducao,
    IReadOnlyList<RegistrarProducaoItemInput>? Itens,
    string? Observacao = null,
    Guid? OperadorUserId = null,
    [property: MaxLength(120)] string? OperadorNome = null);

public sealed record RegistrarProducaoItemResult(
    Guid ProdutoId, Guid ItemEstoqueId, Guid MovimentacaoId, int Porcoes, DateTime ValidadeEm, int? SobraG = null);

public sealed record RegistrarProducaoResult(
    Guid LoteId,
    string CodigoLote,
    int TotalEtiquetas,
    IReadOnlyList<RegistrarProducaoItemResult> Itens);

/// <summary>
/// S23 (#1137): registra uma producao em porcoes numa TRANSACAO UNICA:
/// cria o Lote (LoteItem.Quantidade = porcoes), finaliza (etiquetas) e da a entrada de
/// estoque por item (Natureza=Producao, CodigoLote = Lote.Codigo, Validade = ExpiraEm).
///
/// Compoe os use cases existentes no padrao de FinalizarVendaBalcaoUseCase: cada um chama
/// CommitAsync (= SaveChanges dentro da transacao aberta) e o commit real e o da transacao.
/// SemRetry de proposito: reexecutar geraria Guids e codigos de lote novos.
///
/// Ordem: o lote e FINALIZADO antes das entradas. Com o lote ainda em producao, a
/// RegistrarEntradaEstoque (vinculo P5 por codigo) acrescentaria outro LoteItem e as
/// etiquetas dobrariam; finalizado, a entrada so referencia o lote pelo codigo.
/// </summary>
public class RegistrarProducaoUseCase(
    CriarLoteUseCase criarLoteUC,
    FinalizarLoteUseCase finalizarLoteUC,
    RegistrarEntradaEstoqueUseCase registrarEntradaUC,
    IProdutoRepository produtoRepository,
    IUnitOfWork uow,
    ILogger<RegistrarProducaoUseCase> logger)
{
    public async Task<RegistrarProducaoResult> ExecuteAsync(RegistrarProducaoCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.Itens is null || cmd.Itens.Count == 0)
            throw new UseCaseValidationException("Informe pelo menos 1 item produzido.");

        foreach (var it in cmd.Itens)
        {
            if (it.ProdutoId == Guid.Empty)
                throw new UseCaseValidationException("Item produzido precisa estar vinculado a um produto.");
            if (it.Porcoes <= 0)
                throw new UseCaseValidationException("Porcoes deve ser maior que zero.");
            if (it.ValidadeDias <= 0)
                throw new UseCaseValidationException("Validade em dias deve ser maior que zero.");
            if (it.PesoPorPorcaoG is <= 0)
                throw new UseCaseValidationException("Peso por porcao deve ser maior que zero.");
            if (it.CustoUnitario is < 0)
                throw new UseCaseValidationException("Custo unitario nao pode ser negativo.");
            if (it.PesoRealG is <= 0)
                throw new UseCaseValidationException("Peso real deve ser maior que zero.");
            if (Sobra(it) is < 0)
                throw new UseCaseValidationException(
                    "O peso real nao fecha as porcoes: confira o peso ou o numero de porcoes.");
        }

        // Antecipa a regra RDC 727 do FinalizarLote: Embalado sem peso e rejeitado antes de gravar.
        var produtoIds = cmd.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var tipos = await produtoRepository.GetTipoEmbalagemMapAsync(cmd.EmpresaId, produtoIds);
        var embaladoSemPeso = cmd.Itens.Any(i => i.PesoPorPorcaoG is null
            && tipos.TryGetValue(i.ProdutoId, out var t) && t == TipoEmbalagem.Embalado);
        if (embaladoSemPeso)
            throw new UseCaseValidationException(
                "Informe o peso por porcao dos produtos embalados (RDC 727/2022).");

        var produtos = new Dictionary<Guid, Produto>(produtoIds.Count);
        foreach (var id in produtoIds)
        {
            var p = await produtoRepository.GetByIdAsync(cmd.EmpresaId, id)
                ?? throw new UseCaseValidationException("Produto nao encontrado.");
            produtos[id] = p;
        }

        return await uow.ExecuteInTransactionSemRetryAsync(
            token => ExecutarNaTransacaoAsync(cmd, produtos, token), ct);
    }

    private async Task<RegistrarProducaoResult> ExecutarNaTransacaoAsync(
        RegistrarProducaoCommand cmd, IReadOnlyDictionary<Guid, Produto> produtos, CancellationToken ct)
    {
        var itens = cmd.Itens!;
        var dataProducao = cmd.DataProducao ?? DateTime.UtcNow;

        var lote = await criarLoteUC.ExecuteAsync(new CriarLoteCommand(
            EmpresaId: cmd.EmpresaId,
            LojaId: cmd.LojaId,
            DataProducao: dataProducao,
            OperadorUserId: cmd.OperadorUserId,
            OperadorNome: cmd.OperadorNome,
            Observacoes: cmd.Observacao,
            Origem: "producao",
            Itens: itens.Select(i => new CriarLoteItemInput(
                Nome: Truncar(produtos[i.ProdutoId].Nome, 150),
                Quantidade: i.Porcoes,
                ProdutoId: i.ProdutoId,
                Unidade: "porcao",
                PesoG: i.PesoPorPorcaoG,
                ValidadeDias: i.ValidadeDias,
                PesoRealG: i.PesoRealG)).ToList()));

        var finalizado = await finalizarLoteUC.ExecuteAsync(new FinalizarLoteCommand(cmd.EmpresaId, lote.Id))
            ?? throw new InvalidOperationException($"Lote {lote.Id} nao encontrado dentro da transacao.");

        var resultados = new List<RegistrarProducaoItemResult>(itens.Count);
        foreach (var it in itens)
        {
            ct.ThrowIfCancellationRequested();
            var produto = produtos[it.ProdutoId];
            var validadeEm = dataProducao.AddDays(it.ValidadeDias);
            var custo = it.CustoUnitario ?? (decimal?)produto.CustoReferencia ?? 0m;

            var entrada = await registrarEntradaUC.ExecuteAsync(new RegistrarEntradaEstoqueCommand(
                EmpresaId: cmd.EmpresaId,
                ProdutoId: it.ProdutoId,
                ProdutoVariacaoId: null,
                Quantidade: it.Porcoes,
                CustoUnitario: custo,
                PrecoVendaSugerido: null,
                DataEntrada: dataProducao,
                Natureza: NaturezaMovimentacaoEstoque.Producao,
                CodigoInterno: null,
                CodigoLote: finalizado.Codigo,
                CodigoMarketplace: null,
                VariacaoDescricao: null,
                Cor: null,
                Tamanho: null,
                FornecedorNome: null,
                Validade: validadeEm,
                Observacoes: cmd.Observacao,
                DescricaoAnuncio: null,
                DocumentoReferencia: finalizado.Codigo,
                DimensoesReais: null,
                InstrucoesGeracaoDescricao: null,
                LojaId: cmd.LojaId));

            resultados.Add(new RegistrarProducaoItemResult(
                it.ProdutoId, entrada.ItemEstoqueId, entrada.MovimentacaoId, it.Porcoes, validadeEm, Sobra(it)));
        }

        logger.LogInformation("Producao registrada: lote {Codigo} com {Itens} item(ns) e {Etiquetas} etiqueta(s).",
            finalizado.Codigo, resultados.Count, finalizado.TotalUnidades);

        return new RegistrarProducaoResult(finalizado.Id, finalizado.Codigo, finalizado.TotalUnidades, resultados);
    }

    private static string Truncar(string s, int max) => s.Length > max ? s[..max] : s;

    // US-061: 1.000 g em 2 porções de 500 g sobra 0; 1.144 g sobra 144 g. Sem os dois pesos, null.
    private static int? Sobra(RegistrarProducaoItemInput it) =>
        it.PesoRealG is { } real && it.PesoPorPorcaoG is { } porcao ? real - it.Porcoes * porcao : null;
}
