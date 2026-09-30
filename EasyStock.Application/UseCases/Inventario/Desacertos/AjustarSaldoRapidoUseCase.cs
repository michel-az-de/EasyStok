using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Inventario.Desacertos;

public sealed record AjustarSaldoRapidoInput(
    Guid EmpresaId,
    Guid UsuarioId,
    Guid ProdutoId,
    Guid? LojaId,
    decimal QuantidadeContada,
    string Motivo);

public sealed record AjustarSaldoRapidoResult(
    Guid ProdutoId,
    Guid AjusteInventarioId,
    Guid ContagemId,
    decimal QuantidadeAtual,
    decimal QuantidadeDescoberta);

/// <summary>
/// Ajuste rápido de saldo (S22, UC-09): a dona conta o produto e o sistema acerta, sem abrir uma sessão de
/// contagem completa. Reusa o caminho da contagem: uma <see cref="Contagem"/> de um produto nasce aplicada,
/// cada lote passa por <see cref="ItemEstoque.AplicarAjusteContagem"/> (zera o descoberto e pisa em 0, #772)
/// e grava <see cref="AjusteInventario"/> + <see cref="MovimentacaoEstoque"/> de ajuste por lote alterado.
/// O alerta de desacerto fecha por construção; publica <c>estoque.desacerto_resolvido</c> depois do commit.
/// </summary>
public sealed class AjustarSaldoRapidoUseCase(
    IItemEstoqueRepository itens,
    IContagemRepository contagens,
    IMovimentacaoEstoqueRepository movimentacoes,
    IUnitOfWork unitOfWork,
    IOperacaoEventPublisher operacaoEventos,
    TimeProvider relogio)
{
    public const int MotivoMaximo = 500;

    public async Task<AjustarSaldoRapidoResult> ExecuteAsync(AjustarSaldoRapidoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.UsuarioId, "UsuarioId");
        UseCaseGuards.EnsureNotEmpty(input.ProdutoId, "ProdutoId");
        if (input.QuantidadeContada < 0)
            throw new UseCaseValidationException("A quantidade contada não pode ser negativa.");
        var motivo = input.Motivo?.Trim();
        if (string.IsNullOrEmpty(motivo))
            throw new UseCaseValidationException("Informe o motivo do ajuste.");
        if (motivo.Length > MotivoMaximo)
            throw new UseCaseValidationException($"O motivo aceita até {MotivoMaximo} caracteres.");
        UseCaseGuards.EnsureSemTagsHtml(motivo, "Motivo");

        var lotes = (await itens.GetLotesParaAjusteAsync(input.EmpresaId, input.ProdutoId, input.LojaId, ct))
            .OrderBy(l => l.EntradaEm).ThenBy(l => l.CriadoEm).ToList();
        if (lotes.Count == 0)
            throw new UseCaseValidationException("Produto sem lote de estoque: registre uma entrada em vez de ajustar.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        var tinhaDescoberto = lotes.Any(l => l.QuantidadeDescoberta.Value > 0);
        var alvos = DistribuirContagem(lotes, input.QuantidadeContada);
        var descricao = $"Ajuste rápido: {motivo}";

        var contagem = Contagem.Criar(
            input.EmpresaId,
            input.LojaId is null ? EscopoContagem.Todos : EscopoContagem.Loja,
            input.LojaId,
            ModoContagem.Visivel,
            EstrategiaLoteContagem.Guiado,
            input.UsuarioId,
            descricao);
        contagem.Iniciar(agora);

        var ajuste = AjusteInventario.Criar(input.EmpresaId, contagem.Id, input.UsuarioId);
        var movs = new List<MovimentacaoEstoque>();

        for (var i = 0; i < lotes.Count; i++)
        {
            var lote = lotes[i];
            var antes = lote.QuantidadeAtual;
            var contado = Quantidade.From(alvos[i]);

            var item = new ItemContagem
            {
                Id = Guid.NewGuid(),
                EmpresaId = input.EmpresaId,
                ContagemId = contagem.Id,
                ProdutoId = lote.ProdutoId,
                ItemEstoqueId = lote.Id,
            };
            item.Contar(contado, antes, lote.CustoUnitario, input.UsuarioId, agora);
            contagem.Itens.Add(item);

            // Mesmo método da contagem física: zera o descoberto e nunca deixa saldo negativo (#772).
            lote.AplicarAjusteContagem(contado, antes, agora, agora);

            var delta = lote.QuantidadeAtual.Value - antes.Value;
            if (delta == 0m) continue;

            ajuste.AdicionarLinha(lote.Id, lote.ProdutoId, antes, lote.QuantidadeAtual, lote.CustoUnitario,
                delta > 0 ? TipoAjusteLinha.Sobra : TipoAjusteLinha.Falta);
            movs.Add(MovimentacaoEstoque.CriarAjusteContagem(
                Guid.NewGuid(), input.EmpresaId, lote, delta, lote.CustoUnitario, agora, contagem.Id, descricao, agora));
        }

        contagem.Finalizar(agora);
        contagem.Aplicar(input.UsuarioId, agora);

        await contagens.AddAsync(contagem);
        await contagens.AddAjusteAsync(ajuste, ct);
        await itens.UpdateRangeAsync(lotes);
        if (movs.Count > 0)
            await movimentacoes.InsertRangeAsync(movs);
        await unitOfWork.CommitAsync();

        var atual = lotes.Sum(l => l.QuantidadeAtual.Value);
        var descoberto = lotes.Sum(l => l.QuantidadeDescoberta.Value);
        if (tinhaDescoberto && descoberto == 0m)
            await operacaoEventos.PublicarAsync(EventosOperacao.EstoqueDesacertoResolvido, input.EmpresaId,
                new EstoqueDesacertoResolvidoOperacao(input.ProdutoId, atual), ct);

        return new AjustarSaldoRapidoResult(input.ProdutoId, ajuste.Id, contagem.Id, atual, descoberto);
    }

    /// <summary>
    /// Reparte a contagem total entre os lotes (do mais antigo ao mais novo): os antigos ficam com o saldo que
    /// já têm, até onde a contagem alcança, e o lote mais novo recebe a diferença. Soma = contagem, nada negativo.
    /// </summary>
    internal static decimal[] DistribuirContagem(IReadOnlyList<ItemEstoque> lotes, decimal contada)
    {
        var alvos = new decimal[lotes.Count];
        var restante = contada;
        for (var i = 0; i < lotes.Count - 1; i++)
        {
            alvos[i] = Math.Min(lotes[i].QuantidadeAtual.Value, restante);
            restante -= alvos[i];
        }
        alvos[^1] = restante;
        return alvos;
    }
}
