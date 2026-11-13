using System.Net;
using System.Net.Http.Json;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.AtualizarCliente;
using EasyStock.Application.UseCases.Cliente;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableTheory]
    [InlineData("ficha")]
    [InlineData("adicionar-endereco")]
    [InlineData("remover-endereco")]
    [InlineData("adicionar-telefone")]
    [InlineData("remover-telefone")]
    public async Task CadastroCliente_EscritaNaFichaOuSubcadastroInvalidaCentralAberta(string escritor)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        Guid telefoneId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var telefone = new ClienteTelefone { Id = Guid.NewGuid(), ClienteId = cenario.ClienteId,
                Numero = "+5511988887777", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
            db.Set<ClienteTelefone>().Add(telefone);
            await db.SaveChangesAsync();
            telefoneId = telefone.Id;
        }
        var rota = $"/api/clientes/{cenario.ClienteId}";
        var abertura = (await client.GetFromJsonAsync<ApiResponse<ClienteDetalheResult>>(rota))!.Data;
        using var escrita = escritor switch
        {
            "ficha" => await client.PostAsJsonAsync($"/api/atendimento/conversas/{cenario.ConversaId}/cliente", new
            {
                cenario.ClienteId, cenario.VinculoRevisao, Nome = abertura.Cliente.Nome, Telefone = "11977776666",
                Endereco = new { Cep = "01310100", Logradouro = "Av. Paulista", Numero = "900",
                    Bairro = "Bela Vista", Cidade = "São Paulo", Uf = "SP" }
            }),
            "adicionar-endereco" => await client.PostAsJsonAsync(rota + "/enderecos", new
                { EmpresaId, cenario.ClienteId, Logradouro = "Rua adicional", Numero = "77", Cidade = "São Paulo", Estado = "SP" }),
            "remover-endereco" => await client.DeleteAsync(rota + "/enderecos/" + abertura.Enderecos[0].Id),
            "adicionar-telefone" => await client.PostAsJsonAsync(rota + "/telefones", new
                { EmpresaId, cenario.ClienteId, Numero = "11966665555" }),
            "remover-telefone" => await client.DeleteAsync(rota + "/telefones/" + telefoneId),
            _ => throw new InvalidOperationException(escritor)
        };
        escrita.IsSuccessStatusCode.Should().BeTrue(await escrita.Content.ReadAsStringAsync());
        var confirmada = (await client.GetFromJsonAsync<ApiResponse<ClienteDetalheResult>>(rota))!.Data;
        confirmada.Cliente.Revisao.Should().NotBe(abertura.Cliente.Revisao);
        if (escritor == "ficha")
        {
            confirmada.Cliente.Telefone.Should().Be("+5511977776666");
            confirmada.Cliente.Endereco.Should().Be("Av. Paulista, 900");
        }
        using var antiga = await client.PatchAsJsonAsync(rota, ComandoCadastro(abertura.Cliente) with { Nome = "Central antiga" });
        antiga.StatusCode.Should().Be(HttpStatusCode.Conflict, await antiga.Content.ReadAsStringAsync());
        (await antiga.Content.ReadFromJsonAsync<ApiErrorResponse>())!.Error.Code.Should().Be("CLIENTE_ALTERADO");
        var preservada = (await client.GetFromJsonAsync<ApiResponse<ClienteDetalheResult>>(rota))!.Data;
        preservada.Should().BeEquivalentTo(confirmada, "o payload antigo não pode apagar a escrita da ficha ou do subcadastro");
    }

    [SkippableFact]
    public async Task CadastroCliente_DuasAbasRecusamPayloadAntigoSemApagarEmailOuAlterarOutraPessoa()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        Guid outraPessoaId;
        Guid clienteOutroTenantId;
        uint revisaoOutroTenant;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            using var bypass = db.UseRowLevelSecurityBypass();
            var outra = Cliente.Criar(EmpresaId, "Outra pessoa preservada");
            outra.Email = "outra@exemplo.test";
            db.Clientes.Add(outra);
            var empresaAlheia = Guid.NewGuid();
            var agora = DateTime.UtcNow;
            db.Empresas.Add(new Empresa { Id = empresaAlheia, Nome = "Empresa isolada", Documento = empresaAlheia.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
            var alheio = Cliente.Criar(empresaAlheia, "Pessoa de outra empresa");
            db.Clientes.Add(alheio);
            await db.SaveChangesAsync();
            outraPessoaId = outra.Id;
            clienteOutroTenantId = alheio.Id;
            revisaoOutroTenant = alheio.Revisao;
        }
        var rota = $"/api/clientes/{cenario.ClienteId}";
        var abertura = (await client.GetFromJsonAsync<ApiResponse<ClienteDetalheResult>>(rota))!.Data.Cliente;
        abertura.Revisao.Should().NotBe(0);
        var primeira = ComandoCadastro(abertura) with { Email = "primeira@exemplo.test" };
        using var salva = await client.PatchAsJsonAsync(rota, primeira);
        salva.StatusCode.Should().Be(HttpStatusCode.OK, await salva.Content.ReadAsStringAsync());
        var confirmada = (await salva.Content.ReadFromJsonAsync<ApiResponse<ClienteResult>>())!.Data;
        confirmada.Revisao.Should().NotBe(abertura.Revisao);

        using var antiga = await client.PatchAsJsonAsync(rota, ComandoCadastro(abertura) with { Nome = "Nome da segunda aba" });
        antiga.StatusCode.Should().Be(HttpStatusCode.Conflict, await antiga.Content.ReadAsStringAsync());
        (await antiga.Content.ReadFromJsonAsync<ApiErrorResponse>())!.Error.Code.Should().Be("CLIENTE_ALTERADO");
        using var omitida = await client.PatchAsJsonAsync(rota, new { EmpresaId, Id = cenario.ClienteId, Nome = "Sem revisão" });
        omitida.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var pessoaDivergente = await client.PatchAsJsonAsync(rota, primeira with { Id = outraPessoaId });
        pessoaDivergente.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var outroTenant = await client.PatchAsJsonAsync($"/api/clientes/{clienteOutroTenantId}",
            primeira with { Id = clienteOutroTenantId, Revisao = revisaoOutroTenant });
        outroTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var atual = (await client.GetFromJsonAsync<ApiResponse<ClienteDetalheResult>>(rota))!.Data.Cliente;
        atual.Nome.Should().Be(abertura.Nome);
        atual.Email.Should().Be("primeira@exemplo.test");
        atual.Telefone.Should().Be(abertura.Telefone);
        atual.Endereco.Should().Be(abertura.Endereco);
        atual.Revisao.Should().Be(confirmada.Revisao);
        var outraPessoa = (await client.GetFromJsonAsync<ApiResponse<ClienteDetalheResult>>($"/api/clientes/{outraPessoaId}"))!.Data.Cliente;
        outraPessoa.Nome.Should().Be("Outra pessoa preservada");
        outraPessoa.Email.Should().Be("outra@exemplo.test");
    }

    [SkippableFact]
    public async Task CadastroCliente_XminRecusaContextoCarregadoAntesDaOutraGravacaoEAuditoriaReverteJunto()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        using var primeira = factory.Services.CreateScope();
        using var segunda = factory.Services.CreateScope();
        var dbA = primeira.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var dbB = segunda.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        dbA.SetMobileTenantContext(EmpresaId);
        dbB.SetMobileTenantContext(EmpresaId);
        var pessoaA = await dbA.Clientes.SingleAsync(c => c.Id == cenario.ClienteId);
        var pessoaB = await dbB.Clientes.SingleAsync(c => c.Id == cenario.ClienteId);
        var revisaoAberta = pessoaB.Revisao;
        await primeira.ServiceProvider.GetRequiredService<AtualizarClienteUseCase>().ExecuteAsync(
            new(EmpresaId, pessoaA.Id, pessoaA.Nome, Email: "gravada@exemplo.test", Revisao: pessoaA.Revisao));

        var salvarAntiga = () => segunda.ServiceProvider.GetRequiredService<AtualizarClienteUseCase>().ExecuteAsync(
            new(EmpresaId, pessoaB.Id, "Nome concorrente", Email: pessoaB.Email, Revisao: revisaoAberta));
        await salvarAntiga.Should().ThrowAsync<DbUpdateConcurrencyException>();

        using var verificar = factory.Services.CreateScope();
        var db = verificar.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var atual = await db.Clientes.AsNoTracking().SingleAsync(c => c.Id == cenario.ClienteId);
        atual.Nome.Should().Be(pessoaA.Nome);
        atual.Email.Should().Be("gravada@exemplo.test");
        (await db.Set<ClienteAlteracao>().AnyAsync(a => a.ClienteId == atual.Id && a.ValorNovo == "Nome concorrente")).Should().BeFalse();
    }

    private static AtualizarClienteCommand ComandoCadastro(ClienteResult cliente) => new(
        cliente.EmpresaId, cliente.Id, cliente.Nome, cliente.Apt, cliente.Endereco, cliente.Telefone,
        cliente.Email, cliente.Documento, cliente.Observacoes, PessoaJuridica: cliente.TipoPessoa == "juridica",
        NomeFantasia: cliente.NomeFantasia, InscricaoEstadual: cliente.InscricaoEstadual, Revisao: cliente.Revisao);
}
