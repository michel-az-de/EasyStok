using EasyStock.Domain.Entities;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class IdempotencyKeyTests
{
    [Fact]
    public void Criar_define_expiracao_baseada_em_TTL()
    {
        var antes = DateTime.UtcNow;
        var entry = IdempotencyKey.Criar("k1", Guid.NewGuid(), "POST /api/itensestoque", 200, "{\"id\":1}", TimeSpan.FromHours(2));

        entry.Key.Should().Be("k1");
        entry.HttpStatus.Should().Be(200);
        entry.RespostaJson.Should().Be("{\"id\":1}");
        entry.CriadoEm.Should().BeOnOrAfter(antes);
        entry.ExpiraEm.Should().BeCloseTo(entry.CriadoEm.AddHours(2), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Expirou_retorna_true_apos_a_data_limite()
    {
        var entry = IdempotencyKey.Criar("k", Guid.NewGuid(), "POST /x", 200, null, TimeSpan.FromMinutes(5));

        entry.Expirou(entry.ExpiraEm.AddSeconds(1)).Should().BeTrue();
        entry.Expirou(entry.ExpiraEm.AddSeconds(-1)).Should().BeFalse();
    }
}
