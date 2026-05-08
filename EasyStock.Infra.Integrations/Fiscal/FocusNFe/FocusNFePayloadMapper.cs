using System.Globalization;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Mapeia <see cref="NotaFiscal"/> + <see cref="ConfigFiscalDto"/> →
/// <see cref="FocusNFeEmissaoRequest"/>. Snapshot tests cobrem mudanças
/// de layout. Sem dependência de banco — só transformação determinística.
/// </summary>
public sealed class FocusNFePayloadMapper
{
    public FocusNFeEmissaoRequest Mapear(NotaFiscal nota, ConfigFiscalDto config)
    {
        ArgumentNullException.ThrowIfNull(nota);
        ArgumentNullException.ThrowIfNull(config);

        var (cpf, cnpj) = SplitCpfCnpj(nota.ClienteCpfCnpj);

        return new FocusNFeEmissaoRequest
        {
            NaturezaOperacao = "Venda de mercadoria",
            DataEmissao = nota.DataEmissao.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            TipoDocumento = 1,
            FinalidadeEmissao = 1,
            PresencaComprador = 1,
            ConsumidorFinal = 1,
            ModalidadeFrete = 9,
            Ambiente = config.Ambiente == AmbienteSefaz.Producao ? "producao" : "homologacao",

            CnpjEmitente = config.CnpjEmitente,
            NomeEmitente = config.NomeEmitente,
            LogradouroEmitente = config.LogradouroEmitente,
            NumeroEmitente = config.NumeroEnderecoEmitente,
            ComplementoEmitente = config.ComplementoEnderecoEmitente,
            BairroEmitente = config.BairroEmitente,
            MunicipioEmitente = config.MunicipioEmitente,
            UfEmitente = config.UfEmitente,
            CepEmitente = config.CepEmitente,
            InscricaoEstadualEmitente = config.InscricaoEstadualEmitente,
            RegimeTributarioEmitente = (int)config.RegimeTributario,

            CpfDestinatario = cpf,
            CnpjDestinatario = cnpj,
            NomeDestinatario = (cpf is not null || cnpj is not null) ? "Consumidor Identificado" : null,

            Serie = nota.Serie,
            Numero = nota.Numero,
            FormaEmissao = (int)nota.TipoEmissao,

            Items = nota.Itens.OrderBy(i => i.Ordem).Select(MapearItem).ToList(),
            FormasPagamento = nota.Pagamentos.OrderBy(p => p.Ordem).Select(MapearPagamento).ToList(),

            ValorTotal = nota.ValorTotal.Valor,
            ValorProdutos = nota.Itens.Sum(i => i.Subtotal.Valor),
        };
    }

    public FocusNFeEmissaoRequest MapearContingencia(NotaFiscal nota, ConfigFiscalDto config)
    {
        var req = Mapear(nota, config);
        req.FormaEmissao = (int)TipoEmissao.OfflineNFCe;
        return req;
    }

    private static FocusNFeItemRequest MapearItem(NotaFiscalItem item)
    {
        return new FocusNFeItemRequest
        {
            NumeroItem = item.Ordem,
            CodigoProduto = item.CodigoProduto,
            Descricao = item.DescricaoSnapshot,
            Ean = item.Ean,
            Cfop = item.Cfop.Valor,
            Ncm = item.Ncm.Valor,
            Cest = item.Cest,
            UnidadeComercial = item.UnidadeComercial,
            QuantidadeComercial = item.Quantidade,
            ValorUnitarioComercial = item.PrecoUnitario,
            ValorBruto = item.Subtotal.Valor + item.Desconto,
            ValorDesconto = item.Desconto,
            UnidadeTributavel = item.UnidadeComercial,
            QuantidadeTributavel = item.Quantidade,
            ValorUnitarioTributavel = item.PrecoUnitario,
            IncluiNoTotal = 1,
            IcmsOrigem = (int)item.OrigemMercadoria,
            IcmsSituacaoTributaria = item.CstCsosn.Valor,
            IcmsAliquota = item.IcmsAliquota,
            IcmsValor = item.IcmsValor?.Valor,
            PisSituacaoTributaria = item.CstPis,
            PisAliquota = item.PisAliquota,
            PisValor = item.PisValor?.Valor,
            CofinsSituacaoTributaria = item.CstCofins,
            CofinsAliquota = item.CofinsAliquota,
            CofinsValor = item.CofinsValor?.Valor,
        };
    }

    private static FocusNFePagamentoRequest MapearPagamento(NotaFiscalPagamento p)
    {
        return new FocusNFePagamentoRequest
        {
            FormaPagamento = ((byte)p.FormaPagamento).ToString("D2"),
            ValorPagamento = p.Valor.Valor,
            BandeiraOperadora = p.BandeiraCartao,
            CnpjCredenciadora = p.CnpjCredenciadora,
            Nsu = p.Nsu,
        };
    }

    private static (string? Cpf, string? Cnpj) SplitCpfCnpj(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var digitos = new string(raw.Where(char.IsDigit).ToArray());
        return digitos.Length switch
        {
            11 => (digitos, null),
            14 => (null, digitos),
            _ => (null, null),
        };
    }

    /// <summary>
    /// Gera XML local não-assinado para preservar em contingência. Focus
    /// assina ao retransmitir; localmente apenas estruturamos o documento
    /// pra não perder a venda.
    /// </summary>
    public string MontarXmlLocalSemAssinatura(NotaFiscal nota, ConfigFiscalDto config)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">");
        sb.AppendLine($"  <infNFe versao=\"4.00\" Id=\"NFe{nota.ChaveAcesso.Valor}\">");
        sb.AppendLine($"    <ide>");
        sb.AppendLine($"      <cUF>{nota.ChaveAcesso.Uf}</cUF>");
        sb.AppendLine($"      <cNF>{nota.ChaveAcesso.CodigoNumerico}</cNF>");
        sb.AppendLine($"      <natOp>Venda de mercadoria</natOp>");
        sb.AppendLine($"      <mod>{(short)nota.Modelo}</mod>");
        sb.AppendLine($"      <serie>{nota.Serie}</serie>");
        sb.AppendLine($"      <nNF>{nota.Numero}</nNF>");
        sb.AppendLine($"      <dhEmi>{nota.DataEmissao:yyyy-MM-ddTHH:mm:sszzz}</dhEmi>");
        sb.AppendLine($"      <tpNF>1</tpNF>");
        sb.AppendLine($"      <idDest>1</idDest>");
        sb.AppendLine($"      <cMunFG>{config.MunicipioCodigoIbge}</cMunFG>");
        sb.AppendLine($"      <tpImp>4</tpImp>");
        sb.AppendLine($"      <tpEmis>{(byte)nota.TipoEmissao}</tpEmis>");
        sb.AppendLine($"      <cDV>{nota.ChaveAcesso.DigitoVerificador}</cDV>");
        sb.AppendLine($"      <tpAmb>{(byte)nota.Ambiente}</tpAmb>");
        sb.AppendLine($"      <finNFe>1</finNFe>");
        sb.AppendLine($"      <indFinal>1</indFinal>");
        sb.AppendLine($"      <indPres>1</indPres>");
        sb.AppendLine($"      <procEmi>0</procEmi>");
        sb.AppendLine($"      <verProc>EasyStok-1.0</verProc>");
        sb.AppendLine($"    </ide>");
        sb.AppendLine($"    <emit>");
        sb.AppendLine($"      <CNPJ>{config.CnpjEmitente}</CNPJ>");
        sb.AppendLine($"      <xNome>{Escape(config.NomeEmitente)}</xNome>");
        sb.AppendLine($"      <enderEmit>");
        sb.AppendLine($"        <xLgr>{Escape(config.LogradouroEmitente)}</xLgr>");
        sb.AppendLine($"        <nro>{config.NumeroEnderecoEmitente}</nro>");
        sb.AppendLine($"        <xBairro>{Escape(config.BairroEmitente)}</xBairro>");
        sb.AppendLine($"        <cMun>{config.MunicipioCodigoIbge}</cMun>");
        sb.AppendLine($"        <xMun>{Escape(config.MunicipioEmitente)}</xMun>");
        sb.AppendLine($"        <UF>{config.UfEmitente}</UF>");
        sb.AppendLine($"        <CEP>{config.CepEmitente}</CEP>");
        sb.AppendLine($"      </enderEmit>");
        sb.AppendLine($"      <IE>{config.InscricaoEstadualEmitente}</IE>");
        sb.AppendLine($"      <CRT>{(byte)config.RegimeTributario}</CRT>");
        sb.AppendLine($"    </emit>");
        sb.AppendLine($"    <total>");
        sb.AppendLine($"      <ICMSTot>");
        sb.AppendLine($"        <vNF>{nota.ValorTotal.Valor.ToString("F2", CultureInfo.InvariantCulture)}</vNF>");
        sb.AppendLine($"      </ICMSTot>");
        sb.AppendLine($"    </total>");
        sb.AppendLine($"  </infNFe>");
        sb.AppendLine($"</NFe>");
        return sb.ToString();
    }

    private static string Escape(string s) =>
        System.Security.SecurityElement.Escape(s) ?? s;
}
