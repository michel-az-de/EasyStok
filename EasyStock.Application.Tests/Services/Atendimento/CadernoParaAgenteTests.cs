using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>S54: o núcleo vai inteiro no prompt; o resto entra só como uma linha de índice.</summary>
public class CadernoParaAgenteTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SemTrechosDevolveVazio() =>
        CadernoParaAgente.Montar([]).Should().BeEmpty();

    [Fact]
    public void NucleoInteiroEIndiceSoComTitulo()
    {
        var horario = TrechoCaderno.Criar(Empresa, "Horário", "Abrimos de terça a sábado, das 9h às 18h.", "horário", nucleo: true, Agora);
        var troca = TrechoCaderno.Criar(Empresa, "Troca", "Trocamos em até 24 h com a embalagem fechada.", "troca, devolução", nucleo: false, Agora);
        var congelar = TrechoCaderno.Criar(Empresa, "Congelar", "Massas duram 3 meses no freezer.", "freezer", nucleo: false, Agora);

        var bloco = CadernoParaAgente.Montar([horario, troca, congelar]);

        bloco.Should().Contain("Abrimos de terça a sábado, das 9h às 18h.");
        bloco.Should().Contain($"[{troca.Codigo}] Troca · troca, devolução");
        bloco.Should().Contain($"[{congelar.Codigo}] Congelar · freezer");
        bloco.Should().NotContain("Trocamos em até 24 h", "o texto fora do núcleo só vem pela ferramenta");
        bloco.Should().NotContain($"[{horario.Codigo}]", "o núcleo já está inteiro no prompt");
        bloco.Should().Contain("consultar_caderno");
    }

    [Fact]
    public void SoNucleoNaoMostraIndice()
    {
        var horario = TrechoCaderno.Criar(Empresa, "Horário", "Abrimos às 9h.", null, nucleo: true, Agora);

        var bloco = CadernoParaAgente.Montar([horario]);

        bloco.Should().Contain("Abrimos às 9h.").And.NotContain("consultar_caderno");
    }

    [Fact]
    public void IgnoraArquivados()
    {
        var antigo = TrechoCaderno.Criar(Empresa, "Antigo", "não vale mais", null, nucleo: true, Agora);
        antigo.Arquivar(true, Agora);

        CadernoParaAgente.Montar([antigo]).Should().BeEmpty();
    }
}
