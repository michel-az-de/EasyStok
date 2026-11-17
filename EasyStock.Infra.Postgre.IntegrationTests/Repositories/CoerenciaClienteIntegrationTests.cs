using EasyStock.Application.UseCases.AdicionarClienteTelefone;
using EasyStock.Application.UseCases.RemoverClienteTelefone;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2780, #2777 e #2778: a busca rápida acha por e-mail e mostra o inativo só a quem pede; a lista de Cadastros
/// conta pedidos pela mesma regra de compra do dossiê (sem cancelado, rascunho e aguardando pagamento); a lista de telefones tem um principal só.
/// </summary>
public sealed class CoerenciaClienteIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task BuscaRapidaAchaPorEmailEMostraInativoSoQuandoPedido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Empresa.Criar("Coerência 2780", "55555555000191");
        var outra = Empresa.Criar("Coerência 2780 alheia", "66666666000191");
        var ativa = Cliente.Criar(empresa.Id, "Marina Coerência");
        ativa.AtualizarCadastro(ativa.Nome, null, null, null, "Marina.Duarte@Exemplo.test", null, null);
        var inativa = Cliente.Criar(empresa.Id, "Marina Coerência Antiga");
        inativa.AtualizarCadastro(inativa.Nome, null, null, null, "antiga@exemplo.test", null, null);
        inativa.Desativar();
        var alheia = Cliente.Criar(outra.Id, "Marina Coerência");
        alheia.AtualizarCadastro(alheia.Nome, null, null, null, "marina.duarte@exemplo.test", null, null);
        await Gravar(empresa, outra, [ativa, inativa, alheia], []);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var clientes = new ClienteRepository(db);

        (await clientes.SearchAsync(empresa.Id, "marina.duarte@exemplo")).Should().ContainSingle().Which.Id.Should().Be(ativa.Id);
        (await clientes.SearchAsync(empresa.Id, "antiga@exemplo.test")).Should().BeEmpty("sem pedir, o inativo segue fora (pedido de balcão e Chamar clientes)");
        (await clientes.SearchAsync(empresa.Id, "antiga@exemplo.test", incluirInativos: true)).Should().ContainSingle().Which.Ativo.Should().BeFalse();

        var ambas = (await clientes.SearchAsync(empresa.Id, "marina coerencia", incluirInativos: true)).ToList();
        ambas.Select(c => c.Id).Should().Equal([ativa.Id, inativa.Id], "o ativo vem antes do inativo");
    }

    [SkippableFact]
    public async Task ListaContaSoCompraEClienteSoComCanceladoEhLead()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Empresa.Criar("Coerência 2777", "77777777000191");
        var outra = Empresa.Criar("Coerência 2777 alheia", "88888888000191");
        var compradora = Cliente.Criar(empresa.Id, "Contagem 2777 Compradora");
        var soCancelou = Cliente.Criar(empresa.Id, "Contagem 2777 Cancelou");
        soCancelou.Telefone = "+5511987652777";
        var cancelado = Pedido.Criar(empresa.Id, compradora);
        cancelado.Cancelar();
        var concluido = Pedido.Criar(empresa.Id, compradora);
        var unicoCancelado = Pedido.Criar(empresa.Id, soCancelou);
        unicoCancelado.Cancelar();
        await Gravar(empresa, outra, [compradora, soCancelou], [cancelado, concluido, unicoCancelado]);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var clientes = new ClienteRepository(db);

        var lista = (await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: "contagem 2777")).items.ToDictionary(c => c.Id);
        lista[compradora.Id].OrderCount.Should().Be(1, "o cancelado não é compra, como no dossiê");
        lista[soCancelou.Id].OrderCount.Should().Be(0);
        lista[soCancelou.Id].LastOrderAt.Should().BeNull();

        (await clientes.ObterMetricasPedidosAsync(empresa.Id, compradora.Id)).OrderCount.Should().Be(1);

        var leads = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: "contagem 2777", relacionamento: "lead");
        leads.items.Should().ContainSingle().Which.Id.Should().Be(soCancelou.Id);
        var comCompra = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: "contagem 2777", relacionamento: "cliente");
        comCompra.items.Should().ContainSingle().Which.Id.Should().Be(compradora.Id);

        var porContagem = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: "contagem 2777", sort: "ordercount", order: "desc");
        porContagem.items.First().Id.Should().Be(compradora.Id);

        var candidato = (await new VinculoClienteConversaRepository(db).ListarCandidatosAsync(empresa.Id, ["+5511987652777"], [], default)).Should().ContainSingle().Subject;
        candidato.EhLead.Should().BeTrue("a correção de vínculo usa a mesma regra de compra");
        candidato.QuantidadePedidos.Should().Be(0);
        candidato.UltimoPedidoEm.Should().BeNull();
    }

    [SkippableFact]
    public async Task ListaDeTelefonesFicaComUmPrincipalAoAdicionarERemover()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Empresa.Criar("Coerência 2778", "99999999000191");
        var outra = Empresa.Criar("Coerência 2778 alheia", "12121212000191");
        var paula = Cliente.Criar(empresa.Id, "Paula Dois Telefones");
        await Gravar(empresa, outra, [paula], []);

        async Task<Guid> Adicionar(string numero, bool principal)
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresa.Id);
            var uso = new AdicionarClienteTelefoneUseCase(new ClienteRepository(db), db, NullLogger<AdicionarClienteTelefoneUseCase>.Instance);
            return (await uso.ExecuteAsync(new AdicionarClienteTelefoneCommand(empresa.Id, paula.Id, numero, Principal: principal)))!.Value;
        }
        async Task<List<(Guid Id, bool Principal)>> Lidos()
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresa.Id);
            var c = await new ClienteRepository(db).GetByIdWithDetailsAsync(empresa.Id, paula.Id);
            return c!.Telefones.Select(t => (t.Id, t.Principal)).ToList();
        }

        var primeiro = await Adicionar("+5511987650001", principal: true);
        var segundo = await Adicionar("+5511987650002", principal: true);
        (await Lidos()).Where(t => t.Principal).Select(t => t.Id).Should().Equal([segundo], "adicionar como principal rebaixa o anterior");

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var uso = new RemoverClienteTelefoneUseCase(new ClienteRepository(db), db, NullLogger<RemoverClienteTelefoneUseCase>.Instance);
            (await uso.ExecuteAsync(new RemoverClienteTelefoneCommand(empresa.Id, paula.Id, segundo))).Should().BeTrue();
        }
        (await Lidos()).Should().ContainSingle().Which.Should().Be((primeiro, true), "remover o principal promove o que sobrou");
    }

    private async Task Gravar(Empresa empresa, Empresa outra, Cliente[] clientes, Pedido[] pedidos)
    {
        await using var gravar = fixture.CreateDbContext();
        using var bypass = gravar.UseRowLevelSecurityBypass();
        await gravar.Database.MigrateAsync();
        gravar.Empresas.AddRange(empresa, outra);
        gravar.Clientes.AddRange(clientes);
        gravar.Pedidos.AddRange(pedidos);
        await gravar.SaveChangesAsync();
    }
}
