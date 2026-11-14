using EasyStock.Infra.Integrations.WhatsApp;
using EasyStock.Infra.Integrations.UnitTests.WhatsApp.Waha;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

/// <summary>#2515: menu numerado da conexão provisória, guardado por sessão e contato.</summary>
public class MenuNumeradoWppConnectTests
{
    private const string Sessao = "ez-c8ebd039563e4f34a606c5f7a788c156";
    private const string Contato = "5511999999999";

    private static MenuNumeradoWppConnect Menus() => new(WahaFalso.Cache(), NullLogger<MenuNumeradoWppConnect>.Instance);

    [Fact]
    public async Task RespostaNumericaEscolheAOpcaoUmaVezSo()
    {
        var menus = Menus();
        await menus.GuardarAsync(Sessao, Contato, [("acao:escolher_janela:a:2026-11-14", "14/11 09:00-12:00"),
            ("acao:escolher_janela:b:2026-11-14", "14/11 12:00-15:00")]);

        (await menus.EscolhaAsync(Sessao, Contato, "2")).Should().Be(("acao:escolher_janela:b:2026-11-14", "14/11 12:00-15:00"));
        (await menus.EscolhaAsync(Sessao, Contato, "2")).Should().BeNull();
    }

    [Fact]
    public async Task OutroContatoOutraSessaoOuTextoLivreNaoEscolhem()
    {
        var menus = Menus();
        await menus.GuardarAsync(Sessao, Contato, [("a", "Sim"), ("b", "Não")]);

        (await menus.EscolhaAsync(Sessao, "5511888888888", "1")).Should().BeNull();
        (await menus.EscolhaAsync("ez-00000000000000000000000000000001", Contato, "1")).Should().BeNull();
        (await menus.EscolhaAsync(Sessao, Contato, "quero 2 lasanhas")).Should().BeNull();
        (await menus.EscolhaAsync(Sessao, Contato, "1")).Should().Be(("a", "Sim"));
    }
}
