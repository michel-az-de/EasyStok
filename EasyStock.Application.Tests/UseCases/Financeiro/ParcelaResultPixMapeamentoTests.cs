using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// Garante que <c>ParcelaResult.De(ParcelaReceber)</c> carrega os campos do Pix (EfiTxid,
/// copia-e-cola, QR base64, validade) até o DTO — o pré-requisito para a view de Detalhe
/// exibir o QR (B1). É um teste de MAPEAMENTO (não prova o round-trip de persistência).
/// </summary>
public class ParcelaResultPixMapeamentoTests
{
    [Fact]
    public void De_ParcelaReceber_carrega_campos_Pix()
    {
        var empresa = Guid.NewGuid();
        var p = ParcelaReceber.Criar(Guid.NewGuid(), empresa, 1, 100m, DateTime.UtcNow.AddDays(5));
        var expira = DateTime.UtcNow.AddHours(1);
        p.AssociarPix("TXID-ABC123", "00020126-copia-e-cola-demo", "QRCODEBASE64PNG==", expira);

        var dto = ParcelaResult.De(p);

        dto.EfiTxid.Should().Be("TXID-ABC123");
        dto.PixCopiaCola.Should().Be("00020126-copia-e-cola-demo");
        dto.QrCodeBase64.Should().Be("QRCODEBASE64PNG==");
        dto.PixExpiraEm.Should().Be(expira);
    }

    [Fact]
    public void De_ParcelaReceber_sem_Pix_deixa_campos_nulos()
    {
        var empresa = Guid.NewGuid();
        var p = ParcelaReceber.Criar(Guid.NewGuid(), empresa, 1, 100m, DateTime.UtcNow.AddDays(5));

        var dto = ParcelaResult.De(p);

        dto.EfiTxid.Should().BeNull();
        dto.QrCodeBase64.Should().BeNull();
        dto.PixCopiaCola.Should().BeNull();
        dto.PixExpiraEm.Should().BeNull();
    }
}
