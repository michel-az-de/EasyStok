using EasyStock.Application.UseCases.Faturas.Common;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Faturas.EmitirFatura;

/// <summary>
/// Comando de emissao de fatura — usado por todas as origens
/// (Assinatura via Job, Pedido via service interno, Avulsa via admin).
/// </summary>
/// <param name="IdempotentePorOrigem">Idempotencia: se ja existe fatura ATIVA com este (Origem, OrigemRefId), retorna a existente.</param>
public sealed record EmitirFaturaCommand(
    Guid EmpresaId,
    DadosFaturado DadosFaturado,
    DadosEmissor DadosEmissor,
    OrigemFatura Origem,
    DateTime DataVencimento,
    IReadOnlyList<FaturaItemInput> Itens,
    Guid? ClienteId = null,
    Guid? OrigemRefId = null,
    string? Observacoes = null,
    string Moeda = "BRL",
    DadosFiscais? DadosFiscais = null,
    DateTime? DataEmissao = null,
    bool IdempotentePorOrigem = true,
    Guid? UsuarioId = null,
    string? UsuarioNome = null,
    string? OrigemRegistro = "api"
);

public sealed record EmitirFaturaResult(Guid FaturaId, string Numero, decimal Total);

public class EmitirFaturaUseCase(
    IFaturaRepository repo,
    IFaturaNumeradorService numerador,
    IUnitOfWork uow,
    ILogger<EmitirFaturaUseCase> logger)
{
    public async Task<EmitirFaturaResult> ExecuteAsync(EmitirFaturaCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.Itens is null || cmd.Itens.Count == 0)
            throw new UseCaseValidationException("Fatura deve ter ao menos um item.");
        if (cmd.DadosFaturado is null)
            throw new UseCaseValidationException("DadosFaturado e obrigatorio.");
        if (cmd.DadosEmissor is null)
            throw new UseCaseValidationException("DadosEmissor e obrigatorio.");

        // Idempotencia por origem (evita duplicar fatura de mesma assinatura/pedido)
        if (cmd.IdempotentePorOrigem && cmd.OrigemRefId.HasValue && cmd.Origem != OrigemFatura.Avulsa)
        {
            var existente = await repo.GetByOrigemAsync(cmd.EmpresaId, cmd.Origem, cmd.OrigemRefId.Value, ct);
            if (existente is not null && existente.Status != StatusFatura.Cancelada)
            {
                logger.LogInformation(
                    "Fatura ja existe para origem {Origem}/{OrigemRefId} — reutilizando {FaturaId}",
                    cmd.Origem, cmd.OrigemRefId, existente.Id);
                return new EmitirFaturaResult(existente.Id, existente.Numero, existente.Total);
            }
        }

        var dataEmissao = DataUtc.ParaUtcOpcional(cmd.DataEmissao) ?? DateTime.UtcNow;
        var numero = await numerador.GerarAsync(cmd.EmpresaId, dataEmissao, ct);

        var fatura = Fatura.Criar(
            empresaId: cmd.EmpresaId,
            numero: numero,
            dadosFaturado: cmd.DadosFaturado,
            dadosEmissor: cmd.DadosEmissor,
            origem: cmd.Origem,
            dataEmissao: dataEmissao,
            dataVencimento: DataUtc.ParaUtc(cmd.DataVencimento),
            clienteId: cmd.ClienteId,
            origemRefId: cmd.OrigemRefId,
            observacoes: cmd.Observacoes,
            moeda: cmd.Moeda
        );
        fatura.DadosFiscais = cmd.DadosFiscais;

        foreach (var item in cmd.Itens)
        {
            fatura.AdicionarItem(item.Descricao, item.Quantidade, item.PrecoUnitario, item.Tipo);
        }
        fatura.Emitir();

        // Audit
        fatura.Eventos.Add(FaturaEvento.Criar(
            fatura.Id, TipoEventoFatura.Criada,
            usuarioId: cmd.UsuarioId, usuarioNome: cmd.UsuarioNome, origem: cmd.OrigemRegistro,
            metadadosJson: $"{{\"origem\":\"{cmd.Origem}\",\"itens\":{cmd.Itens.Count}}}"
        ));
        fatura.Eventos.Add(FaturaEvento.Criar(
            fatura.Id, TipoEventoFatura.Emitida,
            usuarioId: cmd.UsuarioId, usuarioNome: cmd.UsuarioNome, origem: cmd.OrigemRegistro,
            valorDepois: $"Total={fatura.Total:F2} {fatura.Moeda}; Vencimento={fatura.DataVencimento:yyyy-MM-dd}"
        ));

        await repo.AddAsync(fatura, ct);
        await uow.CommitAsync();

        logger.LogInformation(
            "Fatura emitida. EmpresaId={EmpresaId} Numero={Numero} Total={Total} Origem={Origem}",
            cmd.EmpresaId, fatura.Numero, fatura.Total, cmd.Origem);

        return new EmitirFaturaResult(fatura.Id, fatura.Numero, fatura.Total);
    }
}
