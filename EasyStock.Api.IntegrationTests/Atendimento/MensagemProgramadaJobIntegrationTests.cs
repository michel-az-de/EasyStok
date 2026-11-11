using DotNet.Testcontainers.Builders;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>
/// #2285: o disparador das mensagens programadas (<c>MensagensProgramadasBackgroundService</c>) contra Postgres real, com o
/// serviço de verdade subindo junto da Api e rodando a cada 1 s. Só a porta do canal é dublê: nada sai para a rede.
/// </summary>
public sealed class MensagemProgramadaJobIntegrationTests : IAsyncLifetime
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();
    private const string EmailDoCliente = "maria@cliente.test";

    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_programadas_job_tests")
                .WithUsername("postgres")
                .WithPassword("senha-de-teste-descartavel")
                .Build();
            await _pg.StartAsync();
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    [SkippableFact]
    public async Task Job_MensagemAgendadaVence_EnviaUmaVezEGravaEstadoEHistorico()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var canal = CanalFalso();
        canal.EnviarTextoAsync(EmailDoCliente, "Seu bolo está pronto", Arg.Any<CancellationToken>()).Returns("ext-programada-1");
        await using var factory = CriarFactory(canal);
        var (mensagemId, conversaId) = await SemearAsync(factory, "Seu bolo está pronto", venceEmSegundos: 4);

        (await LerAsync(factory, mensagemId)).Situacao.Should().Be(SituacaoMensagemProgramada.Agendada, "ainda não venceu");
        await canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);

        var enviada = await AguardarAsync(factory, mensagemId, m => m.Situacao == SituacaoMensagemProgramada.Enviada);
        enviada.IdExterno.Should().Be("ext-programada-1");
        enviada.EnviadaEm.Should().NotBeNull();
        enviada.Tentativas.Should().Be(1);
        enviada.Erro.Should().BeNull();

        // Algumas rodadas a mais: a mensagem enviada não volta à fila.
        await Task.Delay(TimeSpan.FromSeconds(3));
        await canal.Received(1).EnviarTextoAsync(EmailDoCliente, "Seu bolo está pronto", Arg.Any<CancellationToken>());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var saidas = await db.AtendimentoMensagens.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ConversaId == conversaId && m.Direcao == DirecaoMensagem.Saida).ToListAsync();
        var saida = saidas.Should().ContainSingle("o histórico recebe a saída uma vez só").Subject;
        saida.Texto.Should().Be("Seu bolo está pronto");
        saida.ExternoId.Should().Be("ext-programada-1");
        saida.Programada.Should().BeTrue("a saída leva o selo de programada");
    }

    [SkippableFact]
    public async Task Job_FalhaDoProvedor_GravaFalhouNaoReenviaENaoEscreveNoHistorico()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var canal = CanalFalso();
        canal.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("provedor fora do ar"));
        await using var factory = CriarFactory(canal);
        var (mensagemId, conversaId) = await SemearAsync(factory, "Lembrete do pedido", venceEmSegundos: 2);

        var falhou = await AguardarAsync(factory, mensagemId, m => m.Situacao == SituacaoMensagemProgramada.Falhou);
        falhou.Erro.Should().NotBeNullOrWhiteSpace();
        falhou.IdExterno.Should().BeNull();
        falhou.Tentativas.Should().Be(1);

        // Várias rodadas depois, a falha não vira nova tentativa: o provedor foi chamado uma vez só.
        await Task.Delay(TimeSpan.FromSeconds(4));
        await canal.Received(1).EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var depois = await LerAsync(factory, mensagemId);
        depois.Situacao.Should().Be(SituacaoMensagemProgramada.Falhou);
        depois.Tentativas.Should().Be(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.AtendimentoMensagens.IgnoreQueryFilters()
            .CountAsync(m => m.ConversaId == conversaId && m.Direcao == DirecaoMensagem.Saida))
            .Should().Be(0, "mensagem que não saiu não entra no histórico");
    }

    private static ICanalMensageria CanalFalso()
    {
        var canal = Substitute.For<ICanalMensageria>();
        canal.Canal.Returns(CanalConversa.Email);
        return canal;
    }

    private static async Task<(Guid MensagemId, Guid ConversaId)> SemearAsync(
        WebApplicationFactory<Program> factory, string texto, int venceEmSegundos)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var agora = DateTime.UtcNow;
        db.SetMobileTenantContext(EmpresaId);
        db.Empresas.Add(new Empresa
        {
            Id = EmpresaId, Nome = "Empresa Programadas", Documento = EmpresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora,
        });
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Maria", Email = EmailDoCliente };
        db.Clientes.Add(cliente);
        var conversa = Conversa.Abrir(EmpresaId, EmailDoCliente, agora, "Maria", cliente.Id, CanalConversa.Email);
        db.AtendimentoConversas.Add(conversa);
        var mensagem = MensagemProgramada.Agendar(EmpresaId, cliente.Id, conversa.Id, CanalConversa.Email,
            FinalidadeContato.Transacional, texto, null, agora.AddSeconds(venceEmSegundos), UsuarioId, agora.AddHours(-1));
        db.MensagensProgramadas.Add(mensagem);
        await db.SaveChangesAsync();
        return (mensagem.Id, conversa.Id);
    }

    private static async Task<MensagemProgramada> LerAsync(WebApplicationFactory<Program> factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        return await db.MensagensProgramadas.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private static async Task<MensagemProgramada> AguardarAsync(
        WebApplicationFactory<Program> factory, Guid id, Func<MensagemProgramada, bool> pronto)
    {
        var limite = DateTime.UtcNow.AddSeconds(40);
        while (true)
        {
            var atual = await LerAsync(factory, id);
            if (pronto(atual)) return atual;
            if (DateTime.UtcNow > limite) throw new TimeoutException($"A mensagem ficou em {atual.Situacao}.");
            await Task.Delay(300);
        }
    }

    private WebApplicationFactory<Program> CriarFactory(ICanalMensageria canal)
    {
        if (_pg is null) throw new InvalidOperationException("Postgres test container indisponível.");

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Database:Provider", "PostgreSql");
            b.UseSetting("ConnectionStrings:DefaultConnection", _pg!.GetConnectionString());
            b.UseSetting("Atendimento:MensagensProgramadas:PollingIntervalSeconds", "1");
            b.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "PostgreSql",
                    ["ConnectionStrings:DefaultConnection"] = _pg!.GetConnectionString(),
                    ["ConnectionStrings:Redis"] = "localhost:6379",
                    ["Jwt:Issuer"] = "EasyStock",
                    ["Jwt:Audience"] = "EasyStock",
                    ["Jwt:SecretKey"] = "EasyStock-Test-SuperSecretKey-Min32Chars!!",
                    ["Jwt:ExpirationMinutes"] = "60",
                    ["Anthropic:Enabled"] = "false",
                    ["FileStorage:Provider"] = "Local",
                });
            });
            b.ConfigureTestServices(s =>
            {
                // O ResolvedorCanal indexa por canal: o adaptador de e-mail de verdade sai e entra o dublê.
                foreach (var d in s.Where(d => d.ServiceType == typeof(ICanalMensageria)).ToList()) s.Remove(d);
                s.AddSingleton(canal);
            });
        });
        _ = factory.Server; // sobe o host (e o disparador) antes de semear
        return factory;
    }
}
