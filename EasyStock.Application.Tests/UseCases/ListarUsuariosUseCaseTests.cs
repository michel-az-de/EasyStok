using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ListarUsuarios;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>N9: a dona vê "pendente" ou "aceito via +55•••1234" (a máscara é feita no servidor, na hora do aceite).</summary>
public class ListarUsuariosUseCaseTests
{
    private static readonly DateTime Aceito = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();

    private async Task<List<UsuarioResult>> Listar(params Usuario[] usuarios)
    {
        _usuarios.GetByEmpresaAsync(_empresaId, 1, 20).Returns((usuarios.AsEnumerable(), usuarios.Length));
        var (lista, _) = await new ListarUsuariosUseCase(_usuarios).ExecuteAsync(new ListarUsuariosQuery(_empresaId));
        return lista.ToList();
    }

    [Fact]
    public async Task MostraPendenteOuAceitoComTelefoneMascarado()
    {
        var pendente = Usuario.CriarConvidado("Ana", "ana@casadababa.com");
        var porWhatsApp = Usuario.CriarConvidado("Bia", "bia@casadababa.com");
        porWhatsApp.DefinirTelefone(TelefoneE164.From("+5511999991234"));
        porWhatsApp.AceitarConvite("$2a$11$hash", ViaDoConvite.WhatsApp(porWhatsApp.Telefone!), Aceito);
        var porEmail = Usuario.CriarConvidado("Cai", "cai@casadababa.com");
        porEmail.AceitarConvite("$2a$11$hash", ViaDoConvite.Email, Aceito);
        var porGoogle = Usuario.CriarConvidado("Duda", "duda@casadababa.com");
        porGoogle.AceitarConviteSemSenha(ViaDoConvite.Google, Aceito);
        var legado = Usuario.Criar("Eva", "eva@casadababa.com", "$2a$11$hash");

        var lista = await Listar(pendente, porWhatsApp, porEmail, porGoogle, legado);

        lista[0].Convite.Should().Be(new ConviteDoUsuario("pendente", null, null));
        lista[1].Convite.Should().Be(new ConviteDoUsuario("aceito", Aceito, "+55•••1234"));
        lista[2].Convite.Should().Be(new ConviteDoUsuario("aceito", Aceito, "e-mail"));
        lista[3].Convite.Should().Be(new ConviteDoUsuario("aceito", Aceito, "Google"));
        lista[4].Convite.Should().Be(new ConviteDoUsuario("nenhum", null, null), "quem foi criado com senha nunca teve convite");
    }

    [Fact]
    public async Task ListaNuncaVazaOTelefoneInteiroNemOHash()
    {
        var porWhatsApp = Usuario.CriarConvidado("Bia", "bia@casadababa.com");
        porWhatsApp.DefinirTelefone(TelefoneE164.From("+5511999991234"));
        porWhatsApp.AceitarConvite("$2a$11$hashSecreto", ViaDoConvite.WhatsApp(porWhatsApp.Telefone!), Aceito);

        var json = JsonSerializer.Serialize(await Listar(porWhatsApp));

        json.Should().NotContain("999991234").And.NotContain("hashSecreto").And.NotContain(Usuario.MarcadorDeConvite);
    }

    [Fact]
    public async Task EstadoSerializaEmCaixaBaixaParaATela()
    {
        var json = JsonSerializer.Serialize(
            await Listar(Usuario.CriarConvidado("Ana", "ana@casadababa.com")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"convite\":{\"estado\":\"pendente\",\"aceitoEm\":null,\"via\":null}");
    }
}
