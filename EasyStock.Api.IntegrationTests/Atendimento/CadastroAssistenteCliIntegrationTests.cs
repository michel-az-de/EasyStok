using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Integrations.Ia;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task Cadastro_HttpResolveAdaptadorCliComExecutorControladoSemEfeitos()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL indisponível.");
        var chamadas = 0;
        const string texto = "Meu nome é Ana Pereira e meu email é ana@fixture.test.";
        await using var factory = CriarFactory().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.PostConfigure<MontagemFichaCliOptions>(o => { o.Enabled = true; o.ExecutablePath = Environment.ProcessPath; });
            // Mantém a resolução real de IMontagemFichaLlmClient; substitui somente a execução do processo.
            s.AddSingleton<ClaudeCliMontagemFichaClient>(sp => new(
                sp.GetRequiredService<IOptions<MontagemFichaCliOptions>>(),
                sp.GetRequiredService<ILogger<ClaudeCliMontagemFichaClient>>(), (inicio, entrada, _, _) =>
                {
                    Interlocked.Increment(ref chamadas);
                    var argumentos = inicio.ArgumentList.ToArray();
                    argumentos[Array.IndexOf(argumentos, "--json-schema") + 1].Should().Be(SugerirCadastroConversaUseCase.Ferramenta.SchemaJson);
                    argumentos[Array.IndexOf(argumentos, "--tools") + 1].Should().BeEmpty();
                    using var envelope = JsonDocument.Parse(entrada);
                    using var fontes = JsonDocument.Parse(envelope.RootElement[0].GetProperty("textos")[0].GetString()!);
                    var fonte = fontes.RootElement.EnumerateArray().Single(f => f.GetProperty("texto").GetString() == texto);
                    var resposta = JsonSerializer.Serialize(new
                    {
                        subtype = "success", is_error = false, permission_denials = Array.Empty<object>(),
                        structured_output = new { campos = new[] { new { campo = "email", valor = "ana@fixture.test", mensagemId = fonte.GetProperty("mensagemId").GetGuid(), evidencia = texto } } },
                        usage = new { input_tokens = 10, output_tokens = 5 }
                    });
                    return Task.FromResult(new ResultadoMontagemCli(0, resposta));
                }));
        }));
        using var http = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
        scope.ServiceProvider.GetRequiredService<IMontagemFichaLlmClient>().Should().BeOfType<ClaudeCliMontagemFichaClient>();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var mensagem = Mensagem.Entrada(EmpresaId, cenario.ConversaId, DateTime.UtcNow, TipoConteudoMensagem.Texto, texto, "wamid.cadastro.cli.controlado");
        db.AtendimentoMensagens.Add(mensagem);
        await db.SaveChangesAsync();
        var antes = await Retrato();

        using var resposta = await http.PostAsJsonAsync("api/atendimento/assistente/cadastro", new { conversaId = cenario.ConversaId });
        resposta.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var campos = json.RootElement.GetProperty("data").GetProperty("campos");
        campos.GetArrayLength().Should().Be(1);
        campos[0].GetProperty("valor").GetString().Should().Be("ana@fixture.test");
        campos[0].GetProperty("mensagemId").GetGuid().Should().Be(mensagem.Id);
        chamadas.Should().Be(1);
        (await Retrato()).Should().Be(antes, "sugerir não escreve cliente, vínculo, mensagem ou pedido");

        async Task<string> Retrato() => JsonSerializer.Serialize(new
        {
            clientes = await db.Clientes.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Nome, c.Telefone, c.Email }).ToListAsync(),
            conversa = await db.AtendimentoConversas.AsNoTracking().Where(c => c.Id == cenario.ConversaId).Select(c => new { c.ClienteId, c.VinculoRevisao }).SingleAsync(),
            mensagens = await db.AtendimentoMensagens.CountAsync(), pedidos = await db.Pedidos.CountAsync()
        });
    }
}
