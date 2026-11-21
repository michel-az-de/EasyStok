using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>#2492: decisão da equipe sobre um telefone do WhatsApp.</summary>
public class DecisaoVinculoTelefoneTests
{
    private static readonly string Hash = new('a', 64);
    private readonly Guid _empresa = Guid.NewGuid();

    [Fact]
    public void Escolher_o_mesmo_cliente_recusado_grava_so_a_escolha()
    {
        var cliente = Guid.NewGuid();

        var decisao = DecisaoVinculoTelefone.Escolher(_empresa, Hash, cliente, cliente, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        decisao.ClienteEscolhidoId.Should().Be(cliente);
        decisao.ClienteRecusadoId.Should().BeNull();
    }

    [Fact]
    public void Revogar_marca_quem_e_quando_e_repetir_nao_muda()
    {
        var decisao = DecisaoVinculoTelefone.Recusar(_empresa, Hash, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        var usuario = Guid.NewGuid();
        var em = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        decisao.Revogar(usuario, em);
        decisao.Revogar(Guid.NewGuid(), em.AddHours(1));

        decisao.Ativa.Should().BeFalse();
        decisao.RevogadaEm.Should().Be(em);
        decisao.RevogadaPorUsuarioId.Should().Be(usuario);
    }

    [Theory]
    [InlineData("curto")]
    [InlineData("")]
    public void Hash_fora_do_formato_e_recusado(string hash)
    {
        var criar = () => DecisaoVinculoTelefone.Recusar(_empresa, hash, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        criar.Should().Throw<RegraDeDominioVioladaException>();
    }
}
