using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.IntegrationTests.Faturas;

/// <summary>
/// Seed compartilhado dos testes de Fatura (issue #512). Cria uma Empresa minima
/// e uma Fatura EMITIDA pronta para receber pagamento.
/// </summary>
internal static class FaturaTestSeed
{
    public static Empresa Empresa(Guid empresaId)
    {
        var now = DateTime.UtcNow;
        return new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Teste Fatura",
            Documento = empresaId.ToString("N")[..14],
            CriadoEm = now,
            AlteradoEm = now,
        };
    }

    /// <summary>Fatura EMITIDA com 1 item (valor padrao R$100).</summary>
    public static Fatura FaturaEmitida(Guid empresaId, decimal valor = 100m)
    {
        var now = DateTime.UtcNow;
        var fatura = Fatura.Criar(
            empresaId,
            numero: "2026-000001",
            dadosFaturado: new DadosFaturado("Cliente Teste"),
            dadosEmissor: new DadosEmissor("EasyStock"),
            origem: OrigemFatura.Avulsa,
            dataEmissao: now,
            dataVencimento: now.AddDays(7));
        fatura.AdicionarItem("Servico de teste", 1m, valor, TipoItemFatura.Servico);
        fatura.Emitir();
        return fatura;
    }
}
