using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.ObterCaixaDia;

namespace EasyStock.Application.UseCases.EstornarMovimentoCaixa;

public sealed record EstornarMovimentoCaixaCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid Id,
    string? Motivo = null,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(20)] string? Origem = "web");

public class EstornarMovimentoCaixaUseCase(
    ICaixaRepository repo,
    IUnitOfWork uow,
    ObterCaixaDiaUseCase obterCaixaDia,
    ILogger<EstornarMovimentoCaixaUseCase> logger)
{
    public async Task<MovimentoCaixaResult?> ExecuteAsync(EstornarMovimentoCaixaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var mov = await repo.GetMovimentoAsync(cmd.EmpresaId, cmd.Id);
        if (mov == null) return null;
        if (mov.EstornadoEm != null) return AbrirCaixaUseCase.Map(mov);

        if (mov.Origem == PedidoEstornoManual.OrigemCaixa)
            throw new UseCaseValidationException("Esta saída confirma uma devolução ao cliente e não pode ser desfeita pelo Caixa.");

        // Abertura/fechamento definem a sessao do caixa; estornar quebraria saldo e reabertura.
        if (mov.Tipo is not ("entrada" or "saida"))
            throw new UseCaseValidationException("Somente movimentos de entrada ou saída podem ser estornados.");

        // Bloquear estorno se o dia já foi fechado.
        await CaixaFechadoGuard.ExigirDiaAbertoAsync(repo, mov.EmpresaId, mov.DataMovimento, mov.LojaId,
            "Não é possível estornar movimento de dia já fechado.");

        // #1623: estornar uma entrada tira dinheiro do caixa, como uma sangria. Suprimento estornado
        // depois de uma sangria deixava a gaveta negativa. Mesma guarda e mesma trava da saída
        // (RegistrarMovimentoCaixaUseCase, #1521/#1547); o PWA promove o que já fez no aparelho e
        // segue sem a guarda, como a saída mobile.
        var origem = (cmd.Origem ?? "web").Trim().ToLowerInvariant();
        if (mov.Tipo == "entrada" && origem != "mobile")
        {
            return await uow.ExecuteInTransactionSemRetryAsync(async ct =>
            {
                await repo.TravarSaidasAsync(mov.EmpresaId, mov.LojaId, ct);
                await ConferirCaixaParaEstornoAsync(mov);
                return await EstornarAsync(mov, cmd);
            });
        }

        return await EstornarAsync(mov, cmd);
    }

    private async Task<MovimentoCaixaResult> EstornarAsync(MovimentoCaixa mov, EstornarMovimentoCaixaCommand cmd)
    {
        mov.Estornar(cmd.UsuarioId, cmd.UsuarioNome, cmd.Motivo);
        await repo.UpdateMovimentoAsync(mov);
        await uow.CommitAsync();

        logger.LogInformation("Movimento {Id} estornado (motivo={Motivo}).", mov.Id, cmd.Motivo ?? "—");
        return AbrirCaixaUseCase.Map(mov);
    }

    private async Task ConferirCaixaParaEstornoAsync(MovimentoCaixa mov)
    {
        var dia = await CaixaDoMovimentoAsync(mov);
        // O caixa só lista lançamento não estornado: ausente aqui, outra tela estornou entre a leitura
        // e a trava (duplo clique). Não estorna de novo nem fala de gaveta.
        if (dia.Movimentos.All(m => m.Id != mov.Id))
            throw new UseCaseValidationException("Este lançamento já foi estornado. Atualize a tela.");
        var ptBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        // Entrada sem método é do caixa físico (GavetaCaixa); em dinheiro, o teto é o mesmo da sangria.
        if (string.IsNullOrWhiteSpace(mov.Metodo) || GavetaCaixa.EhDinheiro(mov.Metodo))
        {
            var falta = mov.Valor - GavetaCaixa.TetoSaidaEmDinheiro(dia);
            if (falta > 0)
                throw new UseCaseValidationException(
                    $"A gaveta ficaria negativa em {falta.ToString("C", ptBr)}. Estorne antes a saída que levou esse dinheiro.");
        }
        else if (mov.Valor > dia.SaldoEsperado)
        {
            throw new UseCaseValidationException(
                $"O caixa ficaria negativo em {(mov.Valor - dia.SaldoEsperado).ToString("C", ptBr)}. Estorne antes a saída que usou esse valor.");
        }
    }

    // O caixa que contém o movimento: a sessão de hoje (inclusive a esquecida aberta desde outro dia,
    // #596) ou, se ele não está nela, o dia operacional do próprio movimento.
    private async Task<CaixaDiaResult> CaixaDoMovimentoAsync(MovimentoCaixa mov)
    {
        var hoje = await obterCaixaDia.ExecuteAsync(new ObterCaixaDiaQuery(mov.EmpresaId, HorarioBrasil.Hoje(), mov.LojaId));
        if (hoje.Movimentos.Any(m => m.Id == mov.Id)) return hoje;
        return await obterCaixaDia.ExecuteAsync(new ObterCaixaDiaQuery(
            mov.EmpresaId, HorarioBrasil.DataOperacional(mov.DataMovimento), mov.LojaId));
    }
}
