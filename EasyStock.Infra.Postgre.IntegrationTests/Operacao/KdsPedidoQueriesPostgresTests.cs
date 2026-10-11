using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Operacao;

/// <summary>
/// S19 (#1155): a leitura do KDS traduz para SQL no Postgres real (EXISTS da vaga ativa, COALESCE do
/// agendamento, <c>DateOnly</c> da vaga) e escolhe o dia de produção pela vaga quando ela existe.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class KdsPedidoQueriesPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Endereco_do_pedido_vence_cadastro_alterado_e_legado_conserva_fallback()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresa = Guid.NewGuid();
        var outraEmpresa = Guid.NewGuid();
        Guid pedidoId, legadoId;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            db.Empresas.AddRange(NovaEmpresa(empresa), NovaEmpresa(outraEmpresa));
            var cliente = Cliente.Criar(empresa, "Maria");
            cliente.Endereco = "Rua original, 42";
            cliente.Bairro = "Centro";
            cliente.Cep = "01001000";
            db.Clientes.Add(cliente);
            var pedido = NovoPedido(empresa, StatusPedidoMapper.Aguardando);
            pedido.ClienteId = cliente.Id;
            var legado = NovoPedido(empresa, StatusPedidoMapper.Aguardando);
            legado.ClienteId = cliente.Id;
            var registrar = new EasyStock.Application.Services.Pedidos.EnderecoEntregaPedido(
                NSubstitute.Substitute.For<EasyStock.Application.Ports.Output.Lookup.ICepLookupClient>(),
                NSubstitute.Substitute.For<EasyStock.Application.Ports.Output.Lookup.IGeocodingClient>(), TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EasyStock.Application.Services.Pedidos.EnderecoEntregaPedido>.Instance);
            await registrar.RegistrarAsync(pedido, EasyStock.Application.Services.Pedidos.DadosEnderecoEntrega.De(cliente));
            db.Pedidos.AddRange(pedido, legado);
            await db.SaveChangesAsync();
            pedidoId = pedido.Id;
            legadoId = legado.Id;
            cliente.Endereco = "Rua alterada, 99";
            cliente.Bairro = "Outro bairro";
            await db.SaveChangesAsync();
        }
        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresa);
        var hoje = HorarioBrasil.Hoje();
        var kds = await new KdsPedidoQueries(leitura).ListarAsync(empresa, [StatusPedidoMapper.Aguardando], hoje, hoje);
        kds.Single(p => p.Id == pedidoId).Endereco.Should().Be("Rua original, 42 - Centro - CEP 01001-000");
        kds.Single(p => p.Id == legadoId).Endereco.Should().Contain("Rua alterada").And.Contain("Outro bairro");
        var impresso = await new PedidoImpressoQueries(leitura).ObterAsync(empresa, pedidoId);
        impresso!.Cliente.EnderecoEntrega.Should().Be("Rua original, 42 - Centro - CEP 01001-000");
        (await new KdsPedidoQueries(leitura).ListarAsync(outraEmpresa, [StatusPedidoMapper.Aguardando], hoje, hoje))
            .Should().BeEmpty();
        var entregue = await leitura.Pedidos.FindAsync(pedidoId);
        var agora = DateTime.UtcNow;
        entregue!.Status = StatusPedidoMapper.Entregue;
        entregue.EntreguEm = agora;
        await leitura.SaveChangesAsync();
        var bairros = await new EasyStock.Infra.Postgre.Repositories.Atendimento.EntregasPorBairroQuery(leitura)
            .ListarEntreguesAsync(empresa, agora.AddMinutes(-1), agora.AddMinutes(1));
        bairros.Should().ContainSingle().Which.Bairro.Should().Be("Centro");
    }

    [SkippableFact]
    public async Task ListaDoDiaPelaVagaOuCriacao_SoDaEmpresa_ComJanela()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        db.SetMobileTenantContext(empresaA);
        db.Empresas.Add(NovaEmpresa(empresaA));
        db.Empresas.Add(NovaEmpresa(empresaB));

        var storefront = EasyStock.Domain.Entities.Storefront.Storefront.Criar(empresaA, $"sf-kds-{Guid.NewGuid():N}", "SF KDS", 0m);
        db.Storefronts.Add(storefront);
        var janela = JanelaEntrega.Criar(storefront.Id, 1, new TimeOnly(11, 0), new TimeOnly(13, 0), 10, "Almoço");
        db.JanelasEntrega.Add(janela);

        var hoje = HorarioBrasil.Hoje();
        var balcaoHoje = NovoPedido(empresaA, StatusPedidoMapper.Aguardando);
        var vagaHoje = NovoPedido(empresaA, StatusPedidoMapper.Preparando);
        var vagaAmanha = NovoPedido(empresaA, StatusPedidoMapper.Aguardando);
        var entregue = NovoPedido(empresaA, StatusPedidoMapper.Entregue);
        var outraEmpresa = NovoPedido(empresaB, StatusPedidoMapper.Aguardando);
        db.Pedidos.AddRange(balcaoHoje, vagaHoje, vagaAmanha, entregue, outraEmpresa);
        db.VagasOcupadas.Add(VagaOcupada.Ocupar(janela.Id, hoje, vagaHoje.Id));
        db.VagasOcupadas.Add(VagaOcupada.Ocupar(janela.Id, hoje.AddDays(1), vagaAmanha.Id));
        await db.SaveChangesAsync();

        var lidos = await new KdsPedidoQueries(db).ListarAsync(
            empresaA,
            [StatusPedidoMapper.Aguardando, StatusPedidoMapper.Preparando],
            hoje.AddDays(-1),
            hoje);

        lidos.Select(p => p.Id).Should().BeEquivalentTo([balcaoHoje.Id, vagaHoje.Id]);
        var comVaga = lidos.Single(p => p.Id == vagaHoje.Id);
        comVaga.Janela.Should().Be(new KdsJanelaLeitura("Almoço", hoje, new TimeOnly(11, 0), new TimeOnly(13, 0)));
        comVaga.DataProducao.Should().Be(hoje);
        comVaga.Itens.Should().ContainSingle().Which.Linha.Should().Be("prepararEmCasa");
    }

    /// <summary>
    /// F08 item 5 (#1238): pedido fora de área para daqui a 3 dias espera a dona aprovar hoje. O corte por
    /// dia de produção vale para a cozinha, não para a fila de aprovação.
    /// </summary>
    [SkippableFact]
    public async Task AguardandoAprovacao_ApareceSemCorteDeData()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresa = Guid.NewGuid();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(NovaEmpresa(empresa));
        var storefront = EasyStock.Domain.Entities.Storefront.Storefront.Criar(empresa, $"sf-kds-{Guid.NewGuid():N}", "SF KDS", 0m);
        db.Storefronts.Add(storefront);
        var janela = JanelaEntrega.Criar(storefront.Id, 1, new TimeOnly(11, 0), new TimeOnly(13, 0), 10, "Almoço");
        db.JanelasEntrega.Add(janela);

        var hoje = HorarioBrasil.Hoje();
        var paraAprovar = NovoPedido(empresa, StatusPedidoMapper.AguardandoAprovacaoBaba);
        var cozinhaDaquiA3Dias = NovoPedido(empresa, StatusPedidoMapper.Aguardando);
        db.Pedidos.AddRange(paraAprovar, cozinhaDaquiA3Dias);
        db.VagasOcupadas.Add(VagaOcupada.Ocupar(janela.Id, hoje.AddDays(3), paraAprovar.Id));
        db.VagasOcupadas.Add(VagaOcupada.Ocupar(janela.Id, hoje.AddDays(3), cozinhaDaquiA3Dias.Id));
        await db.SaveChangesAsync();

        var lidos = await new KdsPedidoQueries(db).ListarAsync(
            empresa,
            [StatusPedidoMapper.AguardandoAprovacaoBaba, StatusPedidoMapper.Aguardando],
            hoje.AddDays(-1),
            hoje);

        lidos.Select(p => p.Id).Should().Equal(paraAprovar.Id);
    }

    /// <summary>
    /// #1474 B4: pedido nascido sem o retrato do cliente (comanda da conversa antes da correção, checkout
    /// logado do site) mostra o nome do cadastro; o retrato gravado continua mandando.
    /// </summary>
    [SkippableFact]
    public async Task SemRetratoDoClienteUsaONomeDoCadastro()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresa = Guid.NewGuid();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(NovaEmpresa(empresa));
        var cliente = Cliente.Criar(empresa, "Maria Souza");
        db.Clientes.Add(cliente);

        var semRetrato = NovoPedido(empresa, StatusPedidoMapper.Aguardando);
        semRetrato.ClienteId = cliente.Id;
        var comRetrato = NovoPedido(empresa, StatusPedidoMapper.Aguardando);
        comRetrato.ClienteId = cliente.Id;
        comRetrato.ClienteNome = "Maria (retrato)";
        db.Pedidos.AddRange(semRetrato, comRetrato);
        await db.SaveChangesAsync();

        var hoje = HorarioBrasil.Hoje();
        var lidos = await new KdsPedidoQueries(db).ListarAsync(empresa, [StatusPedidoMapper.Aguardando], hoje.AddDays(-1), hoje);

        lidos.Single(p => p.Id == semRetrato.Id).ClienteNome.Should().Be("Maria Souza");
        lidos.Single(p => p.Id == comRetrato.Id).ClienteNome.Should().Be("Maria (retrato)");
    }

    /// <summary>
    /// #1474: a linha de frete ("Entrega — Butantã") não é prato: não vai para o cartão da cozinha.
    /// E o nome do item avulso, gravado minúsculo, aparece como no cardápio.
    /// </summary>
    [SkippableFact]
    public async Task CartaoSemLinhaDeFreteEComNomeDoCardapio()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresa = Guid.NewGuid();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(NovaEmpresa(empresa));
        var pedido = NovoPedido(empresa, StatusPedidoMapper.Aguardando);
        pedido.Itens.Single().Nome = "nhoque artesanal 500 g";
        pedido.Itens.Single().LinhaSnapshot = null; // Item avulso, sem vínculo com catálogo ou linha.
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Entrega — Butantã",
            Quantidade = 1, PrecoUnitario = 12m, Subtotal = 12m, CriadoEm = DateTime.UtcNow,
        });
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        var hoje = HorarioBrasil.Hoje();
        var lido = (await new KdsPedidoQueries(db).ListarAsync(empresa, [StatusPedidoMapper.Aguardando], hoje.AddDays(-1), hoje))
            .Single(p => p.Id == pedido.Id);

        lido.Itens.Select(i => i.Nome).Should().Equal("Nhoque Artesanal 500 g");
    }

    private static Empresa NovaEmpresa(Guid empresaId) => new()
    {
        Id = empresaId,
        Nome = "Empresa KDS",
        Documento = empresaId.ToString("N")[..14],
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    private static Pedido NovoPedido(Guid empresaId, string status)
    {
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = status;
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Nome = "Lasanha",
            Quantidade = 1,
            PrecoUnitario = 10m,
            Subtotal = 10m,
            LinhaSnapshot = "prepararEmCasa",
            CriadoEm = DateTime.UtcNow,
        });
        pedido.RecalcularTotal();
        return pedido;
    }
}
