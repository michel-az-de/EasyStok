namespace EasyStock.Domain.Enums.Fiscal;

/// <summary>
/// Campo orig do grupo ICMS da NF-e/NFC-e. Indica a origem da mercadoria
/// conforme tabela A do Convênio SINIEF s/n de 1970. Afeta o cálculo do
/// diferencial de alíquota e benefícios fiscais.
/// </summary>
public enum OrigemMercadoria : byte
{
    Nacional = 0,
    EstrangeiraImportacaoDireta = 1,
    EstrangeiraAdquiridaInternamente = 2,
    NacionalConteudoImportacaoSuperior40 = 3,
    NacionalProcessosBasicosLei = 4,
    NacionalConteudoImportacaoInferior40 = 5,
    EstrangeiraImportacaoDiretaSemSimilarLP = 6,
    EstrangeiraInternaSemSimilarLP = 7,
    NacionalConteudoImportacaoSuperior70 = 8,
}
