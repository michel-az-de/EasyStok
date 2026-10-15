using EasyStock.Domain.Entities;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>#1102: o número da Meta da empresa é vinculado e desvinculado pelo back-office.</summary>
public class EmpresaWhatsAppTests
{
    [Fact]
    public void DesvincularLimpaONumeroEMarcaAlteracao()
    {
        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        empresa.VincularWhatsApp("5550001111");
        var antes = empresa.AlteradoEm = DateTime.UtcNow.AddMinutes(-5);

        empresa.DesvincularWhatsApp();

        empresa.WhatsAppPhoneNumberId.Should().BeNull();
        empresa.AlteradoEm.Should().BeAfter(antes);
    }
}
