using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// #1430: o formulário antes do chat do site. Sem aceite não há contato; telefone vira E.164 BR, e-mail é
/// opcional e validado; a conversa recebe o contato sem trocar o nome de quem já tem cliente.
/// </summary>
public class ContatoInformadoVisitanteTests
{
    private static readonly DateTime Agora = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Criar_NormalizaNomeTelefoneEEmail()
    {
        var contato = ContatoInformadoVisitante.Criar("  Maria   Souza ", "(11) 98765-4321", " Maria@Exemplo.COM ", true, Agora);

        contato.Nome.Should().Be("Maria Souza");
        contato.Telefone.Should().Be("+5511987654321");
        contato.Email.Should().Be("maria@exemplo.com");
        contato.InformadoEm.Should().Be(Agora);
    }

    [Fact]
    public void Criar_EmailEmBrancoViraNulo()
    {
        ContatoInformadoVisitante.Criar("Maria", "11987654321", "  ", true, Agora).Email.Should().BeNull();
    }

    [Theory]
    [InlineData("Maria", "11987654321", null, false, "*política*")]
    [InlineData("", "11987654321", null, true, "*nome*")]
    [InlineData("M", "11987654321", null, true, "*nome*")]
    [InlineData("Maria", "98765", null, true, "*Telefone*")]
    [InlineData("Maria", "", null, true, "*Telefone*")]
    [InlineData("Maria", "11987654321", "maria-sem-arroba", true, "*E-mail*")]
    [InlineData("Maria", "11987654321", "maria@local", true, "*E-mail*")]
    public void Criar_Recusa(string? nome, string? telefone, string? email, bool aceite, string mensagem)
    {
        var acao = () => ContatoInformadoVisitante.Criar(nome, telefone, email, aceite, Agora);

        acao.Should().Throw<RegraDeDominioVioladaException>().WithMessage(mensagem);
    }

    [Fact]
    public void Criar_RecusaNomeMaiorQueOContatoDaConversa()
    {
        var acao = () => ContatoInformadoVisitante.Criar(new string('a', Conversa.ContatoNomeTamanhoMaximo + 1), "11987654321", null, true, Agora);

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Sessao_GuardaOContatoEOVisitantePodeCorrigir()
    {
        var sessao = SessaoChatSite.Abrir(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Agora);
        sessao.ContatoInformado.Should().BeNull();

        sessao.Identificar(ContatoInformadoVisitante.Criar("Maria", "11987654321", null, true, Agora));
        sessao.Identificar(ContatoInformadoVisitante.Criar("Maria Souza", "11912345678", "maria@exemplo.com", true, Agora.AddMinutes(1)));

        sessao.ContatoInformado.Should().BeEquivalentTo(new
        {
            Nome = "Maria Souza", Telefone = "+5511912345678", Email = "maria@exemplo.com", InformadoEm = Agora.AddMinutes(1),
        });
    }

    [Fact]
    public void Conversa_SemCliente_PassaAUsarONomeInformado()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), "sessao-1", Agora, "Visitante do site", canal: CanalConversa.ChatSite);

        conversa.RegistrarContatoInformado(ContatoInformadoVisitante.Criar("Maria Souza", "11987654321", "maria@exemplo.com", true, Agora));

        conversa.ContatoNome.Should().Be("Maria Souza");
        conversa.ContatoTelefoneInformado.Should().Be("+5511987654321");
        conversa.ContatoEmailInformado.Should().Be("maria@exemplo.com");
        conversa.ContatoInformadoEm.Should().Be(Agora);
        conversa.ClienteId.Should().BeNull("o dado informado nunca liga a conversa a um cliente");
    }

    [Fact]
    public void Conversa_ComCliente_MantemONomeDoCadastro()
    {
        var conversa = Conversa.Abrir(Guid.NewGuid(), "sessao-1", Agora, "Maria Cadastrada", Guid.NewGuid(), CanalConversa.ChatSite);

        conversa.RegistrarContatoInformado(ContatoInformadoVisitante.Criar("Outra Pessoa", "11987654321", null, true, Agora));

        conversa.ContatoNome.Should().Be("Maria Cadastrada");
        conversa.ContatoTelefoneInformado.Should().Be("+5511987654321");
    }
}
