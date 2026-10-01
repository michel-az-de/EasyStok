using EasyStock.Application.Ports.Output.Persistence.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <param name="Nota">Texto curto digitado na hora; cortado em <see cref="MontarPedidoImpressoUseCase.NotaTamanhoMaximo"/>.</param>
public sealed record MontarPedidoImpressoInput(Guid EmpresaId, Guid PedidoId, string? Nota = null);

/// <summary>
/// Monta o <see cref="PedidoImpressoDto"/> (S49). Agendado: janela da vaga ativa, senão o horário agendado;
/// pronto até = início − <see cref="MinutosProntoAntesDaJanela"/>. Imediato: pronto = último pagamento (ou
/// criação) + tempo de preparo da loja; saída = pronto + <see cref="MinutosProntoAntesDaJanela"/>.
/// Devolve <c>null</c> para pedido inexistente ou de outra empresa.
/// </summary>
public sealed class MontarPedidoImpressoUseCase(IPedidoImpressoQueries queries, TimeProvider relogio)
{
    /// <summary>Folga entre o pronto e a entrega. Fixa até a S50 torná-la configurável por loja.</summary>
    public const int MinutosProntoAntesDaJanela = 30;
    public const int NotaTamanhoMaximo = 80;

    public async Task<PedidoImpressoDto?> ExecuteAsync(MontarPedidoImpressoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");

        var p = await queries.ObterAsync(input.EmpresaId, input.PedidoId, ct);
        if (p is null) return null;

        var c = p.Cliente;
        var pago = p.Pagamentos.Sum(g => g.Valor) >= p.Total;
        var ultimoPagamento = p.Pagamentos.OrderBy(g => g.PagoEm).LastOrDefault();

        return new PedidoImpressoDto(
            new PedidoImpressoCasaDto(p.Casa.Nome, Limpo(p.Casa.Documento), Limpo(p.Casa.Site), Limpo(p.Casa.WhatsApp), Limpo(p.Casa.LogoUrl)),
            p.Id.ToString("N")[..8].ToUpperInvariant(),
            Prazo(p, ultimoPagamento?.PagoEm),
            new PedidoImpressoClienteDto(
                c.Id is { } id ? id.ToString("N")[..6].ToUpperInvariant() : null,
                Limpo(c.Nome),
                Limpo(c.Telefone),
                EnderecoImpresso.Montar(c.Endereco, c.Complemento, c.Apt, c.Bairro, c.Cidade, c.Cep)),
            p.Entrega is { } e ? new PedidoImpressoEntregaDto(e.Tipo, Limpo(e.Nome)) : null,
            p.Itens.Select(i => new PedidoImpressoItemDto(
                    i.Quantidade,
                    Limpo(i.Variacao) is { } v ? $"{i.Nome.Trim()} ({v})" : i.Nome.Trim(),
                    Limpo(i.Observacao),
                    i.PrecoUnitario,
                    i.Subtotal))
                .ToList(),
            Limpo(p.Observacoes),
            Nota(input.Nota),
            new PedidoImpressoCobrancaDto(p.Total, pago, Limpo(p.FormaCobranca) ?? Limpo(ultimoPagamento?.Metodo)),
            HorarioBrasil.ConverterParaBrasilia(p.CriadoEm),
            HorarioBrasil.ConverterParaBrasilia(p.AlteradoEm),
            HorarioBrasil.ConverterParaBrasilia(relogio.GetUtcNow().UtcDateTime));
    }

    private static PedidoImpressoPrazoDto Prazo(PedidoImpressoLeitura p, DateTime? ultimoPagamentoEm)
    {
        var folga = TimeSpan.FromMinutes(MinutosProntoAntesDaJanela);
        if (p.Janela is { } j)
        {
            var inicio = j.Data.ToDateTime(j.Inicio);
            return new PedidoImpressoPrazoDto(true, inicio, j.Data.ToDateTime(j.Fim), inicio - folga);
        }
        if (p.AgendadoParaEm is { } agendado)
        {
            var inicio = HorarioBrasil.ConverterParaBrasilia(agendado);
            return new PedidoImpressoPrazoDto(true, inicio, null, inicio - folga);
        }
        var pronto = HorarioBrasil.ConverterParaBrasilia(ultimoPagamentoEm ?? p.CriadoEm)
            .AddMinutes(p.TempoPreparoPadraoMinutos);
        return new PedidoImpressoPrazoDto(false, pronto + folga, null, pronto);
    }

    private static string? Nota(string? nota) =>
        Limpo(nota) is { } n ? (n.Length > NotaTamanhoMaximo ? n[..NotaTamanhoMaximo] : n) : null;

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
