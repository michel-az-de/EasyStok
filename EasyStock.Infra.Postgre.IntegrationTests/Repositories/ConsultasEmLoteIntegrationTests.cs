using System.Data.Common;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Application.UseCases.Producao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1564: as consultas em lote devolvem o mesmo que o caminho antigo (uma ida ao banco por registro,
/// ou a lista inteira filtrada em memória), em Postgres real e com duas empresas. Cada prova refaz o
/// caminho antigo ao lado do novo e compara.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ConsultasEmLoteIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 10, 11, 15, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Viagens_MesmoResultadoDoCaminhoPorParada_ComTresConsultas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Lote viagens", null);
        var outra = Empresa.Criar("Lote viagens outra", null);
        var fulana = Cliente.Criar(casa.Id, "Fulana");
        fulana.Endereco = "Rua do cadastro, 99";
        fulana.Bairro = "Centro";
        var alheio = Cliente.Criar(outra.Id, "Cliente de outra empresa");
        alheio.Endereco = "Rua alheia, 1";
        var comEndereco = Pedido.Criar(casa.Id, fulana);
        comEndereco.EnderecoEntrega = "Rua da encomenda, 42";
        var legado = Pedido.Criar(casa.Id, fulana);
        var semCliente = Pedido.Criar(casa.Id);
        semCliente.ClienteNome = "Balcão";
        var pedidoAlheio = Pedido.Criar(outra.Id, alheio);
        pedidoAlheio.EnderecoEntrega = "Rua alheia, 1";
        var primeira = Viagem.Criar(casa.Id, Agora);
        primeira.IncluirParada(comEndereco.Id, false);
        primeira.IncluirParada(legado.Id, false);
        var segunda = Viagem.Criar(casa.Id, Agora.AddMinutes(1));
        segunda.IncluirParada(semCliente.Id, false);
        // Parada apontando para pedido de outra empresa: o filtro por empresa não o devolve, nos dois caminhos.
        segunda.IncluirParada(pedidoAlheio.Id, false);
        var vazia = Viagem.Criar(casa.Id, Agora.AddMinutes(2));
        var daOutra = Viagem.Criar(outra.Id, Agora);
        await using (var db = fixture.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(casa, outra);
            db.Clientes.AddRange(fulana, alheio);
            db.Pedidos.AddRange(comEndereco, legado, semCliente, pedidoAlheio);
            db.Viagens.AddRange(primeira, segunda, vazia, daOutra);
            await db.SaveChangesAsync();
        }

        var contador = new ContadorDeComandos();
        await using var leitura = CriarComContador(contador);
        leitura.SetMobileTenantContext(casa.Id);
        var pedidos = new PedidoRepository(leitura);
        var clientes = new ClienteRepository(leitura);
        var viagens = new ViagemRepository(leitura);

        var lista = await new ListarViagensUseCase(viagens, new ObterViagemUseCase(viagens, pedidos, clientes))
            .ExecuteAsync(casa.Id, null);
        var consultasEmLote = contador.Comandos;

        // Caminho antigo: um pedido e um cliente por parada.
        contador.Zerar();
        var esperado = new List<ViagemResult>();
        foreach (var viagem in await viagens.ListarAsync(casa.Id, null, ListarViagensUseCase.LimitePadrao))
        {
            var paradas = new List<ParadaViagemResult>();
            foreach (var p in viagem.Paradas.OrderBy(p => p.Ordem))
            {
                var pedido = await pedidos.GetByIdAsync(casa.Id, p.PedidoId);
                var cliente = pedido?.ClienteId is { } cid ? await clientes.GetByIdAsync(casa.Id, cid) : null;
                var enderecoDoCadastro = cliente is null || string.IsNullOrWhiteSpace(cliente.Endereco)
                    ? null
                    : string.Join(", ", new[] { cliente.Endereco, cliente.Bairro, cliente.Cidade }
                        .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
                paradas.Add(new ParadaViagemResult(p.PedidoId, ObterViagemUseCase.NumeroPedido(p.PedidoId), p.Ordem,
                    cliente?.Nome ?? pedido?.ClienteNome, pedido?.EnderecoEntrega ?? enderecoDoCadastro,
                    p.EntregueEm, p.EntregadorNome, p.Veiculo, p.Placa, p.EmpresaEntregador));
            }
            esperado.Add(new ViagemResult(viagem.Id, viagem.EntregadorId, viagem.Situacao, viagem.CriadaEm, viagem.SaiuEm,
                viagem.ConcluidaEm, RotaMaps.Montar(paradas.Select(p => p.Endereco)), paradas));
        }
        var consultasPorParada = contador.Comandos;

        lista.Should().BeEquivalentTo(esperado, o => o.WithStrictOrdering());
        lista.Select(v => v.Id).Should().Equal(vazia.Id, segunda.Id, primeira.Id);
        lista.Single(v => v.Id == primeira.Id).Paradas.Select(p => (p.ClienteNome, p.Endereco)).Should().Equal(
            ("Fulana", "Rua da encomenda, 42"), ("Fulana", "Rua do cadastro, 99, Centro"));
        lista.Single(v => v.Id == segunda.Id).Paradas.Select(p => (p.ClienteNome, p.Endereco)).Should().Equal(
            ("Balcão", (string?)null), ((string?)null, (string?)null));
        consultasEmLote.Should().Be(3, "viagens, pedidos e clientes");
        consultasPorParada.Should().Be(7, "viagens, 4 pedidos e 2 clientes (o cliente repetido é consultado de novo)");

        // Do lado da outra empresa, a mesma lista de ids só devolve o que é dela.
        await using var daOutraEmpresa = fixture.CreateDbContext();
        daOutraEmpresa.SetMobileTenantContext(outra.Id);
        (await new PedidoRepository(daOutraEmpresa).ListarPorIdsAsync(outra.Id, [comEndereco.Id, pedidoAlheio.Id]))
            .Select(p => p.Id).Should().Equal(pedidoAlheio.Id);
        (await new ClienteRepository(daOutraEmpresa).ListarPorIdsAsync(outra.Id, [fulana.Id, alheio.Id]))
            .Select(c => c.Id).Should().Equal(alheio.Id);
    }

    [SkippableFact]
    public async Task Lembretes_ExistentesEmLote_IgualAPerguntarUmPorUm()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Lote lembretes", null);
        var outra = Empresa.Criar("Lote lembretes outra", null);
        string Ref() => Guid.NewGuid().ToString("N");
        var (aberta, concluida, soNaOutra, deOutroTipo, nenhuma) = (Ref(), Ref(), Ref(), Ref(), Ref());
        var concluido = Lembrete.Automatico(casa.Id, TipoLembrete.PagamentoSemBaixa, concluida, "Concluído", Agora);
        concluido.Concluir(Agora);
        await using (var db = fixture.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(casa, outra);
            db.Lembretes.AddRange(
                Lembrete.Automatico(casa.Id, TipoLembrete.PagamentoSemBaixa, aberta, "Aberto", Agora),
                concluido,
                Lembrete.Automatico(outra.Id, TipoLembrete.PagamentoSemBaixa, soNaOutra, "Outra empresa", Agora),
                Lembrete.Automatico(casa.Id, TipoLembrete.ClienteSemResposta, deOutroTipo, "Outro tipo", Agora));
            await db.SaveChangesAsync();
        }
        (Guid EmpresaId, string Referencia)[] fatos =
        [
            (casa.Id, aberta), (casa.Id, concluida), (casa.Id, soNaOutra), (outra.Id, aberta),
            (casa.Id, deOutroTipo), (casa.Id, nenhuma), (outra.Id, soNaOutra),
        ];

        await using var leitura = fixture.CreateDbContext();
        using var semRls = leitura.UseRowLevelSecurityBypass();
        var achados = await new LembreteRepository(leitura).ListarAutomaticosExistentesAsync(TipoLembrete.PagamentoSemBaixa, fatos);

        // Caminho antigo: um AnyAsync por fato.
        var umPorUm = new List<(Guid, string)>();
        foreach (var (empresaId, referencia) in fatos)
            if (await leitura.Lembretes.IgnoreQueryFilters().AnyAsync(l =>
                    l.EmpresaId == empresaId && l.Tipo == TipoLembrete.PagamentoSemBaixa && l.Referencia == referencia))
                umPorUm.Add((empresaId, referencia));

        achados.Should().BeEquivalentTo(umPorUm);
        achados.Should().BeEquivalentTo([(casa.Id, aberta), (casa.Id, concluida), (outra.Id, soNaOutra)],
            "o concluído conta, e o par é o exato: a referência que só existe na outra empresa não vaza");
        (await new LembreteRepository(leitura).ListarAutomaticosExistentesAsync(TipoLembrete.PagamentoSemBaixa, [])).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Movimentacoes_PorReferenciaEPerdasSemInsumo_IgualAoFiltroEmMemoria()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var casa = Empresa.Criar("Lote movimentos", null);
        var outra = Empresa.Criar("Lote movimentos outra", null);
        var (loteCasa, sementesCasa) = Estoque(casa.Id);
        var (loteOutra, sementesOutra) = Estoque(outra.Id);
        var referencia = $"{Guid.NewGuid()}:{Guid.NewGuid()}";
        var estornada = Movimento(loteCasa, NaturezaMovimentacaoEstoque.Venda, Agora.AddHours(-3), referencia: referencia);
        estornada.MarcarComoEstornada(Agora.AddHours(-1));
        var estorno = Movimento(loteCasa, NaturezaMovimentacaoEstoque.Estorno, Agora.AddHours(-1), referencia: referencia);
        estorno.Tipo = TipoMovimentacaoEstoque.Entrada;
        MovimentacaoEstoque[] movimentos =
        [
            estornada, estorno,
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.Venda, Agora.AddHours(-2), referencia: referencia),
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.Venda, Agora.AddHours(-2), referencia: $"{Guid.NewGuid()}:{Guid.NewGuid()}"),
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.Venda, Agora.AddHours(-2)),
            Movimento(loteOutra, NaturezaMovimentacaoEstoque.Venda, Agora.AddHours(-2), referencia: referencia),
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.Perda, Agora.AddHours(-4), "Perda · Perda no preparo"),
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.UsoInterno, Agora.AddHours(-5), "Perda · Degustação"),
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.UsoInterno, Agora.AddHours(-6), $"{BaixaDeInsumosDaProducao.PrefixoDaDescricao} LOT-261011-001"),
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.UsoInterno, Agora.AddHours(-7), BaixaDeInsumosDaProducao.PrefixoDaDescricao),
            // Sem descrição e com o prefixo em outra caixa ou no meio do texto: o filtro em memória (ordinal) mantém.
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.UsoInterno, Agora.AddHours(-8)),
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.UsoInterno, Agora.AddHours(-9), BaixaDeInsumosDaProducao.PrefixoDaDescricao.ToLowerInvariant()),
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.UsoInterno, Agora.AddHours(-10), $"Sobra de {BaixaDeInsumosDaProducao.PrefixoDaDescricao}"),
            Movimento(loteCasa, NaturezaMovimentacaoEstoque.Perda, Agora.AddDays(-30), "Perda · fora do período"),
            Movimento(loteOutra, NaturezaMovimentacaoEstoque.Perda, Agora.AddHours(-4), "Perda · outra empresa"),
        ];
        await using (var db = fixture.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(casa, outra);
            db.AddRange(sementesCasa);
            db.AddRange(sementesOutra);
            db.MovimentacoesEstoque.AddRange(movimentos);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(casa.Id);
        var repo = new MovimentacaoEstoqueRepository(leitura);

        // Cancelamento: caminho antigo = todas as movimentações do produto, filtradas em memória.
        var antigas = (await repo.GetByProdutoAsync(casa.Id, loteCasa.ProdutoId))
            .Where(m => m.DocumentoReferencia == referencia && m.Tipo == TipoMovimentacaoEstoque.Saida
                && m.Natureza == NaturezaMovimentacaoEstoque.Venda).ToList();
        var novas = (await repo.GetPorReferenciaAsync(casa.Id, loteCasa.ProdutoId, referencia, NaturezaMovimentacaoEstoque.Venda))
            .Where(m => m.Tipo == TipoMovimentacaoEstoque.Saida).ToList();
        novas.Select(m => (m.Id, m.ItemEstoqueId, m.Quantidade.Value, m.EstornadaEm)).Should().Equal(
            antigas.Select(m => (m.Id, m.ItemEstoqueId, m.Quantidade.Value, m.EstornadaEm)));
        novas.Select(m => m.Id).Should().Equal(movimentos[2].Id, estornada.Id);
        (await repo.GetPorReferenciaAsync(casa.Id, loteOutra.ProdutoId, referencia, NaturezaMovimentacaoEstoque.Venda)).Should().BeEmpty();
        (await repo.GetPorReferenciaAsync(casa.Id, loteCasa.ProdutoId, referencia, NaturezaMovimentacaoEstoque.Estorno))
            .Select(m => m.Id).Should().Equal(estorno.Id);

        // Perdas: caminho antigo = o período inteiro, com a baixa de insumo tirada em memória.
        NaturezaMovimentacaoEstoque[] naturezas = [NaturezaMovimentacaoEstoque.Perda, NaturezaMovimentacaoEstoque.UsoInterno];
        var (de, ate) = (Agora.AddDays(-7), Agora.AddDays(1));
        var emMemoria = (await repo.GetSaidasPorNaturezaAsync(casa.Id, naturezas, de, ate))
            .Where(m => !(m.Descricao ?? "").StartsWith(BaixaDeInsumosDaProducao.PrefixoDaDescricao, StringComparison.Ordinal)).ToList();
        var noBanco = await repo.GetSaidasPorNaturezaAsync(casa.Id, naturezas, de, ate, BaixaDeInsumosDaProducao.PrefixoDaDescricao);
        noBanco.Select(m => (m.Id, m.Descricao, m.Produto!.Nome, Lote: m.ItemEstoque!.Id)).Should().Equal(
            emMemoria.Select(m => (m.Id, m.Descricao, m.Produto!.Nome, Lote: m.ItemEstoque!.Id)));
        noBanco.Select(m => m.Id).Should().Equal(movimentos[6].Id, movimentos[7].Id, movimentos[10].Id, movimentos[11].Id, movimentos[12].Id);
    }

    private EasyStockDbContext CriarComContador(ContadorDeComandos contador)
    {
        using var modelo = fixture.CreateDbContext();
        return new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(modelo.Database.GetConnectionString())
            .AddInterceptors(contador)
            .Options);
    }

    private static (ItemEstoque Lote, object[] Sementes) Estoque(Guid empresaId)
    {
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Massas", CriadoEm = Agora, AlteradoEm = Agora };
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoria.Id, Nome = "Lasanha",
            Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = Agora, AlteradoEm = Agora,
        };
        var lote = new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produto.Id,
            QuantidadeInicial = Quantidade.From(10), QuantidadeAtual = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(10m), EntradaEm = Agora.AddDays(-40),
            Status = StatusItemEstoque.Ok, CriadoEm = Agora, AlteradoEm = Agora,
        };
        return (lote, [categoria, produto, lote]);
    }

    private static MovimentacaoEstoque Movimento(
        ItemEstoque lote, NaturezaMovimentacaoEstoque natureza, DateTime em, string? descricao = null, string? referencia = null) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = lote.EmpresaId, ItemEstoqueId = lote.Id, ProdutoId = lote.ProdutoId,
        Tipo = TipoMovimentacaoEstoque.Saida, Natureza = natureza, Quantidade = Quantidade.From(1),
        DataMovimentacao = em, Descricao = descricao, DocumentoReferencia = referencia, CriadoEm = em,
    };

    private sealed class ContadorDeComandos : DbCommandInterceptor
    {
        public int Comandos { get; private set; }

        public void Zerar() => Comandos = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Comandos++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
