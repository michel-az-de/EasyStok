namespace EasyStock.Domain.Enums.Fiscal;

/// <summary>
/// Tabela tPag do XML da NF-e/NFC-e (Ato Cotepe 28/2019 + atualizações).
/// Valores numéricos são os códigos SEFAZ — não alterar sem atualizar
/// a tabela de referência do layout vigente.
/// </summary>
public enum FormaPagamentoFiscal : byte
{
    Dinheiro = 1,
    Cheque = 2,
    CartaoCredito = 3,
    CartaoDebito = 4,
    CreditoLoja = 5,
    ValeAlimentacao = 10,
    ValeRefeicao = 11,
    ValePresente = 12,
    ValeCombustivel = 13,
    BoletoBancario = 15,
    DepositoBancario = 16,
    Pix = 17,
    TransferenciaBancaria = 18,
    ProgramaFidelidade = 19,
    SemPagamento = 90,
    Outros = 99,
}
