using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using EasyStock.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Authorization;

public class InternalCronJobAuthHandlerTests
{
    private const string Esperado = "token-do-cron-123";

    private static async Task<AuthenticateResult> Autenticar(string? enviado, string? configurado = Esperado)
    {
        var cron = Substitute.For<IOptionsMonitor<InternalCronJobOptions>>();
        cron.CurrentValue.Returns(new InternalCronJobOptions { Habilitado = true, Token = configurado });
        var schemeOpts = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOpts.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());

        var handler = new InternalCronJobAuthHandler(
            schemeOpts, NullLoggerFactory.Instance, UrlEncoder.Default, cron);
        var ctx = new DefaultHttpContext();
        if (enviado is not null) ctx.Request.Headers["X-Internal-Cron-Token"] = enviado;
        await handler.InitializeAsync(
            new AuthenticationScheme(InternalCronJobAuthHandler.SchemeName, null, typeof(InternalCronJobAuthHandler)), ctx);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task Token_certo_autentica()
        => (await Autenticar(Esperado)).Succeeded.Should().BeTrue();

    [Fact]
    public async Task Token_errado_do_mesmo_tamanho_falha()
        => (await Autenticar("token-do-cron-124")).Succeeded.Should().BeFalse();

    [Theory]
    [InlineData("curto")]
    [InlineData("token-do-cron-123-com-sufixo-maior")]
    public async Task Token_de_outro_tamanho_falha(string enviado)
        => (await Autenticar(enviado)).Succeeded.Should().BeFalse();

    [Fact]
    public async Task Token_configurado_vazio_falha()
        => (await Autenticar(Esperado, configurado: "")).Succeeded.Should().BeFalse();

    [Fact]
    public void Comparacao_usa_FixedTimeEquals_sobre_hashes_de_tamanho_fixo()
    {
        var metodo = typeof(InternalCronJobAuthHandler)
            .GetMethod("CryptographicEquals", BindingFlags.NonPublic | BindingFlags.Static)!;
        var il = metodo.GetMethodBody()!.GetILAsByteArray()!;
        var chamados = new List<MethodBase>();
        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] is 0x28 or 0x6F) // call / callvirt
            {
                try { chamados.Add(metodo.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1))!); }
                catch (Exception) { /* byte que não é opcode */ }
            }
        }

        chamados.Should().Contain(m => m.DeclaringType == typeof(CryptographicOperations) && m.Name == "FixedTimeEquals");
        chamados.Should().Contain(m => m.DeclaringType == typeof(SHA256) && m.Name == "HashData");
    }
}
