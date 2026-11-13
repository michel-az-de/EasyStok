using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #2018 (atendimento-12): "primeiro nome" tinha cinco regras (saudação, variáveis, aviso de status, reenvio e aviso à
/// equipe), mais a comanda do cliente, o aviso de reembolso e a impressão. Esta é a regra única; cada tela escolhe só o
/// padrão para quando não há nome de pessoa.
/// </summary>
public class NomesDeContatoTests
{
    [Theory]
    [InlineData("Maria Souza", "Maria")]
    [InlineData("  Ana   Clara ", "Ana")]
    [InlineData("João", "João")]
    public void NomeDePessoa_DevolveOPrimeiroNome(string nome, string esperado) =>
        NomesDeContato.PrimeiroNome(nome).Should().Be(esperado);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(IdentificarClientePorTelefoneUseCase.NomePadraoLead)]
    [InlineData("+55 11 98888-7777")]
    [InlineData("5511988887777")]
    [InlineData("11 98888-7777")]
    public void SemNomeDePessoa_DevolveNulo(string? nome) =>
        NomesDeContato.PrimeiroNome(nome).Should().BeNull();

    [Theory]
    [InlineData(null, "cliente")]
    [InlineData("Cliente WhatsApp", "cliente")]
    [InlineData("5511988887777", "cliente")]
    [InlineData("Carla Dias", "Carla")]
    public void ComPadrao_UsaOPadraoQuandoNaoHaNomeDePessoa(string? nome, string esperado) =>
        NomesDeContato.PrimeiroNome(nome, "cliente").Should().Be(esperado);
}
