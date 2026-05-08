namespace EasyStock.Domain.Enums.Fiscal;

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
