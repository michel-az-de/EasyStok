using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2461: a conversa informa se a pessoa do cadastro já fez pedido (<c>temPedido</c>), na lista e no estado do
/// vínculo. A regra é a das abas de Cadastros (<c>ClienteRepository.GetByEmpresaAsync</c>): qualquer pedido do
/// cliente na empresa conta, inclusive cancelado; pedido de outra empresa não conta.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class LeadSemPedidoIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    // As mesmas opções da API (CoreMvcExtensions).
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [SkippableFact]
    public async Task Lista_InformaTemPedido_SemCadastroSemPedidoComPedidoESoCancelado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var c = await SemearAsync();

        await using var db = Contexto(c.EmpresaId);
        var resumos = await Inbox(db).ExecuteAsync(new ListarConversasAtendimentoQuery(c.EmpresaId, null, null, 1, 10));

        resumos.Should().HaveCount(4);
        TemPedido(c.SemCadastro).Should().BeNull("sem cadastro não há de quem contar pedido");
        TemPedido(c.DoLead).Should().BeFalse("cadastro sem pedido é lead");
        TemPedido(c.DoCliente).Should().BeTrue();
        TemPedido(c.DoSoCancelado).Should().BeTrue("pedido cancelado conta, como na aba Clientes de Cadastros");

        // O console lê `temPedido` em camelCase (traducaoConversas.js); nulo sai como null, não some.
        JsonDe(c.SemCadastro)["temPedido"].Should().BeNull();
        JsonDe(c.SemCadastro).ContainsKey("temPedido").Should().BeTrue();
        JsonDe(c.DoLead)["temPedido"]!.GetValue<bool>().Should().BeFalse();
        JsonDe(c.DoSoCancelado)["temPedido"]!.GetValue<bool>().Should().BeTrue();

        bool? TemPedido(Conversa conversa) => resumos.Single(r => r.Id == conversa.Id).TemPedido;
        System.Text.Json.Nodes.JsonObject JsonDe(Conversa conversa) =>
            JsonSerializer.SerializeToNode(resumos.Single(r => r.Id == conversa.Id), Json)!.AsObject();
    }

    [SkippableFact]
    public async Task Lista_PedidoDeOutraEmpresaComOMesmoCliente_NaoConta()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var c = await SemearAsync();
        await SemearPedidoAlheioAsync(c);

        await using var db = Contexto(c.EmpresaId);
        var resumos = await Inbox(db).ExecuteAsync(new ListarConversasAtendimentoQuery(c.EmpresaId, null, null, 1, 10));

        resumos.Single(r => r.Id == c.DoLead.Id).TemPedido.Should().BeFalse("o pedido é de outra empresa");
    }

    [SkippableFact]
    public async Task Vinculo_InformaTemPedido_ERespondeODoClienteNovoDepoisDeVincular()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var c = await SemearAsync();
        await SemearPedidoAlheioAsync(c);

        await using var db = Contexto(c.EmpresaId);
        var caso = Vinculo(db, c.EmpresaId);

        (await caso.ObterAsync(c.EmpresaId, c.SemCadastro.Id)).TemPedido.Should().BeNull();
        (await caso.ObterAsync(c.EmpresaId, c.DoCliente.Id)).TemPedido.Should().BeTrue();
        (await caso.ObterAsync(c.EmpresaId, c.DoSoCancelado.Id)).TemPedido.Should().BeTrue();
        var doLead = await caso.ObterAsync(c.EmpresaId, c.DoLead.Id);
        doLead.TemPedido.Should().BeFalse("o pedido de outra empresa não conta");

        var retirada = await caso.ExecuteAsync(new(c.EmpresaId, c.DoLead.Id, Guid.NewGuid(), doLead.VinculoRevisao,
            c.LeadId, "retirar", null, "Pessoa conferida no atendimento"));
        retirada.TemPedido.Should().BeNull("sem vínculo não há cadastro");

        var vinculada = await caso.ExecuteAsync(new(c.EmpresaId, c.DoLead.Id, Guid.NewGuid(), retirada.VinculoRevisao,
            null, "vincular", c.SoCanceladoId, "Outra pessoa confirmada"));
        vinculada.ClienteId.Should().Be(c.SoCanceladoId);
        vinculada.TemPedido.Should().BeTrue("a resposta da correção já traz o do cliente novo");
        JsonSerializer.SerializeToNode(vinculada, Json)!["temPedido"]!.GetValue<bool>().Should().BeTrue();

        var deVoltaAoLead = await caso.ExecuteAsync(new(c.EmpresaId, c.SemCadastro.Id, Guid.NewGuid(), 0,
            null, "vincular", c.LeadId, "Pessoa confirmada no atendimento"));
        deVoltaAoLead.TemPedido.Should().BeFalse();
    }

    private EasyStockDbContext Contexto(Guid empresaId)
    {
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return db;
    }

    private static ListarConversasAtendimentoUseCase Inbox(EasyStockDbContext db)
    {
        var configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
        configuracoes.GetByEmpresaIdAsync(Arg.Any<Guid>()).Returns(ci => ConfiguracaoAtendimento.CriarPadrao(ci.Arg<Guid>()));
        return new ListarConversasAtendimentoUseCase(
            new ConversaRepository(db), configuracoes, Options.Create(new PrazosOptions { ClienteSemRespostaMin = 7 }));
    }

    private static VinculoClienteConversaUseCase Vinculo(EasyStockDbContext db, Guid empresaId)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(empresaId);
        usuario.UsuarioId.Returns(Guid.NewGuid());
        usuario.IsAuthenticated.Returns(true);
        usuario.Nivel.Returns(NivelAcesso.Admin);
        usuario.TemPermissao(Arg.Any<Permissao>()).Returns(true);
        return new(new ConversaRepository(db), new ClienteRepository(db), new VinculoClienteConversaRepository(db),
            db, usuario, TimeProvider.System);
    }

    private sealed record Cenario(Guid EmpresaId, Guid OutraEmpresaId, Guid LeadId, Guid SoCanceladoId,
        Conversa SemCadastro, Conversa DoLead, Conversa DoCliente, Conversa DoSoCancelado);

    /// <summary>Quatro conversas da empresa, uma por caso da regra. Nenhuma tem pedido na própria conversa.</summary>
    private async Task<Cenario> SemearAsync()
    {
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Lead sem pedido", null);
        var outra = Empresa.Criar("Lead sem pedido outra", null);
        var lead = Cliente.Criar(empresa.Id, "Cadastro sem pedido");
        var cliente = Cliente.Criar(empresa.Id, "Cadastro com pedido");
        var soCancelado = Cliente.Criar(empresa.Id, "Cadastro com pedido cancelado");
        lead.Ativo = cliente.Ativo = soCancelado.Ativo = true;
        var pedido = Pedido.Criar(empresa.Id, cliente);
        var cancelado = Pedido.Criar(empresa.Id, soCancelado);
        cancelado.Cancelar();

        // ChatSite: a correção do vínculo não grava decisão por telefone (só o WhatsApp grava).
        Conversa Abrir(int minutos, Guid? clienteId) => Conversa.Abrir(
            empresa.Id, Guid.NewGuid().ToString("N"), agora.AddMinutes(-minutos), null, clienteId, CanalConversa.ChatSite);
        var semCadastro = Abrir(4, null);
        var doLead = Abrir(3, lead.Id);
        var doCliente = Abrir(2, cliente.Id);
        var doSoCancelado = Abrir(1, soCancelado.Id);

        await using var seed = fixture.CreateDbContext();
        using var bypass = seed.UseRowLevelSecurityBypass();
        seed.Empresas.AddRange(empresa, outra);
        seed.Clientes.AddRange(lead, cliente, soCancelado);
        seed.Pedidos.AddRange(pedido, cancelado);
        seed.AtendimentoConversas.AddRange(semCadastro, doLead, doCliente, doSoCancelado);
        await seed.SaveChangesAsync();

        return new Cenario(empresa.Id, outra.Id, lead.Id, soCancelado.Id, semCadastro, doLead, doCliente, doSoCancelado);
    }

    /// <summary>Pedido de outra empresa apontando para o cadastro do lead: o filtro por empresa tem de ignorá-lo.</summary>
    private async Task SemearPedidoAlheioAsync(Cenario c)
    {
        var alheio = Pedido.Criar(c.OutraEmpresaId);
        alheio.ClienteId = c.LeadId;
        await using var seed = fixture.CreateDbContext();
        using var bypass = seed.UseRowLevelSecurityBypass();
        seed.Pedidos.Add(alheio);
        await seed.SaveChangesAsync();
    }
}
