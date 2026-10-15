using System.Text.Json;
using EasyStock.Api.Controllers;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public sealed class EntregasDemonstracaoControllerTests
{
    [Fact]
    public void Link_assinado_valido_recupera_apenas_id_do_despacho()
    {
        using var cenario = new Cenario();
        Assert.Equal(cenario.DespachoId, cenario.Links.Validar(cenario.Token()));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("validade")]
    [InlineData("assinatura")]
    public void Token_adulterado_e_rejeitado(string campo)
    {
        using var cenario = new Cenario();
        var partes = cenario.Token().Split('.');
        if (campo == "id") partes[0] = Guid.NewGuid().ToString("N");
        if (campo == "validade") partes[1] = (long.Parse(partes[1]) + 600).ToString();
        if (campo == "assinatura") partes[2] = (partes[2][0] == '0' ? "1" : "0") + partes[2][1..];
        Assert.Null(cenario.Links.Validar(string.Join('.', partes)));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void Token_expira_no_limite_e_depois_dos_sete_dias(int dias)
    {
        using var cenario = new Cenario();
        var token = cenario.Token();
        cenario.Clock.Avancar(TimeSpan.FromDays(dias));
        Assert.Null(cenario.Links.Validar(token));
    }

    [Fact]
    public void Token_nao_e_transferivel_para_outro_tenant_nem_outra_chave()
    {
        using var cenario = new Cenario();
        var token = cenario.Token();
        var outroTenant = new RastreioLogisticaLink(cenario.Config, cenario.LogisticaConfig with { EmpresaDemonstracaoId = Guid.NewGuid() }, cenario.Clock);
        Assert.Null(outroTenant.Validar(token));
        var outraChave = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SecretKey"] = "outra-chave-de-teste" }).Build();
        Assert.Null(new RastreioLogisticaLink(outraChave, cenario.LogisticaConfig, cenario.Clock).Validar(token));
        Assert.Null(new RastreioLogisticaLink(cenario.Config, cenario.LogisticaConfig with { EmpresaDemonstracaoId = Guid.Empty }, cenario.Clock).Validar(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sem-assinatura")]
    [InlineData("00000000000000000000000000000000.999999999999999999999999.00")]
    public async Task Token_invalido_retorna_404_sem_consultar_banco(string token)
    {
        using var cenario = new Cenario();
        Assert.IsType<NotFoundResult>(await cenario.Controller.Obter(token, default));
        Assert.Empty(cenario.Repository.ReceivedCalls());
        Assert.Equal(Guid.Empty, cenario.Db.CurrentTenantId);
    }

    [Fact]
    public async Task Assinatura_hexadecimal_invalida_retorna_404()
    {
        using var cenario = new Cenario();
        var partes = cenario.Token().Split('.');
        partes[2] = new string('z', 64);
        Assert.IsType<NotFoundResult>(await cenario.Controller.Obter(string.Join('.', partes), default));
        Assert.Empty(cenario.Repository.ReceivedCalls());
    }

    [Fact]
    public async Task Link_valido_sem_despacho_retorna_404_no_tenant_da_demo()
    {
        using var cenario = new Cenario();
        Assert.IsType<NotFoundResult>(await cenario.Controller.Obter(cenario.Token(), default));
        Assert.Equal(cenario.Empresa, cenario.Db.CurrentTenantId);
        await cenario.Repository.Received(1).DespachoAsync(cenario.Empresa, cenario.DespachoId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Repositorio_retornando_outro_tenant_nao_vaza_dados()
    {
        using var cenario = new Cenario();
        cenario.Repository.DespachoAsync(cenario.Empresa, cenario.DespachoId, Arg.Any<CancellationToken>())
            .Returns(new DespachoLogistica { Id = cenario.DespachoId, EmpresaId = Guid.NewGuid() });
        Assert.IsType<NotFoundResult>(await cenario.Controller.Obter(cenario.Token(), default));
    }

    [Fact]
    public async Task Pagina_publica_exibe_somente_status_simulado_sem_pii_custo_motivo_ou_script()
    {
        using var cenario = new Cenario();
        const string dadoPrivado = "Maria, Rua Privada 123, +5511999990000, valor 987.65 <script>alert(1)</script>";
        var despacho = new DespachoLogistica
        {
            Id = cenario.DespachoId, EmpresaId = cenario.Empresa, Simulado = true, ValorCentavos = 98765,
            ReferenciaPagamento = dadoPrivado, RastreioUrl = "https://privado.invalid/segredo", PedidoIdsJson = JsonSerializer.Serialize(new[] { Guid.NewGuid() }),
            EventosJson = JsonSerializer.Serialize(new[]
            {
                new LogisticaEvento("agendado", dadoPrivado, DateTime.UtcNow, "chave-interna-privada"),
                new LogisticaEvento("pagamento_confirmado", dadoPrivado, DateTime.UtcNow, "pagamento-privado"),
                new LogisticaEvento("<script>alert(2)</script>", dadoPrivado, DateTime.UtcNow, "evento-privado")
            })
        };
        cenario.Repository.DespachoAsync(cenario.Empresa, cenario.DespachoId, Arg.Any<CancellationToken>()).Returns(despacho);
        var result = Assert.IsType<ContentResult>(await cenario.Controller.Obter(cenario.Token(), default));
        Assert.Equal("text/html; charset=utf-8", result.ContentType);
        Assert.Contains("Aguardando coleta", result.Content);
        Assert.Contains("demonstração", result.Content);
        Assert.Contains("transportadores são simulados", result.Content);
        Assert.DoesNotContain(dadoPrivado, result.Content);
        Assert.DoesNotContain("98765", result.Content);
        Assert.DoesNotContain("chave-interna-privada", result.Content);
        Assert.DoesNotContain("<script>", result.Content);
        Assert.DoesNotContain("privado.invalid", result.Content);
        Assert.DoesNotContain("Transporte confirmado", result.Content);
        Assert.Equal("no-referrer", cenario.Controller.Response.Headers["Referrer-Policy"].ToString());
        Assert.Equal("noindex, nofollow", cenario.Controller.Response.Headers["X-Robots-Tag"].ToString());
        Assert.Contains("default-src 'none'", cenario.Controller.Response.Headers["Content-Security-Policy"].ToString());
        var cache = Assert.Single(typeof(EntregasDemonstracaoController).GetMethod(nameof(EntregasDemonstracaoController.Obter))!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), false).Cast<ResponseCacheAttribute>());
        Assert.True(cache.NoStore);
    }

    private sealed class Cenario : IDisposable
    {
        public Guid Empresa { get; } = Guid.NewGuid();
        public Guid DespachoId { get; } = Guid.NewGuid();
        public Relogio Clock { get; } = new();
        public IConfiguration Config { get; } = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Jwt:SecretKey"] = "segredo-exclusivamente-unitario-sem-credencial-real" }).Build();
        public ConfiguracaoLogistica LogisticaConfig { get; }
        public ILogisticaRepository Repository { get; } = Substitute.For<ILogisticaRepository>();
        public EasyStockDbContext Db { get; }
        public RastreioLogisticaLink Links { get; }
        public EntregasDemonstracaoController Controller { get; }
        public Cenario()
        {
            LogisticaConfig = new(Empresa, new("Demo", "Origem", "5500000000000", -23.5, -46.6));
            Links = new(Config, LogisticaConfig, Clock);
            Db = new(new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase("rastreio-" + Guid.NewGuid()).Options,
                Substitute.For<ICurrentUserAccessor>());
            Controller = new(Links, LogisticaConfig, new(Repository, LogisticaConfig), Db)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        }
        public string Token() => Links.Criar(DespachoId).Split('/')[^1];
        public void Dispose() => Db.Dispose();
    }

    private sealed class Relogio : TimeProvider
    {
        private DateTimeOffset agora = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => agora;
        public void Avancar(TimeSpan tempo) => agora += tempo;
    }
}
