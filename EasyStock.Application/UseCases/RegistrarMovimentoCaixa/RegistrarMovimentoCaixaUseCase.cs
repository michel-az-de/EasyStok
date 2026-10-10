using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.ObterCaixaDia;

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
    ObterCaixaDiaUseCase obterCaixaDia,
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

            var dia = await ObterCaixaDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);
            var ptBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            // #1521: saída em dinheiro (sangria, despesa paga da gaveta) sai da gaveta, e nela só há
            // dinheiro. O saldo esperado soma Pix e cartão: com R$100 na gaveta e R$400 em Pix,
            // uma sangria de R$450 passava e a gaveta "ficava negativa".
            if (GavetaCaixa.EhDinheiro(cmd.Metodo))
            {
                var teto = GavetaCaixa.TetoSaidaEmDinheiro(dia);
                if (cmd.Valor > teto)
                    throw new UseCaseValidationException(
                        $"Saída de {cmd.Valor.ToString("C", ptBr)} em dinheiro é maior que o dinheiro na gaveta " +
                        $"({teto.ToString("C", ptBr)}). Pix e cartão não estão na gaveta.");
            }
            else if (cmd.Valor > dia.SaldoEsperado)
            {
                throw new UseCaseValidationException(
                    $"Saída de {cmd.Valor.ToString("C", ptBr)} é maior que o saldo disponível em caixa " +
                    $"({dia.SaldoEsperado.ToString("C", ptBr)}). O caixa não pode ficar negativo.");
            }
        }

        var mov = MovimentoCaixa.Criar(cmd.EmpresaId, tipo, cmd.Valor, dataMov, cmd.LojaId);
        mov.Descricao = cmd.Descricao;
        mov.Metodo = cmd.Metodo;
        mov.Categoria = cmd.Categoria;
        mov.Referencia = cmd.Referencia;
        mov.RegistradoPorUserId = cmd.RegistradoPorUserId;
        mov.RegistradoPorNome = cmd.RegistradoPorNome;
        mov.Origem = origem;   // valor normalizado (trim+lower), nao o cru do body

        await repo.AddMovimentoAsync(mov);
        await uow.CommitAsync();

        logger.LogInformation("Movimento de caixa {Id} ({Tipo} {Valor}) registrado.", mov.Id, tipo, cmd.Valor);
        return AbrirCaixaUseCase.Map(mov);
    }

    // #686/BUG-004: a validacao anti-negativo precisa usar EXATAMENTE o mesmo saldo exibido
    // no card "Saldo esperado" e no modal de fechamento — incluindo a resolucao cross-day de
    // sessao aberta em dia anterior (#596). Reusa o ObterCaixaDiaUseCase como FONTE UNICA em
    // vez de recalcular. Antes este metodo agregava so a janela civil de hoje e divergia (card
    // R$4.514 vs validacao R$0), bloqueando saidas legitimas em sessao que atravessa a meia-noite.
    private Task<CaixaDiaResult> ObterCaixaDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId) =>
        obterCaixaDia.ExecuteAsync(new ObterCaixaDiaQuery(empresaId, data, lojaId));
}
