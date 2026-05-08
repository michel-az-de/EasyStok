using System;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities.Fiscal;

public sealed class NotaFiscalPagamento
{
    public Guid Id { get; private set; }
    public Guid NotaFiscalId { get; private set; }
    public Guid EmpresaId { get; private set; }
    public int Ordem { get; private set; }
    public FormaPagamentoFiscal FormaPagamento { get; private set; }
    public Dinheiro Valor { get; private set; } = Dinheiro.Zero;
    public string? BandeiraCartao { get; private set; }
    public string? CnpjCredenciadora { get; private set; }
    public string? Nsu { get; private set; }
    public Dinheiro Troco { get; private set; } = Dinheiro.Zero;

    private NotaFiscalPagamento() { }

    public static NotaFiscalPagamento Criar(
        Guid notaFiscalId,
        Guid empresaId,
        int ordem,
        FormaPagamentoFiscal formaPagamento,
        Dinheiro valor,
        string? bandeiraCartao = null,
        string? cnpjCredenciadora = null,
        string? nsu = null,
        Dinheiro? troco = null)
    {
        if (notaFiscalId == Guid.Empty)
            throw new ArgumentException("NotaFiscalId é obrigatório.", nameof(notaFiscalId));
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
        if (ordem <= 0)
            throw new ArgumentOutOfRangeException(nameof(ordem), "Ordem deve ser positiva.");
        if (valor is null)
            throw new ArgumentNullException(nameof(valor));
        if (valor.Valor <= 0)
            throw new ArgumentException("Valor do pagamento deve ser positivo.", nameof(valor));

        return new NotaFiscalPagamento
        {
            Id = Guid.NewGuid(),
            NotaFiscalId = notaFiscalId,
            EmpresaId = empresaId,
            Ordem = ordem,
            FormaPagamento = formaPagamento,
            Valor = valor,
            BandeiraCartao = bandeiraCartao,
            CnpjCredenciadora = cnpjCredenciadora,
            Nsu = nsu,
            Troco = troco ?? Dinheiro.Zero,
        };
    }
}
