namespace EasyStock.Domain.Enums.Fiscal;

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
