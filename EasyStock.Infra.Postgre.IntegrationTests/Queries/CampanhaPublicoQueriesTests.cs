using EasyStock.Application.UseCases.Campanhas;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Queries;
using EasyStock.Infra.Postgre.Repositories.Campanhas;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Queries;

/// <summary>
/// S29 em Postgres real: os fatos de cada cliente que o cálculo do público usa (tags, consentimento
/// por canal, última compra do item entregue, última campanha recebida) e a lista de destinatários,
/// sempre só da empresa.
/// </summary>
public class CampanhaPublicoQueriesTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static Campanha NovaCampanha(Guid empresaId, string nome) =>
        Campanha.Criar(empresaId, Guid.NewGuid(),
            new DadosCampanha(nome, "Oi {{nome}}", null, null, FiltroCampanha.ParaTodos, [], null, false, null), Agora);

    [SkippableFact]
    public async Task CandidatosTrazemOsFatosDoPublicoSoDaEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var empresa = Empresa.Criar("Casa da Baba Publico", "55555555000191");
        var outraEmpresa = Empresa.Criar("Outra Publico", "66666666000191");
        var item = Guid.NewGuid();

        var ana = Cliente.Criar(empresa.Id, "Ana");
        ana.Telefone = "5511999990001";
        ana.DefinirConsentimentoMarketing(true, Agora);
        ana.AdicionarTag("vegano", OrigemClienteTag.Dona, Agora);
        var bia = Cliente.Criar(empresa.Id, "Bia");
        bia.DefinirConsentimentoMarketing(true, Agora); // o booleano diz sim, mas a linha do canal revogou
        bia.Bloquear("calote", Agora);
        var cris = Cliente.Criar(empresa.Id, "Cris"); // sem booleano, com consentimento no WhatsApp
        var inativa = Cliente.Criar(empresa.Id, "Inativa");
        inativa.Ativo = false;
        var deFora = Cliente.Criar(outraEmpresa.Id, "De fora");

        var entregue = Pedido.Criar(empresa.Id, ana);
        entregue.Status = StatusPedidoMapper.Entregue;
        entregue.EntreguEm = Agora.AddDays(-3);
        entregue.Itens.Add(NovoItem(entregue, cardapioItemId: item));
        var antigo = Pedido.Criar(empresa.Id, ana);
        antigo.Status = StatusPedidoMapper.Entregue;
        antigo.EntreguEm = Agora.AddDays(-20);
        antigo.Itens.Add(NovoItem(antigo, cardapioItemId: item)); // ProdutoId tem FK para produtos: fica no OR da query
        var naoEntregue = Pedido.Criar(empresa.Id, cris);
        naoEntregue.Itens.Add(NovoItem(naoEntregue, cardapioItemId: item));

        var campanha = NovaCampanha(empresa.Id, "Esta");
        var anterior = NovaCampanha(empresa.Id, "Anterior");
        var recebeuAnterior = CampanhaDestinatario.Criar(anterior, ana.Id);
        recebeuAnterior.Enfileirar(1, Guid.NewGuid());
        recebeuAnterior.MarcarEnviado(Agora.AddDays(-5));
        var recebeuEsta = CampanhaDestinatario.Criar(campanha, cris.Id);
        recebeuEsta.Enfileirar(1, Guid.NewGuid());
        recebeuEsta.MarcarEnviado(Agora.AddDays(-1));

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            db.Empresas.AddRange(empresa, outraEmpresa);
            db.Clientes.AddRange(ana, bia, cris, inativa, deFora);
            db.Pedidos.AddRange(entregue, antigo, naoEntregue);
            db.ConsentimentosContato.AddRange(
                ConsentimentoContato.Registrar(empresa.Id, bia.Id, CanalConversa.WhatsApp, FinalidadeContato.Marketing,
                    SituacaoConsentimento.Revogado, "palavra_sair", Agora),
                ConsentimentoContato.Registrar(empresa.Id, cris.Id, CanalConversa.WhatsApp, FinalidadeContato.Marketing,
                    SituacaoConsentimento.Concedido, "console", Agora),
                ConsentimentoContato.Registrar(empresa.Id, ana.Id, CanalConversa.Email, FinalidadeContato.Marketing,
                    SituacaoConsentimento.Revogado, "console", Agora));
            db.Campanhas.AddRange(campanha, anterior);
            db.CampanhaDestinatarios.AddRange(recebeuAnterior, recebeuEsta);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var queries = new CampanhaPublicoQueries(db);

            var candidatos = await queries.ListarCandidatosAsync(empresa.Id, campanha.Id, item);

            candidatos.Select(c => c.Nome).Should().Equal("Ana", "Bia", "Cris");
            candidatos[0].Should().BeEquivalentTo(new
            {
                TemTelefone = true,
                Bloqueado = false,
                ConsentiuMarketing = true, // revogação no e-mail não vale para o WhatsApp
                Tags = new[] { "vegano" },
                UltimaCompraItemEm = Agora.AddDays(-3),
                UltimaCampanhaRecebidaEm = Agora.AddDays(-5),
            });
            candidatos[1].Should().BeEquivalentTo(new { TemTelefone = false, Bloqueado = true, ConsentiuMarketing = false });
            candidatos[2].ConsentiuMarketing.Should().BeTrue();
            candidatos[2].UltimaCompraItemEm.Should().BeNull("pedido não entregue não conta");
            candidatos[2].UltimaCampanhaRecebidaEm.Should().BeNull("a própria campanha não conta no limite semanal");

            (await queries.ListarCandidatosAsync(empresa.Id, campanha.Id, null))[0].UltimaCompraItemEm
                .Should().BeNull("sem item no filtro não há compra a procurar");
            (await queries.ListarCandidatosAsync(outraEmpresa.Id, campanha.Id, item)).Should().BeEmpty("EmpresaId no WHERE e RLS");

            var destinatarios = await queries.ListarDestinatariosAsync(empresa.Id, campanha.Id, null, 100);
            destinatarios.Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                ClienteId = cris.Id,
                Nome = "Cris",
                Status = StatusCampanhaDestinatario.Enviado,
                Onda = 1,
            });
            (await queries.ListarDestinatariosAsync(empresa.Id, campanha.Id, StatusCampanhaDestinatario.Pendente, 100))
                .Should().BeEmpty();
            (await queries.ListarDestinatariosAsync(outraEmpresa.Id, campanha.Id, null, 100)).Should().BeEmpty();
        }
    }

    [SkippableFact]
    public async Task RecalculoNoBancoPreservaEnviadosERefazOsDemais()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Casa da Baba Recalculo", "77777777000191");
        var clientes = new[] { "Ana", "Bia", "Cris", "Duda" }.Select(nome =>
        {
            var cliente = Cliente.Criar(empresa.Id, nome);
            cliente.Telefone = "5511999990000";
            cliente.DefinirConsentimentoMarketing(true, agora);
            return cliente;
        }).ToArray();
        var (ana, bia, cris, duda) = (clientes[0], clientes[1], clientes[2], clientes[3]);
        var campanha = NovaCampanha(empresa.Id, "Recalculo");

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            db.Empresas.Add(empresa);
            db.Clientes.AddRange(ana, bia, cris);
            db.Campanhas.Add(campanha);
            await db.SaveChangesAsync();
        }

        async Task<PublicoCampanhaResult> RecalcularAsync()
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresa.Id);
            var useCase = new CalcularPublicoCampanhaUseCase(
                new CampanhaRepository(db), new CampanhaPublicoQueries(db), db, TimeProvider.System);
            return await useCase.ExecuteAsync(empresa.Id, campanha.Id);
        }

        (await RecalcularAsync()).Pendentes.Should().Be(3);

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var enviado = await db.CampanhaDestinatarios.SingleAsync(d => d.CampanhaId == campanha.Id && d.ClienteId == ana.Id);
            enviado.Enfileirar(1, Guid.NewGuid());
            enviado.MarcarEnviado(agora);
            (await db.Clientes.SingleAsync(c => c.Id == bia.Id)).Bloquear(null, agora);
            (await db.Clientes.SingleAsync(c => c.Id == cris.Id)).Ativo = false;
            db.Clientes.Add(duda);
            await db.SaveChangesAsync();
        }

        var resumo = await RecalcularAsync();

        resumo.Should().BeEquivalentTo(new
        {
            Total = 3,
            Pendentes = 1,
            Preservados = 1,
            ExcluidosPorMotivo = new Dictionary<string, int> { [MotivoExclusaoCampanha.Bloqueado] = 1 },
            Amostra = new[] { "Duda" },
        });
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var destinatarios = await new CampanhaPublicoQueries(db).ListarDestinatariosAsync(empresa.Id, campanha.Id, null, 100);
            destinatarios.Select(d => (d.Nome, d.Status, d.MotivoExclusao)).Should().Equal(
                ("Ana", StatusCampanhaDestinatario.Enviado, null),
                ("Bia", StatusCampanhaDestinatario.Excluido, MotivoExclusaoCampanha.Bloqueado),
                ("Duda", StatusCampanhaDestinatario.Pendente, null));
        }
    }

    private static PedidoItem NovoItem(Pedido pedido, Guid cardapioItemId) => new()
    {
        Id = Guid.NewGuid(),
        PedidoId = pedido.Id,
        Nome = "Bolo de fubá",
        Quantidade = 1m,
        PrecoUnitario = 10m,
        Subtotal = 10m,
        CriadoEm = Agora,
        CardapioItemId = cardapioItemId,
    };
}
