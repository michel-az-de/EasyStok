using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>#2211: a pessoa inscrita no aviso da equipe também escreve para a loja; o robô não a atende.</summary>
public class ContatoDaEquipeAvisadaTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IAudienciaUsuarios _usuarios = Substitute.For<IAudienciaUsuarios>();
    private readonly IPreferenciaNotificacaoRepository _preferencias = Substitute.For<IPreferenciaNotificacaoRepository>();
    private readonly UsuarioParaAudiencia _ana = new(Guid.NewGuid(), "Ana", "ana@casa.com", true, true, "+5511999991234", DateTime.UtcNow);

    public ContatoDaEquipeAvisadaTests() =>
        _usuarios.ListarDaEmpresaAsync(_empresaId, Arg.Any<IReadOnlyCollection<NivelAcesso>>(), Arg.Any<CancellationToken>())
            .Returns([_ana]);

    private ContatoDaEquipeAvisada Criar() => new(_usuarios, _preferencias);

    private void Inscrever(string rotina, bool habilitada = true) =>
        _preferencias.ListarDaRotinaAsync(_empresaId, rotina, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([PreferenciaNotificacaoUsuario.Criar(_ana.Id, _empresaId, rotina, habilitada)]);

    [Theory]
    [InlineData("+5511999991234", "5511999991234")]
    [InlineData("(11) 99999-1234", "551199991234")] // wa_id antigo, sem o nono dígito
    [InlineData("11999991234", "+55 11 99999-1234")]
    public void MesmoCelularEmFormatosDiferentes(string a, string b) =>
        ContatoDaEquipeAvisada.MesmoTelefone(a, b).Should().BeTrue();

    [Theory]
    [InlineData("+5511999991234", "+5521999991234")] // outro DDD
    [InlineData("+5511999991234", "+5511999995678")]
    [InlineData("+5511999991234", null)]
    [InlineData("123", "123")]
    public void TelefonesDiferentesOuInvalidos(string? a, string? b) =>
        ContatoDaEquipeAvisada.MesmoTelefone(a, b).Should().BeFalse();

    [Fact]
    public async Task InscritaEmQualquerAvisoEhContatoDaEquipe()
    {
        Inscrever(AvisosDaEquipe.ProblemaNoPagamento.RotinaCodigo);

        (await Criar().EhInscritoAsync(_empresaId, "551199991234")).Should().BeTrue();
    }

    [Fact]
    public async Task QuemDesligouOAvisoVoltaASerAtendidaPeloRobo()
    {
        Inscrever(AvisosDaEquipe.PedidoComCanhoto.RotinaCodigo, habilitada: false);

        (await Criar().EhInscritoAsync(_empresaId, "+5511999991234")).Should().BeFalse();
    }

    [Fact]
    public async Task ClienteComOutroTelefoneNaoConsultaPreferencias()
    {
        (await Criar().EhInscritoAsync(_empresaId, "+5511988887777")).Should().BeFalse();

        await _preferencias.DidNotReceiveWithAnyArgs().ListarDaRotinaAsync(default, default!, default!, default);
    }
}
