using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#2155: o que cada mensagem do WhatsApp acrescenta ao cadastro do cliente.</summary>
public sealed class PerfilClienteWhatsAppTests
{
    private static readonly DateTime Agora = new(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();

    private PerfilClienteWhatsApp Perfil() => new(_clientes, _crm);

    private static MensagemRecebidaWhatsApp Mensagem(string? texto, LocalizacaoWhatsApp? local = null) =>
        new("5541988887777", "wamid.1", DateTimeOffset.UtcNow, local is null ? "text" : "location", texto,
            null, null, null, null, local);

    [Theory]
    [InlineData("Cliente WhatsApp", "Maria Souza", "Maria Souza")]
    [InlineData("cliente whatsapp", "  Maria  ", "Maria")]
    [InlineData("5541988887777", "Maria", "Maria")]
    [InlineData("", "Maria", "Maria")]
    [InlineData("Maria Souza", "Mari ✨", null)]
    [InlineData("Cliente WhatsApp", "🎂🎂", null)]
    [InlineData("Cliente WhatsApp", ".", null)]
    [InlineData("Cliente WhatsApp", null, null)]
    [InlineData("Maria", "Maria", null)]
    public void NomeDoPerfilSoTrocaONomeProvisorio(string atual, string? perfil, string? esperado) =>
        PerfilClienteWhatsApp.NomeParaTrocar(atual, perfil).Should().Be(esperado);

    [Theory]
    [InlineData("meu email é Maria.Souza@Gmail.com.", "maria.souza@gmail.com")]
    [InlineData("manda pra joao_1@empresa.com.br por favor", "joao_1@empresa.com.br")]
    [InlineData("a@b.com e c@d.com", null)]
    [InlineData("a@b.com e A@B.com", "a@b.com")]
    [InlineData("sem e-mail aqui @fulano", null)]
    [InlineData(null, null)]
    public void EmailDoTextoSoDecideComUmEmail(string? texto, string? esperado) =>
        PerfilClienteWhatsApp.EmailDoTexto(texto).Should().Be(esperado);

    [Fact]
    public void NotaDaLocalizacaoLevaOLinkDoMapaDentroDoLimite()
    {
        var local = new LocalizacaoWhatsApp(-25.4284, -49.2733, "Casa", new string('x', 900));

        var texto = PerfilClienteWhatsApp.TextoDaNota(local);

        texto.Length.Should().BeLessThanOrEqualTo(ClienteNota.TextoTamanhoMaximo);
        texto.Should().StartWith("Localização enviada pelo WhatsApp: Casa, xxx")
            .And.EndWith("Mapa: https://www.google.com/maps?q=-25.4284,-49.2733");
    }

    [Fact]
    public async Task LeadProvisorioGanhaONomeDoPerfilComHistorico()
    {
        var cliente = Cliente.Criar(_empresaId, "Cliente WhatsApp");
        _clientes.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);

        await Perfil().AplicarAsync(_empresaId, cliente.Id, null, "Maria Souza", Mensagem("oi"), Guid.NewGuid(), Agora, default);

        cliente.Nome.Should().Be("Maria Souza");
        cliente.AlteradoEm.Should().Be(Agora);
        await _clientes.Received(1).AddAlteracaoAsync(Arg.Is<ClienteAlteracao>(a =>
            a.Campo == "Nome" && a.ValorAntigo == "Cliente WhatsApp" && a.ValorNovo == "Maria Souza"
            && a.Origem == "whatsapp" && a.ClienteId == cliente.Id && a.EmpresaId == _empresaId));
    }

    [Fact]
    public async Task NomeDadoPelaLojaNaoMuda()
    {
        var cliente = Cliente.Criar(_empresaId, "Dona Maria do 302");

        await Perfil().AplicarAsync(_empresaId, cliente.Id, cliente, "Mari", Mensagem("oi"), Guid.NewGuid(), Agora, default);

        cliente.Nome.Should().Be("Dona Maria do 302");
        await _clientes.DidNotReceiveWithAnyArgs().AddAlteracaoAsync(default!);
    }

    [Fact]
    public async Task EmailEscritoPreencheOCampoVazio()
    {
        var cliente = Cliente.Criar(_empresaId, "Maria");

        await Perfil().AplicarAsync(_empresaId, cliente.Id, cliente, null, Mensagem("pode mandar a nota no maria@exemplo.com"),
            Guid.NewGuid(), Agora, default);

        cliente.Email.Should().Be("maria@exemplo.com");
        await _clientes.Received(1).AddAlteracaoAsync(Arg.Is<ClienteAlteracao>(a => a.Campo == "Email" && a.ValorNovo == "maria@exemplo.com"));
    }

    [Fact]
    public async Task EmailDoCadastroNaoESobrescrito()
    {
        var cliente = Cliente.Criar(_empresaId, "Maria");
        cliente.Email = "maria@cadastro.com";

        await Perfil().AplicarAsync(_empresaId, cliente.Id, cliente, null, Mensagem("o da minha filha é ana@exemplo.com"),
            Guid.NewGuid(), Agora, default);

        cliente.Email.Should().Be("maria@cadastro.com");
    }

    [Fact]
    public async Task LocalizacaoViraNotaDoClienteLigadaAMensagem()
    {
        var cliente = Cliente.Criar(_empresaId, "Maria");
        var mensagemId = Guid.NewGuid();
        var local = new LocalizacaoWhatsApp(-25.4284, -49.2733, "Prédio azul", "Rua XV, 100");

        await Perfil().AplicarAsync(_empresaId, cliente.Id, cliente, null, Mensagem("Prédio azul", local), mensagemId, Agora, default);

        await _crm.Received(1).AdicionarNotaAsync(Arg.Is<ClienteNota>(n =>
            n.ClienteId == cliente.Id && n.EmpresaId == _empresaId && n.MensagemId == mensagemId && n.Autor == "whatsapp"
            && n.Texto.Contains("Prédio azul, Rua XV, 100") && n.Texto.Contains("maps?q=-25.4284,-49.2733")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClienteDeOutraEmpresaNaoEMexido()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Cliente WhatsApp");

        var resultado = await Perfil().AplicarAsync(_empresaId, cliente.Id, cliente, "Maria", Mensagem("x@y.com"),
            Guid.NewGuid(), Agora, default);

        resultado.Should().BeNull();
        cliente.Nome.Should().Be("Cliente WhatsApp");
        cliente.Email.Should().BeNull();
    }
}
