using EasyStock.Domain.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.Common;

/// <summary>
/// Validação de entrada das contas a pagar e a receber (#2095, F7 a F9). Os limites de texto são os das configurações
/// EF (<c>ContaPagarConfiguration</c>, <c>PagamentoParcelaConfiguration</c>, <c>CategoriaFinanceiraConfiguration</c>,
/// <c>CentroCustoConfiguration</c>): estourar um deles no banco virava 500; aqui vira 400 com a mensagem.
/// </summary>
public static class ValidacaoFinanceira
{
    /// <summary>Teto de sanidade de valor (cabe com folga no numeric(14,2) e barra o "1e15" digitado).</summary>
    public const decimal ValorMaximo = 999_999_999.99m;

    public const int DescricaoMax = 200;
    public const int ObservacoesContaMax = 4000;
    public const int DocumentoMax = 120;
    public const int MetodoMax = 20;
    public const int ObservacaoPagamentoMax = 500;
    public const int MotivoMax = 500;
    public const int GatewayProvedorMax = 40;
    public const int GatewayTransacaoMax = 120;
    public const int NomeCadastroMax = 80;
    public const int CorMax = 20;
    public const int IconeMax = 60;
    public const int CodigoCentroMax = 20;
    public const int DescricaoCentroMax = 400;
    public const int TamanhoPaginaMax = 200;

    /// <summary>Texto opcional com no máximo <paramref name="max"/> caracteres (depois do trim, como é gravado).</summary>
    public static void Texto(string? valor, int max, string campo)
    {
        if (valor is not null && valor.Trim().Length > max)
            throw new UseCaseValidationException($"{campo} deve ter no máximo {max} caracteres.");
    }

    /// <summary>
    /// Valor em reais arredondado a 2 casas ANTES de validar (F8: 0,004 passava no "&gt; 0" e gravava R$ 0,00), entre
    /// um centavo e <see cref="ValorMaximo"/>.
    /// </summary>
    public static decimal Valor(decimal valor, string campo)
    {
        var arredondado = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
        if (arredondado <= 0m)
            throw new UseCaseValidationException($"{campo} deve ser maior que zero.");
        if (arredondado > ValorMaximo)
            throw new UseCaseValidationException($"{campo} deve ser no máximo R$ 999.999.999,99.");
        return arredondado;
    }

    /// <summary>Forma de pagamento do caixa (<see cref="MetodosPagamentoCaixa"/>), normalizada.</summary>
    public static string MetodoPagamento(string? metodo)
    {
        if (string.IsNullOrWhiteSpace(metodo))
            throw new UseCaseValidationException("Forma de pagamento é obrigatória.");
        return MetodosPagamentoCaixa.Normalizar(metodo)
               ?? throw new UseCaseValidationException(
                   $"Forma de pagamento inválida. Use: {string.Join(", ", MetodosPagamentoCaixa.Todos)}.");
    }

    /// <summary>
    /// Instante do pagamento (F9). Sem data é agora. Data sem fuso é horário de Brasília (data pura é o início do dia
    /// civil, não 00:00Z, que caía às 21h do dia anterior). Dia depois de hoje em Brasília é recusado.
    /// </summary>
    public static DateTime DataPagamentoUtc(DateTime? data)
    {
        if (!data.HasValue) return DateTime.UtcNow;
        var instante = DataUtc.InicioDoPeriodoUtc(data)!.Value;
        if (instante >= DateTime.MaxValue.AddDays(-1) || HorarioBrasil.DataOperacional(instante) > HorarioBrasil.Hoje())
            throw new UseCaseValidationException("A data do pagamento não pode ser futura.");
        return instante;
    }

    /// <summary>
    /// Página e tamanho seguros (F7): tamanho entre 1 e <see cref="TamanhoPaginaMax"/>; página entre 1 e a última cujo
    /// deslocamento ainda cabe num int (página enorme estourava o OFFSET e respondia 500).
    /// </summary>
    public static (int Page, int PageSize) Pagina(int page, int pageSize)
    {
        var tamanho = Math.Clamp(pageSize, 1, TamanhoPaginaMax);
        return (Math.Clamp(page, 1, int.MaxValue / tamanho), tamanho);
    }

    /// <summary>Descrição, observações e documento de uma conta (criar e atualizar).</summary>
    public static void TextosDaConta(string? descricao, string? observacoes, string? documentoReferencia = null)
    {
        Texto(descricao, DescricaoMax, "Descrição");
        Texto(observacoes, ObservacoesContaMax, "Observações");
        Texto(documentoReferencia, DocumentoMax, "Documento de referência");
    }

    /// <summary>Valor e forma planejada de uma parcela; devolve o valor arredondado.</summary>
    public static decimal Parcela(decimal valor, string? metodoPlanejado)
    {
        Texto(metodoPlanejado, MetodoMax, "Forma de pagamento planejada");
        return Valor(valor, "Valor da parcela");
    }

    /// <summary>
    /// Referências opcionais da conta (F7): fornecedor, cliente e loja precisam existir na empresa. Id inexistente virava
    /// erro de chave estrangeira (500), e id de outra empresa ligava a conta a um cadastro alheio. <see cref="Guid.Empty"/>
    /// é "sem referência" (a edição usa para limpar). Sem o repositório (teste antigo) a checagem não roda.
    /// </summary>
    public static async Task GarantirReferenciasAsync(Guid empresaId,
        Guid? fornecedorId = null, IFornecedorRepository? fornecedores = null,
        Guid? clienteId = null, IClienteRepository? clientes = null,
        Guid? lojaId = null, ILojaRepository? lojas = null)
    {
        if (fornecedores is not null && fornecedorId is { } f && f != Guid.Empty &&
            await fornecedores.GetByIdAsync(empresaId, f) is null)
            throw new UseCaseValidationException("Fornecedor não encontrado.");
        if (clientes is not null && clienteId is { } c && c != Guid.Empty &&
            await clientes.GetByIdAsync(empresaId, c) is null)
            throw new UseCaseValidationException("Cliente não encontrado.");
        if (lojas is not null && lojaId is { } l && l != Guid.Empty &&
            await lojas.GetByIdAsync(empresaId, l) is null)
            throw new UseCaseValidationException("Loja não encontrada.");
    }
}
