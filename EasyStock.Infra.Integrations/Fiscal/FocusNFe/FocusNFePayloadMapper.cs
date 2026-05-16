using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Mapeia <see cref="NfeDocumento"/> + <see cref="ConfigFiscalDto"/> para
/// <see cref="FocusNFeEmissaoRequest"/>. Centraliza traducao de regime
/// tributario, presenca de comprador, modalidade de frete, etc.
/// </summary>
public static class FocusNFePayloadMapper
{
    public static FocusNFeEmissaoRequest Map(NfeDocumento nfe, ConfigFiscalDto config)
    {
        ArgumentNullException.ThrowIfNull(nfe);
        ArgumentNullException.ThrowIfNull(config);

        var request = new FocusNFeEmissaoRequest
        {
            NaturezaOperacao = "Venda ao consumidor",
            DataEmissao = nfe.CriadoEm.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz"),
            TipoDocumento = 1,
            FinalidadeEmissao = 1,
            ConsumidorFinal = 1,
            PresencaComprador = 1,

            CnpjEmitente = SomenteDigitos(config.Cnpj),
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

            ModalidadeFrete = 9, // sem frete
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

            // FormasPagamento: NFC-e exige ao menos um pagamento. Sem dados detalhados,
            // assume "dinheiro" com o total — F1 nao recebe pagamentos explicitos.
            // Quando F3 adicionar PagamentoInput ao Command, mapear aqui.
            FormasPagamento = new List<FocusNFePagamento>
            {
                new() { FormaPagamento = "01", ValorPagamento = nfe.TotalNota.Valor },
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
