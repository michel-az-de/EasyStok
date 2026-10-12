using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>
/// #1290: a conversa guarda o <c>wa_id</c> como a Meta entrega (celular antigo sem o nono dígito,
/// <c>551197573992</c>), mas aviso e mensagem programada procuram pelo cadastro (<c>+5511997573992</c>).
/// O lookup da conversa aberta do WhatsApp precisa casar as duas grafias. InMemory basta: o
/// <c>Contains</c> e a ordenação são LINQ puro.
/// </summary>
public sealed class ConversaRepositoryContatoTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly EasyStockDbContext _db;
    private readonly ConversaRepository _repo;

    public ConversaRepositoryContatoTests()
    {
        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"conversa-contato-{Guid.NewGuid()}")
            .Options);
        _db.SetMobileTenantContext(_empresaId);
        _repo = new ConversaRepository(_db);
    }

    private async Task<Conversa> GravarAsync(string contato, CanalConversa canal = CanalConversa.WhatsApp, bool encerrada = false)
    {
        var conversa = Conversa.Abrir(_empresaId, contato, Agora, canal: canal);
        if (encerrada) conversa.Encerrar(Agora.AddMinutes(1));
        _db.AtendimentoConversas.Add(conversa);
        await _db.SaveChangesAsync();
        return conversa;
    }

    [Theory]
    [InlineData("551197573992", "+5511997573992")] // gravada pelo wa_id antigo, procurada pelo cadastro
    [InlineData("5511997573992", "551197573992")] // gravada com o 9, procurada pelo wa_id antigo
    public async Task WhatsAppAchaPelaOutraGrafiaDoNonoDigito(string gravado, string procurado)
    {
        var conversa = await GravarAsync(gravado);

        var achada = await _repo.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, procurado);

        achada.Should().NotBeNull();
        achada!.Id.Should().Be(conversa.Id);
    }

    [Fact]
    public async Task WhatsAppComAsDuasGrafiasAbertasPrefereAExata()
    {
        await GravarAsync("551197573992");
        var exata = await GravarAsync("5511997573992");

        var achada = await _repo.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, "+5511997573992");

        achada!.Id.Should().Be(exata.Id);
    }

    [Fact]
    public async Task WhatsAppIgnoraConversaEncerradaDaOutraGrafia()
    {
        await GravarAsync("551197573992", encerrada: true);

        var achada = await _repo.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, "+5511997573992");

        achada.Should().BeNull();
    }

    [Fact]
    public async Task SmsContinuaPorIgualdadeExata()
    {
        await GravarAsync("551197573992", CanalConversa.Sms);

        var achada = await _repo.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.Sms, "+5511997573992");

        achada.Should().BeNull("a Meta só reescreve o nono dígito no WhatsApp");
    }

    public void Dispose() => _db.Dispose();
}
