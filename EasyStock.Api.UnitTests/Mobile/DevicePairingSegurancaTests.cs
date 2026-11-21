using System.Reflection;
using System.Text.Json;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2367: o segredo do pair-auto vem embutido no APK. Quem o extrai consegue trocar a chave de um aparelho
/// ativo e derrubá-lo. A rotação continua (o "Desparear" do Diagnóstico depende dela), mas fica limitada
/// a 1 a cada 10 min por aparelho, avisa a dona no sino e exige segredo de 32 caracteres ou mais.
/// </summary>
public sealed class DevicePairingSegurancaTests : IDisposable
{
    private const string SegredoForte = "segredo-de-teste-com-mais-de-32-caracteres";
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();

    public DevicePairingSegurancaTests()
    {
        var anonimo = Substitute.For<ICurrentUserAccessor>();
        anonimo.IsAuthenticated.Returns(false);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"pairing-seguranca-{Guid.NewGuid()}")
                .Options,
            anonimo);
        _db.Set<Empresa>().Add(new Empresa { Id = _empresaId, Nome = "Casa da Baba Ltda" });
        _db.Set<Loja>().Add(new Loja { Id = _lojaId, EmpresaId = _empresaId, Nome = "Centro", Ativa = true });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private DevicePairingController Controller(string segredo = SegredoForte) =>
        new(_db, Substitute.For<ICurrentUserAccessor>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppProvisioning:Secret"] = segredo,
                ["AppProvisioning:EmpresaId"] = _empresaId.ToString(),
                ["AppProvisioning:LojaId"] = _lojaId.ToString(),
            }).Build(),
            NullLogger<DevicePairingController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private async Task AparelhoAtivoAsync(DateTime pareadoEm)
    {
        _db.Set<MobileDevice>().Add(new MobileDevice
        {
            Id = "dev-tablet", ApiKeyHash = "chave-atual", EmpresaId = _empresaId, LojaId = _lojaId,
            Label = "Tablet balcão", PairedAt = pareadoEm, CreatedAt = pareadoEm, UpdatedAt = pareadoEm,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task PairAuto_com_segredo_curto_fica_desligado()
    {
        const string curto = "segredo-apenas-para-teste"; // 25 caracteres

        var result = await Controller(curto).PairAuto(new PairAutoRequest(curto, "dev-novo", null), CancellationToken.None);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
        (await _db.Set<MobileDevice>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task PairAuto_recusa_rotacionar_aparelho_pareado_ha_menos_de_10_minutos()
    {
        await AparelhoAtivoAsync(DateTime.UtcNow.AddMinutes(-2));

        var result = await Controller().PairAuto(new PairAutoRequest(SegredoForte, "dev-tablet", null), CancellationToken.None);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        (await _db.Set<MobileDevice>().AsNoTracking().SingleAsync()).ApiKeyHash.Should().Be("chave-atual");
        (await _db.Set<Notificacao>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task PairAuto_que_troca_a_chave_de_aparelho_ativo_avisa_a_dona_no_sino()
    {
        await AparelhoAtivoAsync(DateTime.UtcNow.AddHours(-1));

        var result = await Controller().PairAuto(new PairAutoRequest(SegredoForte, "dev-tablet", null), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        (await _db.Set<MobileDevice>().AsNoTracking().SingleAsync()).ApiKeyHash.Should().NotBe("chave-atual");
        var aviso = await _db.Set<Notificacao>().AsNoTracking().SingleAsync();
        aviso.EmpresaId.Should().Be(_empresaId);
        aviso.TipoAlerta.Should().Be(TipoAlertaEstoque.AvisoOperacional);
        aviso.Mensagem.Should().Contain("Tablet balcão");
    }

    [Fact]
    public async Task PairAuto_de_aparelho_novo_nao_gera_aviso()
    {
        var result = await Controller().PairAuto(new PairAutoRequest(SegredoForte, "dev-novo", null), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        (await _db.Set<Notificacao>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task DiagTrace_nao_revela_se_o_aparelho_existe()
    {
        await AparelhoAtivoAsync(DateTime.UtcNow.AddHours(-1));
        var controller = new MobileDiagTraceController(NullLogger<MobileDiagTraceController>.Instance);
        var trace = JsonDocument.Parse("[]").RootElement;

        var result = await controller.UploadTrace(
            new MobileDiagTraceController.TraceUploadRequest("dev-tablet", null, null, null, null, "ua\r\nFAKE", trace, "nota\nFAKE"),
            CancellationToken.None);

        var corpo = JsonSerializer.Serialize(result.Should().BeOfType<OkObjectResult>().Which.Value);
        corpo.Should().NotContain("deviceKnown");
    }

    [Fact]
    public void DiagTrace_limita_o_tamanho_do_corpo()
    {
        var limite = typeof(MobileDiagTraceController).GetMethod(nameof(MobileDiagTraceController.UploadTrace))!
            .GetCustomAttribute<RequestSizeLimitAttribute>();

        limite.Should().NotBeNull();
    }

    [Theory]
    [InlineData("linha\r\nFORJADA", "linha  FORJADA")]
    [InlineData(null, "")]
    public void DiagTrace_tira_quebra_de_linha_do_que_vai_para_o_log(string? entrada, string esperado) =>
        MobileDiagTraceController.ParaLog(entrada, 200).Should().Be(esperado);
}
