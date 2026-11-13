using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CadastroFicha_SnapshotAnteriorARequisicao_RecusaTrocaEAbaSemAlterarPessoas(bool restaurarOriginal)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var ficha = await SemearAsync(factory);
        string antes;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var original = await db.Clientes.SingleAsync(c => c.Id == ficha.ClienteId);
            original.TelefoneHash = ClienteOtp.CalcularTelefoneHash(original.Telefone!);
            var outra = Cliente.Criar(EmpresaId, "Bia, outra pessoa conferida");
            outra.Telefone = "+5511922222222";
            outra.TelefoneHash = ClienteOtp.CalcularTelefoneHash(outra.Telefone);
            outra.Enderecos.Add(new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = outra.Id,
                Logradouro = "Rua da Bia", Numero = "20", Cep = "01310200", Bairro = "Centro",
                Cidade = "São Paulo", Estado = "SP", Padrao = true,
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            db.Clientes.Add(outra);
            var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == ficha.ConversaId);
            conversa.CorrigirVinculoCliente(null, conversa.VinculoRevisao);
            conversa.CorrigirVinculoCliente(outra.Id, conversa.VinculoRevisao);
            if (restaurarOriginal)
            {
                conversa.CorrigirVinculoCliente(null, conversa.VinculoRevisao);
                conversa.CorrigirVinculoCliente(original.Id, conversa.VinculoRevisao);
            }
            await db.SaveChangesAsync();
            antes = await LerCadastrosDaFichaAsync(db);
        }

        using var resposta = await client.PostAsJsonAsync($"/api/atendimento/conversas/{ficha.ConversaId}/cliente", new
        {
            ficha.ClienteId, ficha.VinculoRevisao, Nome = "Nome da ficha antiga", Telefone = "11933333333",
            Endereco = new { Cep = "01310100", Logradouro = "Rua da ficha antiga", Numero = "99",
                Bairro = "Bela Vista", Cidade = "São Paulo", Uf = "SP" },
        });

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict, await resposta.Content.ReadAsStringAsync());
        using var erro = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        erro.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VINCULO_CLIENTE_ALTERADO");
        using var verificar = factory.Services.CreateScope();
        var estado = verificar.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await LerCadastrosDaFichaAsync(estado)).Should().Be(antes,
            "a ficha antiga não pode alterar nome, telefone, hash, endereço ou outro dado de nenhuma das duas pessoas");
    }

    [SkippableTheory]
    [InlineData("clienteId")]
    [InlineData("vinculoRevisao")]
    public async Task CadastroFicha_SnapshotOmitido_Recusa400SemEscrever(string campoOmitido)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var ficha = await SemearAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var antes = await LerCadastrosDaFichaAsync(db);
        var body = new Dictionary<string, object?> { ["clienteId"] = ficha.ClienteId,
            ["vinculoRevisao"] = ficha.VinculoRevisao, ["nome"] = "Não deve ser gravado" };
        body.Remove(campoOmitido);

        using var resposta = await client.PostAsJsonAsync($"/api/atendimento/conversas/{ficha.ConversaId}/cliente", body);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resposta.Content.ReadAsStringAsync());
        (await LerCadastrosDaFichaAsync(db)).Should().Be(antes);
    }

    [SkippableFact]
    public async Task CadastroFicha_ClienteNuloExplicito_CadastraEDevolveRevisaoConfirmada()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        await SemearAsync(factory);
        var conversa = Conversa.Abrir(EmpresaId, "visitante-ficha-sem-cliente", DateTime.UtcNow, canal: CanalConversa.ChatSite);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.AtendimentoConversas.Add(conversa);
            await db.SaveChangesAsync();
        }

        using var resposta = await client.PostAsJsonAsync($"/api/atendimento/conversas/{conversa.Id}/cliente", new
        {
            conversa.ClienteId, conversa.VinculoRevisao, Nome = "Nova pessoa conferida", Telefone = "11912345999",
        });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        using var verificar = factory.Services.CreateScope();
        var estado = verificar.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var confirmada = await estado.AtendimentoConversas.SingleAsync(c => c.Id == conversa.Id);
        confirmada.ClienteId.Should().Be(data.GetProperty("clienteId").GetGuid());
        confirmada.VinculoRevisao.Should().BeGreaterThan(conversa.VinculoRevisao);
        data.GetProperty("vinculoRevisao").GetInt64().Should().Be(confirmada.VinculoRevisao);
    }

    private static async Task<string> LerCadastrosDaFichaAsync(EasyStockDbContext db)
    {
        var clientes = await db.Clientes.AsNoTracking().Include(c => c.Enderecos)
            .Where(c => c.EmpresaId == EmpresaId).OrderBy(c => c.Id).ToListAsync();
        return JsonSerializer.Serialize(clientes.Select(c => new
        {
            c.Id, c.Nome, c.Telefone, c.TelefoneHash, c.Email, c.Endereco, c.Cep, c.AlteradoEm,
            Enderecos = c.Enderecos.OrderBy(e => e.Id).Select(e => new
            {
                e.Id, e.Logradouro, e.Numero, e.Complemento, e.Bairro, e.Cidade, e.Estado, e.Cep, e.Padrao, e.AlteradoEm,
            }),
        }));
    }
}
