using System;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Financeiro;

/// <summary>
/// Timeline de eventos de dominio de uma conta a pagar ou a receber. Registra
/// transicoes, pagamentos, notificacoes, geracao/reconciliacao de Pix.
/// Polimorfico via XOR entre <see cref="ContaPagarId"/> e <see cref="ContaReceberId"/>.
/// </summary>
public class ContaFinanceiraEvento
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }

    public Guid? ContaPagarId { get; set; }
    public ContaPagar? ContaPagar { get; set; }

    public Guid? ContaReceberId { get; set; }
    public ContaReceber? ContaReceber { get; set; }

    public TipoEventoContaFinanceira TipoEvento { get; set; }
    public string? Descricao { get; set; }
    public string? ValorAntes { get; set; }
    public string? ValorDepois { get; set; }

    public Guid? UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }
    public string? Origem { get; set; } // "web" | "api" | "job" | "webhook"

    public DateTime CriadoEm { get; set; }

    public static ContaFinanceiraEvento ParaContaPagar(
        Guid empresaId,
        Guid contaPagarId,
        TipoEventoContaFinanceira tipo,
        string? descricao = null,
        string? valorAntes = null,
        string? valorDepois = null,
        Guid? usuarioId = null,
        string? usuarioNome = null,
        string? origem = null)
    {
        if (contaPagarId == Guid.Empty) throw new RegraDeDominioVioladaException("ContaPagarId invalido.");
        return new ContaFinanceiraEvento
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ContaPagarId = contaPagarId,
            TipoEvento = tipo,
            Descricao = descricao,
            ValorAntes = valorAntes,
            ValorDepois = valorDepois,
            UsuarioId = usuarioId,
            UsuarioNome = usuarioNome,
            Origem = origem,
            CriadoEm = DateTime.UtcNow
        };
    }

    public static ContaFinanceiraEvento ParaContaReceber(
        Guid empresaId,
        Guid contaReceberId,
        TipoEventoContaFinanceira tipo,
        string? descricao = null,
        string? valorAntes = null,
        string? valorDepois = null,
        Guid? usuarioId = null,
        string? usuarioNome = null,
        string? origem = null)
    {
        if (contaReceberId == Guid.Empty) throw new RegraDeDominioVioladaException("ContaReceberId invalido.");
        return new ContaFinanceiraEvento
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ContaReceberId = contaReceberId,
            TipoEvento = tipo,
            Descricao = descricao,
            ValorAntes = valorAntes,
            ValorDepois = valorDepois,
            UsuarioId = usuarioId,
            UsuarioNome = usuarioNome,
            Origem = origem,
            CriadoEm = DateTime.UtcNow
        };
    }
}
