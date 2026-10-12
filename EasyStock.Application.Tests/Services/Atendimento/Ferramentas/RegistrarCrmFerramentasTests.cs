using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>
/// S24: <c>registrar_restricao</c> e <c>registrar_nota</c>. O commit é do turno do agente, no fim:
/// a ferramenta só altera o que está rastreado.
/// </summary>
public class RegistrarCrmFerramentasTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();
    private readonly Cliente _cliente;
    private readonly Conversa _conversa;

    public RegistrarCrmFerramentasTests()
    {
        _cliente = Cliente.Criar(_empresaId, "Maria");
        _crm.ObterComTagsAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns(_cliente);
        _conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria", _cliente.Id);
    }

    private ContextoTurnoAgente Contexto(Conversa? conversa = null) => new(_empresaId, conversa ?? _conversa, Agora);

    [Fact]
    public async Task RestricaoViraTagComOrigemAgente()
    {
        var ferramenta = new RegistrarRestricaoFerramenta(_crm);

        var resultado = await ferramenta.ExecutarAsync(Contexto(), JsonSerializer.SerializeToElement(new { tag = "Intolerante Lactose" }));

        resultado.Should().Contain("intolerante_lactose");
        var tag = _cliente.Tags.Should().ContainSingle().Subject;
        tag.Tag.Should().Be("intolerante_lactose");
        tag.Origem.Should().Be(OrigemClienteTag.Agente);
        tag.CriadoEm.Should().Be(Agora);
    }

    [Fact]
    public async Task RestricaoRepetidaEhNoOp()
    {
        _cliente.AdicionarTag("vegano", OrigemClienteTag.Dona, Agora.AddDays(-3));
        var ferramenta = new RegistrarRestricaoFerramenta(_crm);

        var resultado = await ferramenta.ExecutarAsync(Contexto(), JsonSerializer.SerializeToElement(new { tag = "vegano" }));

        resultado.Should().Contain("ja_registrada");
        _cliente.Tags.Should().ContainSingle().Which.Origem.Should().Be(OrigemClienteTag.Dona);
    }

    [Fact]
    public async Task RestricaoSemClienteDevolveErro()
    {
        var anonima = Conversa.Abrir(_empresaId, "5511911112222", Agora);
        var ferramenta = new RegistrarRestricaoFerramenta(_crm);

        var resultado = await ferramenta.ExecutarAsync(Contexto(anonima), JsonSerializer.SerializeToElement(new { tag = "vegano" }));

        resultado.Should().Contain("cliente_nao_identificado");
    }

    [Fact]
    public async Task RestricaoInvalidaDevolveErroSemLancar()
    {
        var ferramenta = new RegistrarRestricaoFerramenta(_crm);

        var resultado = await ferramenta.ExecutarAsync(Contexto(), JsonSerializer.SerializeToElement(new { tag = "!!!" }));

        resultado.Should().Contain("tag_invalida");
        _cliente.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task NotaEhGravadaComAutorAgenteSemEcoarOTexto()
    {
        var ferramenta = new RegistrarNotaFerramenta(_crm);

        var resultado = await ferramenta.ExecutarAsync(Contexto(),
            JsonSerializer.SerializeToElement(new { texto = "prefere retirar depois das 18h" }));

        resultado.Should().NotContain("18h");
        await _crm.Received(1).AdicionarNotaAsync(
            Arg.Is<ClienteNota>(n => n.ClienteId == _cliente.Id && n.EmpresaId == _empresaId
                                     && n.Autor == RegistrarNotaFerramenta.Autor && n.CriadoEm == Agora),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotaSemClienteDevolveErro()
    {
        var anonima = Conversa.Abrir(_empresaId, "5511911112222", Agora);
        var ferramenta = new RegistrarNotaFerramenta(_crm);

        var resultado = await ferramenta.ExecutarAsync(Contexto(anonima), JsonSerializer.SerializeToElement(new { texto = "x" }));

        resultado.Should().Contain("cliente_nao_identificado");
        await _crm.DidNotReceiveWithAnyArgs().AdicionarNotaAsync(default!, default);
    }
}
