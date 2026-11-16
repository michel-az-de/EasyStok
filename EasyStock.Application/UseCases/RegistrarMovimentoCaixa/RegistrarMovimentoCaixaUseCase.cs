using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Domain.Financeiro;

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
    [property: MaxLength(20)] string? Origem = "web",
    // #2319: só o lançamento manual do operador liga isto (o controller força, nunca vem do body). Pagamento de pedido,
    // webhook, estorno e sync do app seguem sem exigir caixa aberto.
    bool ExigirCaixaAberto = false);

public class RegistrarMovimentoCaixaUseCase(
    ICaixaRepository repo,
    IUnitOfWork uow,
    ObterCaixaDiaUseCase obterCaixaDia,
    ILogger<RegistrarMovimentoCaixaUseCase> logger,
    ILojaRepository? lojas = null)
{
    public const string CaixaAbertoMensagem = "Abra o caixa antes de lançar.";

    private static readonly TimeSpan ToleranciaDeRelogio = TimeSpan.FromMinutes(5);

    private const int TamanhoCategoria = 60;
    private const int TamanhoReferencia = 120;

    private static string? Cortar(string? texto, int max) => texto is { } t && t.Length > max ? t[..max] : texto;

    private static void ExigirTamanho(string? texto, int max, string campo)
    {
        if (texto is { } t && t.Length > max)
            throw new UseCaseValidationException($"{campo} deve ter no máximo {max} caracteres (veio com {t.Length}).");
    }

    public async Task<MovimentoCaixaResult> ExecuteAsync(RegistrarMovimentoCaixaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        // #2481: a loja vem do corpo. De outra empresa ou inexistente vira 400, como na abertura (#2343), não
        // movimento gravado com loja alheia nem FK (500) no insert.
        if (cmd.LojaId is { } lojaId && lojas is not null && await lojas.GetByIdAsync(cmd.EmpresaId, lojaId) is null)
            throw new UseCaseValidationException("Loja nao encontrada nesta empresa.");

        var tipo = (cmd.Tipo ?? "").Trim().ToLowerInvariant();
        if (tipo != "entrada" && tipo != "saida")
            throw new UseCaseValidationException("Tipo deve ser 'entrada' ou 'saida'.");

        if (cmd.Valor <= 0)
            throw new UseCaseValidationException("Valor deve ser maior que zero.");
        var origem = (cmd.Origem ?? "web").Trim().ToLowerInvariant();
        // O mobile envia fato já gravado no aparelho: arredonda em vez de recusar, para a mutação não virar rejeição permanente.
        if (origem == "mobile") cmd = cmd with { Valor = decimal.Round(cmd.Valor, 2, MidpointRounding.AwayFromZero) };
        // #1776 (item 12): texto maior que a coluna estourava no banco (500). Quem digita recebe 400 claro; o app corta,
        // como arredonda o valor, para o fato já gravado no aparelho não virar rejeição permanente.
        cmd = origem == "mobile"
            ? cmd with { Categoria = Cortar(cmd.Categoria, TamanhoCategoria), Referencia = Cortar(cmd.Referencia, TamanhoReferencia) }
            : cmd;
        ExigirTamanho(cmd.Categoria, TamanhoCategoria, "Categoria");
        ExigirTamanho(cmd.Referencia, TamanhoReferencia, "Referência");
        ValidacaoCaixa.ExigirValorMonetario(cmd.Valor, "Valor");

        var dataMov = DataUtc.ParaUtc(cmd.DataMovimento ?? DateTime.UtcNow);
        var data = HorarioBrasil.DataOperacional(dataMov);    // dia operacional em Brasilia (alinha com o card; BUG-09)

        // O mobile promove fatos já gravados no aparelho: não recusa forma antiga, só normaliza a conhecida.
        cmd = cmd with { Metodo = origem == "mobile"
            ? MetodosPagamentoCaixa.Normalizar(cmd.Metodo) ?? cmd.Metodo
            : ValidacaoCaixa.NormalizarMetodo(cmd.Metodo) };

        // #1615: lançamento retroativo é uso legítimo; no futuro ele some do caixa de hoje e cai num
        // dia que ainda não abriu. O mobile fica de fora: a hora vem do relógio do aparelho.
        if (origem != "mobile" && dataMov > DateTime.UtcNow.Add(ToleranciaDeRelogio))
            throw new UseCaseValidationException("Data do lançamento não pode estar no futuro.");

        if (cmd.ExigirCaixaAberto)
        {
            var abertura = await repo.GetAberturaPendenteAsync(cmd.EmpresaId, cmd.LojaId);
            // #2440: a abertura da empresa (sem loja) vale para a loja, como o fechamento (#1562) e o pix (#2341).
            // Sem lojaId o repositório não filtra por loja: só vale a abertura gravada sem loja.
            if (abertura is null && cmd.LojaId.HasValue)
            {
                var daEmpresa = await repo.GetAberturaPendenteAsync(cmd.EmpresaId);
                if (daEmpresa?.LojaId == null) abertura = daEmpresa;
            }
            if (abertura is null) throw new UseCaseValidationException(CaixaAbertoMensagem);
            // ADR-0034: o fechamento cobre o dia da abertura; movimentos de hoje ficam no caixa de hoje.
            var diaDaAbertura = HorarioBrasil.DataOperacional(abertura.DataMovimento);
            if (data != diaDaAbertura && data != HorarioBrasil.Hoje())
                throw new UseCaseValidationException(
                    $"O lançamento manual deve usar o dia da abertura ({diaDaAbertura:dd/MM/yyyy}) ou hoje. Revise a data informada.");
        }

        const string diaFechado = "Caixa do dia já foi fechado. Lance em outra data ou faça estorno.";

        // O mobile promove fatos já registrados no device (sync): só a guarda de dia fechado, sem as de saída.
        // FIN-003: saída interativa exige rastro de auditoria e não pode estourar o saldo do dia — caixa físico não
        // fica negativo.
        var saida = origem != "mobile" && tipo == "saida";
        if (saida && string.IsNullOrWhiteSpace(cmd.Metodo))
            throw new UseCaseValidationException("Saída exige método (dinheiro, pix, cartão, etc.).");
        if (saida && string.IsNullOrWhiteSpace(cmd.Descricao))
            throw new UseCaseValidationException("Saída exige descrição (justificativa para auditoria).");

        // #1547: duas saídas simultâneas liam o mesmo saldo e as duas passavam do teto. #2613: o fechamento toma a
        // mesma trava; a guarda de dia fechado só vale lida atrás dela, senão o lançamento cai depois do retrato.
        // #2629: vale também para o sync do mobile.
        return await uow.ExecuteInTransactionSemRetryAsync(async ct =>
        {
            await repo.TravarSaidasAsync(cmd.EmpresaId, cmd.LojaId, ct);
            await CaixaFechadoGuard.ExigirDiaAbertoAsync(repo, cmd.EmpresaId, dataMov, cmd.LojaId, diaFechado);
            if (saida) await ConferirSaldoParaSaidaAsync(cmd, data);
            return await GravarAsync(cmd, tipo, origem, dataMov);
        });
    }

    private async Task ConferirSaldoParaSaidaAsync(RegistrarMovimentoCaixaCommand cmd, DateOnly data)
    {
        var dia = await ObterCaixaDoDiaAsync(cmd.EmpresaId, data, cmd.LojaId);
        var ptBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        // #1521: saída em dinheiro (sangria, despesa paga da gaveta) sai da gaveta, e nela só há
        // dinheiro. O saldo esperado soma Pix e cartão: com R$100 na gaveta e R$400 em Pix,
        // uma sangria de R$450 passava e a gaveta "ficava negativa".
        if (GavetaCaixa.EhDinheiro(cmd.Metodo))
        {
            TetoDaGaveta.ExigirSaidaEmDinheiroCabe(dia, cmd.Valor);
        }
        else if (cmd.Valor > dia.SaldoEsperado)
        {
            throw new UseCaseValidationException(
                $"Saída de {cmd.Valor.ToString("C", ptBr)} é maior que o saldo disponível em caixa " +
                $"({dia.SaldoEsperado.ToString("C", ptBr)}). O caixa não pode ficar negativo.");
        }
    }

    private async Task<MovimentoCaixaResult> GravarAsync(RegistrarMovimentoCaixaCommand cmd, string tipo, string origem, DateTime dataMov)
    {
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
