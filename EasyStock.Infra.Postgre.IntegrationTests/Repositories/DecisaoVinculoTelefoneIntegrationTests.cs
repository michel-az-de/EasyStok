using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2492: a correção da dona numa conversa do WhatsApp vale para as próximas conversas do mesmo telefone.
/// A conversa nova nasce pelo mesmo caminho do webhook: identificação pelo wa_id na abertura.
/// </summary>
public sealed class DecisaoVinculoTelefoneIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string WaId = "5511999990001";
    private readonly Guid _ator = Guid.NewGuid();

    private EasyStockDbContext Contexto(Guid empresa)
    {
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        return db;
    }

    private VinculoClienteConversaUseCase Correcao(EasyStockDbContext db, Guid empresa)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(empresa);
        usuario.UsuarioId.Returns(_ator);
        usuario.IsAuthenticated.Returns(true);
        usuario.Nivel.Returns(NivelAcesso.Admin);
        usuario.TemPermissao(Arg.Any<Permissao>()).Returns(true);
        return new(new ConversaRepository(db), new ClienteRepository(db), new VinculoClienteConversaRepository(db),
            db, usuario, TimeProvider.System);
    }

    private static IdentificarClientePorTelefoneUseCase Identificar(EasyStockDbContext db) =>
        new(new ClienteRepository(db), new ClienteStorefrontRepository(db), new VinculoClienteConversaRepository(db),
            NullLogger<IdentificarClientePorTelefoneUseCase>.Instance);

    private sealed record Cenario(Guid Empresa, Guid Y, Guid Z, Guid ConversaId);

    /// <summary>Y tem o telefone do WhatsApp e está na conversa aberta; Z é a pessoa certa, com outro telefone.</summary>
    private async Task<Cenario> SemearAsync(string telefoneDeY = "+" + WaId)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var empresa = Empresa.Criar("Teste decisão " + Guid.NewGuid().ToString("N")[..6], null);
        await using var db = Contexto(empresa.Id);
        db.Empresas.Add(empresa);
        var y = Cliente.Criar(empresa.Id, "Pessoa errada");
        y.Telefone = telefoneDeY;
        var z = Cliente.Criar(empresa.Id, "Pessoa certa");
        z.Telefone = "+5511988880002";
        db.Clientes.AddRange(y, z);
        var conversa = Conversa.Abrir(empresa.Id, WaId, DateTime.UtcNow, "Contato", y.Id);
        db.AtendimentoConversas.Add(conversa);
        await db.SaveChangesAsync();
        return new(empresa.Id, y.Id, z.Id, conversa.Id);
    }

    private async Task CorrigirAsync(Cenario c, string acao, Guid? destino = null)
    {
        await using var db = Contexto(c.Empresa);
        var conversa = await db.AtendimentoConversas.AsNoTracking().SingleAsync(x => x.Id == c.ConversaId);
        await Correcao(db, c.Empresa).ExecuteAsync(new CorrigirVinculoClienteCommand(c.Empresa, c.ConversaId, Guid.NewGuid(),
            conversa.VinculoRevisao, conversa.ClienteId, acao, destino, "Pessoa conferida no atendimento"));
    }

    private async Task EncerrarAsync(Cenario c)
    {
        await using var db = Contexto(c.Empresa);
        (await db.AtendimentoConversas.SingleAsync(x => x.Id == c.ConversaId)).Encerrar(DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    /// <summary>Mesmo passo do webhook ao abrir a conversa nova (ProcessarEventoWhatsAppUseCase).</summary>
    private async Task<Conversa> NovaConversaAsync(Guid empresa, string waId = WaId)
    {
        await using var db = Contexto(empresa);
        var conversa = Conversa.Abrir(empresa, waId, DateTime.UtcNow, "Contato");
        var identificacao = await Identificar(db).ExecuteAsync(
            new IdentificarClientePorTelefoneInput(empresa, waId, "Contato", HonrarDecisaoDaEquipe: true));
        if (identificacao.Cliente is { } cliente) conversa.VincularCliente(cliente.Id);
        else conversa.ExigirConferenciaCliente();
        db.AtendimentoConversas.Add(conversa);
        await db.SaveChangesAsync();
        return conversa;
    }

    [SkippableFact]
    public async Task CorrigirDeYParaZFazAProximaConversaNascerComZ()
    {
        var c = await SemearAsync();
        await CorrigirAsync(c, "retirar");
        await CorrigirAsync(c, "vincular", c.Z);
        await EncerrarAsync(c);

        var nova = await NovaConversaAsync(c.Empresa);

        nova.ClienteId.Should().Be(c.Z);
        nova.VinculoEmRevisao.Should().BeFalse();
    }

    [SkippableFact]
    public async Task RetirarQuandoSoYTemOTelefoneDeixaAProximaSemClienteESemLeadNovo()
    {
        var c = await SemearAsync();
        await CorrigirAsync(c, "retirar");
        await EncerrarAsync(c);

        var nova = await NovaConversaAsync(c.Empresa);

        nova.ClienteId.Should().BeNull();
        nova.VinculoEmRevisao.Should().BeTrue();
        await using var db = Contexto(c.Empresa);
        (await db.Clientes.CountAsync()).Should().Be(2);
    }

    [SkippableFact]
    public async Task RestaurarRevogaADecisaoEAProximaVoltaParaY()
    {
        var c = await SemearAsync();
        await CorrigirAsync(c, "retirar");
        await CorrigirAsync(c, "restaurar");
        await EncerrarAsync(c);

        var nova = await NovaConversaAsync(c.Empresa);

        nova.ClienteId.Should().Be(c.Y);
        await using var db = Contexto(c.Empresa);
        var decisoes = await db.Set<DecisaoVinculoTelefone>().ToListAsync();
        decisoes.Should().ContainSingle().Which.RevogadaEm.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task WaIdSemNonoDigitoAchaADecisaoGravadaComNonoDigito()
    {
        var c = await SemearAsync();
        await CorrigirAsync(c, "retirar");
        await CorrigirAsync(c, "vincular", c.Z);
        await EncerrarAsync(c);

        var nova = await NovaConversaAsync(c.Empresa, "551199990001");

        nova.ClienteId.Should().Be(c.Z);
    }

    [SkippableFact]
    public async Task RlsIsolaADecisaoEntreEmpresasComOMesmoTelefone()
    {
        var primeira = await SemearAsync();
        var segunda = await SemearAsync();
        await CorrigirAsync(primeira, "retirar");
        await CorrigirAsync(primeira, "vincular", primeira.Z);
        await EncerrarAsync(segunda);

        var daSegunda = await NovaConversaAsync(segunda.Empresa);
        daSegunda.ClienteId.Should().Be(segunda.Y);

        await using var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {segunda.Empresa.ToString()}, false)");
        (await db.Set<DecisaoVinculoTelefone>().IgnoreQueryFilters().ToListAsync()).Should().BeEmpty();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {primeira.Empresa.ToString()}, false)");
        var daPrimeira = await db.Set<DecisaoVinculoTelefone>().IgnoreQueryFilters().ToListAsync();
        daPrimeira.Should().HaveCount(2).And.OnlyContain(d => d.EmpresaId == primeira.Empresa);
        daPrimeira.Should().ContainSingle(d => d.RevogadaEm == null).Which.ClienteEscolhidoId.Should().Be(primeira.Z);
    }
}
