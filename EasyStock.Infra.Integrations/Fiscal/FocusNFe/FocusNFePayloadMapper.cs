using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Mapeia <see cref="NfeDocumento"/> + <see cref="ConfigFiscalDto"/> para
/// <see cref="FocusNFeEmissaoRequest"/>. Centraliza tradução de regime
/// tributário, presença de comprador, modalidade de frete, etc.
/// </summary>
public static class FocusNFePayloadMapper
{
    public static FocusNFeEmissaoRequest Map(NfeDocumento nfe, ConfigFiscalDto config, FocusNFeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(nfe);
        ArgumentNullException.ThrowIfNull(config);
        options ??= new FocusNFeOptions();

        var request = new FocusNFeEmissaoRequest
        {
            NaturezaOperacao = "Venda ao consumidor",
            DataEmissao = nfe.CriadoEm.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz"),
            TipoDocumento = 1,
            FinalidadeEmissao = 1,
            ConsumidorFinal = 1,
            PresencaComprador = options.PresencaCompradorPadrao,

            CnpjEmitente = SomenteDigitos(config.Cnpj) ?? string.Empty,
            NomeEmitente = nfe.DadosEmitente.RazaoSocial ?? nfe.DadosEmitente.Nome ?? string.Empty,
            NomeFantasiaEmitente = nfe.DadosEmitente.Nome,
            LogradouroEmitente = config.Endereco?.Logradouro,
            NumeroEmitente = config.Endereco?.Numero,
            BairroEmitente = config.Endereco?.Bairro,
            MunicipioEmitente = config.Endereco?.Cidade,
            UfEmitente = config.Endereco?.Uf,
            CepEmitente = SomenteDigitos(config.Endereco?.Cep),
            InscricaoEstadualEmitente = config.InscricaoEstadual,
            RegimeTributarioEmitente = MapearRegime(config.RegimeTributario),

            CpfDestinatario = nfe.DadosDestinatario?.Documento is { Length: 11 } cpf ? cpf : null,
            NomeDestinatario = nfe.DadosDestinatario?.Nome,

            ModalidadeFrete = options.ModalidadeFretePadrao,
            ValorTotal = nfe.TotalNota.Valor,
            ValorProdutos = nfe.TotalNota.Valor,

            Items = nfe.Itens
                .OrderBy(i => i.Ordem)
                .Select(i => new FocusNFeItem
                {
                    NumeroItem = i.Ordem,
                    CodigoProduto = i.ProdutoIdSnapshot?.ToString() ?? $"item-{i.Ordem}",
                    Descricao = i.NomeSnapshot,
                    CodigoNcm = i.NcmSnapshot,
                    Cfop = i.CfopSnapshot,
                    Unidade = i.Unidade,
                    Quantidade = i.Quantidade,
                    ValorUnitario = i.PrecoUnitario.Valor,
                    ValorBruto = i.Subtotal.Valor,
                    UnidadeTributavel = i.Unidade,
                    QuantidadeTributavel = i.Quantidade,
                    ValorUnitarioTributavel = i.PrecoUnitario.Valor,
                    OrigemMercadoria = i.OrigemMercadoria,
                    IcmsSituacaoTributaria = i.CstOuCsosn,
                })
                .ToList(),

            // FormasPagamento: NFC-e exige ao menos um pagamento. Sem dados detalhados
            // (F1 ainda não recebe pagamentos explícitos no Command), assume o padrão
            // configurado em FocusNFeOptions.FormaPagamentoPadrao — em produção real,
            // expor PagamentoInput no Command (F3.5) e mapear aqui é prioritário.
            FormasPagamento = new List<FocusNFePagamento>
            {
                new() { FormaPagamento = options.FormaPagamentoPadrao, ValorPagamento = nfe.TotalNota.Valor },
            },
        };

        return request;
    }

    private static int MapearRegime(EasyStock.Domain.Fiscal.RegimeTributario regime) =>
        regime switch
        {
            EasyStock.Domain.Fiscal.RegimeTributario.Simples => 1,
            EasyStock.Domain.Fiscal.RegimeTributario.MicroempreendedorIndividual => 1,
            EasyStock.Domain.Fiscal.RegimeTributario.LucroPresumido => 3,
            EasyStock.Domain.Fiscal.RegimeTributario.LucroReal => 3,
            _ => 3,
        };

    private static string? SomenteDigitos(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        return new string(input.Where(char.IsDigit).ToArray());
    }
}
