using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Http;

public class ValidateEmpresaIdAttributeTests
{
    private static readonly Guid UserEmpresa   = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherEmpresa  = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── Query string ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Query_com_empresa_diferente_retorna_Forbid()
    {
        var ctx = BuildContext(query: $"empresaId={OtherEmpresa}");

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.Admin);

        ctx.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Query_com_mesma_empresa_passa()
    {
        var ctx = BuildContext(query: $"empresaId={UserEmpresa}");

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.Admin);

        ctx.Result.Should().BeNull();
    }

    // ── Action argument: Guid direto ─────────────────────────────────────────

    [Fact]
    public async Task ActionArgument_guid_direto_com_empresa_diferente_retorna_Forbid()
    {
        var ctx = BuildContext(actionArgs: new() { ["empresaId"] = OtherEmpresa });

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.Admin);

        ctx.Result.Should().BeOfType<ForbidResult>();
    }

    // ── Action argument: DTO com EmpresaId ──────────────────────────────────

    public sealed class CommandComEmpresaId
    {
        public Guid EmpresaId { get; set; }
        public string? Nome { get; set; }
    }

    [Fact]
    public async Task ActionArgument_dto_com_EmpresaId_diferente_retorna_Forbid()
    {
        var command = new CommandComEmpresaId { EmpresaId = OtherEmpresa, Nome = "X" };
        var ctx = BuildContext(actionArgs: new() { ["command"] = command });

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.Admin);

        ctx.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ActionArgument_dto_com_EmpresaId_mesma_passa()
    {
        var command = new CommandComEmpresaId { EmpresaId = UserEmpresa, Nome = "X" };
        var ctx = BuildContext(actionArgs: new() { ["command"] = command });

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.Admin);

        ctx.Result.Should().BeNull();
    }

    // ── Objeto sem EmpresaId — deve ser ignorado ─────────────────────────────

    public sealed class DtoSemEmpresa
    {
        public string? Nome { get; set; }
    }

    [Fact]
    public async Task ActionArgument_dto_sem_EmpresaId_e_ignorado()
    {
        var ctx = BuildContext(actionArgs: new() { ["command"] = new DtoSemEmpresa { Nome = "X" } });

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.Admin);

        ctx.Result.Should().BeNull();
    }

    // ── SuperAdmin bypass ────────────────────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_com_empresa_diferente_passa()
    {
        var command = new CommandComEmpresaId { EmpresaId = OtherEmpresa };
        var ctx = BuildContext(actionArgs: new() { ["command"] = command });

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.SuperAdmin);

        ctx.Result.Should().BeNull();
    }

    // ── EmpresaId vazio é ignorado ───────────────────────────────────────────

    [Fact]
    public async Task EmpresaId_vazio_em_dto_e_ignorado()
    {
        var command = new CommandComEmpresaId { EmpresaId = Guid.Empty };
        var ctx = BuildContext(actionArgs: new() { ["command"] = command });

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.Admin);

        ctx.Result.Should().BeNull();
    }

    // ── Guid? EmpresaId (nullable) ───────────────────────────────────────────

    public sealed class CommandComEmpresaIdNullable
    {
        public Guid? EmpresaId { get; set; }
    }

    [Fact]
    public async Task ActionArgument_dto_com_EmpresaId_nullable_diferente_retorna_Forbid()
    {
        var command = new CommandComEmpresaIdNullable { EmpresaId = OtherEmpresa };
        var ctx = BuildContext(actionArgs: new() { ["command"] = command });

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.Admin);

        ctx.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ActionArgument_dto_com_EmpresaId_nullable_null_e_ignorado()
    {
        var command = new CommandComEmpresaIdNullable { EmpresaId = null };
        var ctx = BuildContext(actionArgs: new() { ["command"] = command });

        await ExecuteFilter(ctx, UserEmpresa, NivelAcesso.Admin);

        ctx.Result.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task ExecuteFilter(
        ActionExecutingContext ctx,
        Guid userEmpresa,
        NivelAcesso nivel)
    {
        var accessor = Substitute.For<ICurrentUserAccessor>();
        accessor.EmpresaId.Returns(userEmpresa);
        accessor.Nivel.Returns(nivel);

        // Acesso ao tipo internal ValidateEmpresaIdFilter via reflection — mantemos encapsulado.
        var filterType = typeof(ValidateEmpresaIdAttribute).Assembly
            .GetType("EasyStock.Api.Http.ValidateEmpresaIdFilter")!;

        // ILogger<ValidateEmpresaIdFilter> via NullLogger<>.Instance (typed correctly).
        var nullLoggerType = typeof(NullLogger<>).MakeGenericType(filterType);
        var logger = nullLoggerType.GetField("Instance")!.GetValue(null);

        var filter = (IAsyncActionFilter)Activator.CreateInstance(filterType, accessor, logger)!;

        await filter.OnActionExecutionAsync(ctx,
            () => Task.FromResult(Substitute.For<ActionExecutedContext>(
                ctx, new List<IFilterMetadata>(), ctx.Controller)));
    }

    private static ActionExecutingContext BuildContext(
        string? query = null,
        Dictionary<string, object?>? actionArgs = null)
    {
        var httpContext = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(query))
            httpContext.Request.QueryString = new QueryString("?" + query);

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            actionArgs ?? new Dictionary<string, object?>(),
            controller: new object());
    }
}
