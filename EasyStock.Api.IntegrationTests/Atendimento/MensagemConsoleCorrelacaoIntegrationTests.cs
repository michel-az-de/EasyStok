using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task MensagemConsole_ChaveNoPollingAntesDoAck_ReplayETextosIguaisIsoladosPorEmpresa()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new EnvioWhatsAppResult("wamid.correlacao." + Guid.NewGuid().ToString("N")));
        await using var factory = CriarFactory(Provedor(new())).WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton(cloud)));
        factory.UseKestrel(0);
        using var leitura = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        var chave = new string('a', 120);
        Guid conversaAlheiaId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            using var bypass = db.UseRowLevelSecurityBypass();
            var agora = DateTime.UtcNow;
            var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId);
            conversa.RegistrarEntrada(agora);
            conversa.Assumir(agora, UsuarioId);
            db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversa.Id, agora,
                TipoConteudoMensagem.Texto, "Entrada sintética", "wamid.correlacao.entrada"));
            var empresaAlheia = Guid.NewGuid();
            db.Empresas.Add(new Empresa { Id = empresaAlheia, Nome = "Empresa isolada", Documento = empresaAlheia.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
            var alheia = Conversa.Abrir(empresaAlheia, "5511912345666", agora);
            conversaAlheiaId = alheia.Id;
            db.AtendimentoConversas.Add(alheia);
            var mensagemAlheia = Mensagem.Saida(empresaAlheia, alheia.Id, AutorMensagem.Dona, agora,
                TipoConteudoMensagem.Texto, "Somente outra empresa", "wamid.correlacao.alheia");
            mensagemAlheia.RegistrarChaveEnvio(chave);
            db.AtendimentoMensagens.Add(mensagemAlheia);
            await db.SaveChangesAsync();
        }
        var rota = $"/api/atendimento/conversas/{cenario.ConversaId}/mensagens";
        using var ack = new ReterPrimeiroAckMensagem { InnerHandler = new HttpClientHandler() };
        using var envio = new HttpClient(ack) { BaseAddress = leitura.BaseAddress };
        envio.DefaultRequestHeaders.Add("Idempotency-Key", chave);
        var pendente = envio.PostAsJsonAsync(rota, new { texto = "Mesmo texto, intenções distintas" });
        Guid mensagemId;
        try
        {
            (await ack.Recebida.Task.WaitAsync(TimeSpan.FromSeconds(30))).Should().Be(HttpStatusCode.OK);
            pendente.IsCompleted.Should().BeFalse("o transporte ainda retém a confirmação do POST");
            using var polling = await leitura.GetAsync(rota);
            polling.EnsureSuccessStatusCode();
            var mensagens = await CorrelacaoDadosAsync(polling);
            var correlacionada = mensagens.EnumerateArray().Where(m =>
                m.TryGetProperty("chaveEnvio", out var k) && k.GetString() == chave).Should().ContainSingle().Subject;
            mensagemId = correlacionada.GetProperty("id").GetGuid();
            correlacionada.GetProperty("texto").GetString().Should().Be("Mesmo texto, intenções distintas");
            using var alheia = await leitura.GetAsync($"/api/atendimento/conversas/{conversaAlheiaId}/mensagens");
            alheia.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { ack.Liberar.TrySetResult(); }

        using var resposta = await pendente;
        resposta.EnsureSuccessStatusCode();
        var confirmada = await CorrelacaoDadosAsync(resposta);
        confirmada.GetProperty("id").GetGuid().Should().Be(mensagemId);
        confirmada.GetProperty("chaveEnvio").GetString().Should().Be(chave);
        using var replay = await envio.PostAsJsonAsync(rota, new { texto = "Mesmo texto, intenções distintas" });
        replay.EnsureSuccessStatusCode();
        replay.Headers.GetValues("X-Idempotent-Replay").Should().Contain("true");
        (await CorrelacaoDadosAsync(replay)).GetProperty("id").GetGuid().Should().Be(mensagemId);

        var outraChave = Guid.NewGuid().ToString();
        envio.DefaultRequestHeaders.Remove("Idempotency-Key");
        envio.DefaultRequestHeaders.Add("Idempotency-Key", outraChave);
        using var segunda = await envio.PostAsJsonAsync(rota, new { texto = "Mesmo texto, intenções distintas" });
        segunda.EnsureSuccessStatusCode();
        var nova = await CorrelacaoDadosAsync(segunda);
        nova.GetProperty("id").GetGuid().Should().NotBe(mensagemId);
        nova.GetProperty("chaveEnvio").GetString().Should().Be(outraChave);
        using var final = await leitura.GetAsync(rota);
        var confirmadas = (await CorrelacaoDadosAsync(final)).EnumerateArray()
            .Where(m => m.GetProperty("texto").GetString() == "Mesmo texto, intenções distintas").ToArray();
        confirmadas.Should().HaveCount(2);
        confirmadas.Select(m => m.GetProperty("chaveEnvio").GetString()).Should().BeEquivalentTo(new[] { chave, outraChave });
        await cloud.Received(2).EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static async Task<JsonElement> CorrelacaoDadosAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").Clone();
    }

    private sealed class ReterPrimeiroAckMensagem : DelegatingHandler
    {
        private int _retida;
        public TaskCompletionSource<HttpStatusCode> Recebida { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var resposta = await base.SendAsync(request, ct);
            if (Interlocked.Exchange(ref _retida, 1) == 0)
            {
                await resposta.Content.LoadIntoBufferAsync(ct);
                Recebida.TrySetResult(resposta.StatusCode);
                await Liberar.Task.WaitAsync(ct);
            }
            return resposta;
        }
    }
}
