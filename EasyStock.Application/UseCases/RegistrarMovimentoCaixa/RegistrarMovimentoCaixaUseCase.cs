using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;

namespace EasyStock.Application.UseCases.RegistrarMovimentoCaixa;

public sealed record RegistrarMovimentoCaixaCommand(
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(20)] string Tipo,        // "entrada" | "saida"
    decimal Valor,
    string? Descricao = null,
    Guid? LojaId = null,
    [property: MaxLength(20)] string? Metodo = null,
    [property: MaxLength(60)] string? Categoria = null,
    [property: MaxLength(120)] string? Referencia = null,
    DateTime? DataMovimento = null,
    Guid? RegistradoPorUserId = null,
    [property: MaxLength(120)] string? RegistradoPorNome = null,
    [property: MaxLength(20)] string? Origem = "web");

public class RegistrarMovimentoCaixaUseCase(
    ICaixaRepository repo,
    IUnitOfWork uow,
    ILogger<RegistrarMovimentoCaixaUseCase> logger)
{
    public async Task<MovimentoCaixaResult> ExecuteAsync(RegistrarMovimentoCaixaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var tipo = (cmd.Tipo ?? "").Trim().ToLowerInvariant();
        if (tipo != "entrada" && tipo != "saida")
            throw new UseCaseValidationException("Tipo deve ser 'entrada' ou 'saida'.");

        if (cmd.Valor <= 0)
            throw new UseCaseValidationException("Valor deve ser maior que zero.");

        var dataMov = cmd.DataMovimento ?? DateTime.UtcNow;   // timestamp do movimento (UTC, armazenado)
        var data = HorarioBrasil.DataOperacional(dataMov);    // dia operacional em Brasilia (alinha com o card; BUG-09)

        // Bloquear lançamento em dia já fechado (preserva integridade do snapshot).
        var fechamento = await repo.GetFechamentoDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);
        if (fechamento != null)
            throw new UseCaseValidationException("Caixa do dia já foi fechado. Lance em outra data ou faça estorno.");

        // FIN-003: saída interativa (não-mobile) exige rastro de auditoria e não pode estourar o
        // saldo do dia — caixa físico não fica negativo. O mobile promove fatos já registrados no
        // device (sync); não passa por estas guardas pra não bloquear lançamento legítimo.
        var origem = (cmd.Origem ?? "web").Trim().ToLowerInvariant();
        if (tipo == "saida" && origem != "mobile")
        {
            if (string.IsNullOrWhiteSpace(cmd.Metodo))
                throw new UseCaseValidationException("Saída exige método (dinheiro, pix, cartão, etc.).");
            if (string.IsNullOrWhiteSpace(cmd.Descricao))
                throw new UseCaseValidationException("Saída exige descrição (justificativa para auditoria).");

            var saldoAtual = await CalcularSaldoDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);
            if (cmd.Valor > saldoAtual)
            {
                var ptBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
                throw new UseCaseValidationException(
                    $"Saída de {cmd.Valor.ToString("C", ptBr)} é maior que o saldo disponível em caixa " +
                    $"({saldoAtual.ToString("C", ptBr)}). O caixa não pode ficar negativo.");
            }
        }

        var mov = MovimentoCaixa.Criar(cmd.EmpresaId, tipo, cmd.Valor, dataMov, cmd.LojaId);
        mov.Descricao = cmd.Descricao;
        mov.Metodo = cmd.Metodo;
        mov.Categoria = cmd.Categoria;
        mov.Referencia = cmd.Referencia;
        mov.RegistradoPorUserId = cmd.RegistradoPorUserId;
        mov.RegistradoPorNome = cmd.RegistradoPorNome;
        mov.Origem = cmd.Origem;

        await repo.AddMovimentoAsync(mov);
        await uow.CommitAsync();

        logger.LogInformation("Movimento de caixa {Id} ({Tipo} {Valor}) registrado.", mov.Id, tipo, cmd.Valor);
        return AbrirCaixaUseCase.Map(mov);
    }

    // Saldo esperado do dia (mesma fórmula do ObterCaixaDiaUseCase): saldo inicial (abertura)
    // + vendas + pagamentos de pedidos + entradas extras - saídas extras.
    private async Task<decimal> CalcularSaldoDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId)
    {
        var movs = (await repo.GetMovimentosDoDiaAsync(empresaId, data, lojaId)).ToList();
        var saldoInicial = movs.Where(m => m.Tipo == "abertura").Sum(m => m.Valor);
        var entradas = movs.Where(m => m.Tipo == "entrada").Sum(m => m.Valor);
        var saidas = movs.Where(m => m.Tipo == "saida").Sum(m => m.Valor);
        var vendas = await repo.GetTotalVendasDoDiaAsync(empresaId, data, lojaId);
        var pagamentos = await repo.GetTotalPagamentosPedidosDoDiaAsync(empresaId, data, lojaId);
        return saldoInicial + vendas + pagamentos + entradas - saidas;
    }
}
