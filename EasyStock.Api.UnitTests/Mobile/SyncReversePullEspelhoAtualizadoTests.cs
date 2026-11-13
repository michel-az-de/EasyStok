using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// SYNC-2 (#2306): alteração de Produto ERP já espelhado em mobile_products não chegava ao app.
/// </summary>
public sealed class SyncReversePullEspelhoAtualizadoTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly SyncReversePullService _servico;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncReversePullEspelhoAtualizadoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"reverse-pull-espelho-{Guid.NewGuid()}")
                .Options,
            currentUser);
        _servico = new SyncReversePullService(
            _db, new ConfigurationBuilder().Build(), NullLogger<SyncReversePullService>.Instance,
            new NumeroCurtoDosLotesMobile(_db, Substitute.For<EasyStock.Application.Ports.Output.Persistence.ILoteNumeroCurtoRepository>(), NullLogger<NumeroCurtoDosLotesMobile>.Instance));
    }

    private Guid CriarCategoria()
    {
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Massas" };
        _db.Add(categoria);
        return categoria.Id;
    }

    private Produto ProdutoErpComEspelho(string nome, DateTime alteradoEm, StatusProduto status = StatusProduto.Ativo)
    {
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = _empresaId,
            Nome = nome,
            Tipo = TipoProduto.Alimento,
            Status = status,
            CategoriaId = CriarCategoria(),
            CriadoEm = DateTime.UtcNow.AddDays(-10),
            AlteradoEm = alteradoEm,
        };
        _db.Add(produto);
        _db.Add(new Product
        {
            Id = "mob-" + produto.Id,
            Name = "Nome antigo",
            Category = "extra",
            Stock = 7,
            EmpresaId = _empresaId,
            ErpProductId = produto.Id,
        });
        return produto;
    }

    private void ItemEstoqueDoErp(Guid produtoId, Guid? lojaId, decimal quantidade, DateTime? alteradoEm = null) => _db.Add(new ItemEstoque
    {
        Id = Guid.NewGuid(),
        EmpresaId = _empresaId,
        ProdutoId = produtoId,
        LojaId = lojaId,
        QuantidadeInicial = Quantidade.From(quantidade),
        QuantidadeAtual = Quantidade.From(quantidade),
        CustoUnitario = Dinheiro.FromDecimal(1m),
        CriadoEm = alteradoEm ?? DateTime.UtcNow,
        AlteradoEm = alteradoEm ?? DateTime.UtcNow,
    });

    // #2440: renomear o produto no console reenviava o saldo do ERP e apagava o saldo do app,
    // mesmo sem nenhum movimento de estoque depois do cursor.
    [Fact]
    public async Task ProdutoErpRenomeado_SemMovimentoDeEstoqueAposOCursor_MantemOSaldoDoApp()
    {
        var cursor = DateTime.UtcNow.AddDays(-1);
        var produto = ProdutoErpComEspelho("Nome novo do console", DateTime.UtcNow);
        ItemEstoqueDoErp(produto.Id, null, 4m, cursor.AddDays(-3));
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, cursor, _empresaId, null);

        var upsert = mutations.Should().ContainSingle(m => m.Type == "product.upsert").Subject;
        upsert.Payload.GetProperty("name").GetString().Should().Be("Nome novo do console");
        upsert.Payload.GetProperty("stock").GetInt32().Should().Be(7, "so movimento de estoque depois do cursor troca o saldo do app");
    }

    [Fact]
    public async Task ProdutoErpAlterado_ComEspelho_EmiteUpsertComIdDoEspelhoEEstoqueDele()
    {
        var produto = ProdutoErpComEspelho("Nome novo do console", DateTime.UtcNow);
        ItemEstoqueDoErp(produto.Id, null, 7m);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        var upsert = mutations.Should().ContainSingle(m => m.Type == "product.upsert").Subject;
        upsert.Payload.GetProperty("id").GetString().Should().StartWith("mob-");
        upsert.Payload.GetProperty("name").GetString().Should().Be("Nome novo do console");
        upsert.Payload.GetProperty("stock").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task ProdutoErpNaoAlterado_ComEspelho_NaoEmiteUpsert()
    {
        ProdutoErpComEspelho("Nome antigo", DateTime.UtcNow.AddDays(-5));
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        mutations.Should().NotContain(m => m.Type == "product.upsert");
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 0)]
    public async Task CriadorDoProduto_SemUpsertNormal_PreservaSaldoMobileSemEstoqueErp(bool temEstoqueErp, int saldoEsperado)
    {
        var loja = Guid.NewGuid();
        var produto = ProdutoErpComEspelho("Produto criado pelo app", DateTime.UtcNow);
        var espelho = _db.Set<Product>().Local.Single(p => p.ErpProductId == produto.Id);
        espelho.LojaId = loja;
        espelho.LastDeviceId = "criador";
        ItemEstoqueDoErp(produto.Id, Guid.NewGuid(), 9m);
        if (temEstoqueErp) ItemEstoqueDoErp(produto.Id, loja, 0m);
        await _db.SaveChangesAsync();

        // O pull normal exclui LastDeviceId == criador, portanto o reverse recebe a lista vazia.
        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, loja);

        var upsert = mutations.Should().ContainSingle(m => m.Type == "product.upsert").Subject;
        upsert.Payload.GetProperty("id").GetString().Should().Be(espelho.Id);
        upsert.Payload.GetProperty("stock").GetInt32().Should().Be(saldoEsperado);
        espelho.Stock.Should().Be(7, "o pull nao altera o saldo persistido do aparelho");
    }

    [Fact]
    public async Task ProdutoErpAlterado_EstoqueVemDosItensDaLojaDoDevice()
    {
        var lojaDevice = Guid.NewGuid();
        var produto = ProdutoErpComEspelho("Nhoque", DateTime.UtcNow);
        ItemEstoqueDoErp(produto.Id, lojaDevice, 4m);
        ItemEstoqueDoErp(produto.Id, Guid.NewGuid(), 9m);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, lojaDevice);

        mutations.Single(m => m.Type == "product.upsert").Payload.GetProperty("stock").GetInt32().Should().Be(4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EstoqueErpPosteriorAoCursor_ChegaAoCriadorEAOutroDevice(bool outroDevice)
    {
        var criadoEm = DateTime.UtcNow.AddMinutes(-3);
        var cursorInicial = criadoEm.AddMinutes(1);
        var alteradoEm = cursorInicial.AddMinutes(1);
        var cursorFinal = alteradoEm.AddSeconds(1);
        var loja = Guid.NewGuid();
        var produto = ProdutoErpComEspelho("Produto do app", criadoEm);
        var espelho = _db.Set<Product>().Local.Single(p => p.ErpProductId == produto.Id);
        espelho.LojaId = loja;
        espelho.LastDeviceId = "criador";
        await _db.SaveChangesAsync();

        var primeiroPull = new List<MutationDto>();
        if (outroDevice)
            primeiroPull.Add(new MutationDto("mobile", "criador", "product.upsert",
                System.Text.Json.JsonSerializer.SerializeToElement(
                    new ProductDto(espelho.Id, espelho.Name, null, "extra", null, 0m, espelho.Stock, false),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
                new DateTimeOffset(criadoEm).ToUnixTimeMilliseconds()));
        await _servico.AppendAsync(primeiroPull, criadoEm.AddSeconds(-1), _empresaId, loja);
        primeiroPull.Should().ContainSingle().Which.Payload.GetProperty("stock").GetInt32().Should().Be(7);

        // Depois do cursor, o cadastro ERP mudou e ha saldo real da loja e global.
        produto.Nome = "Produto atualizado no ERP";
        produto.AlteradoEm = alteradoEm;
        ItemEstoqueDoErp(produto.Id, loja, 4m);
        ItemEstoqueDoErp(produto.Id, null, 2m);
        ItemEstoqueDoErp(produto.Id, Guid.NewGuid(), 9m);
        await _db.SaveChangesAsync();
        var incremental = new List<MutationDto>();
        await _servico.AppendAsync(incremental, cursorInicial, _empresaId, loja);
        var atualizacao = incremental.Should().ContainSingle().Which;
        atualizacao.Payload.GetProperty("stock").GetInt32().Should().Be(6);
        atualizacao.Payload.GetProperty("name").GetString().Should().Be(produto.Nome);
        atualizacao.Ts.Should().Be(new DateTimeOffset(alteradoEm).ToUnixTimeMilliseconds());

        var repetido = new List<MutationDto>();
        await _servico.AppendAsync(repetido, cursorFinal, _empresaId, loja);
        repetido.Should().BeEmpty();
    }

    [Fact]
    public async Task EspelhoJaNoPullDoMobile_NaoDuplicaUpsert_MesclaCadastroDoErpEMantemEstoqueDoApp()
    {
        var produto = ProdutoErpComEspelho("Nome novo do console", DateTime.UtcNow);
        ItemEstoqueDoErp(produto.Id, null, 7m);
        await _db.SaveChangesAsync();

        var idEspelho = "mob-" + produto.Id;
        var doMobile = new ProductDto(idEspelho, "Nome antigo", null, "extra", null, 10m, 3, false);
        var mutations = new List<MutationDto>
        {
            new("m1", "aparelho-a", "product.upsert", System.Text.Json.JsonSerializer.SerializeToElement(doMobile, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)), 123),
        };

        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        var upsert = mutations.Should().ContainSingle(m => m.Type == "product.upsert"
            && m.Payload.GetProperty("id").GetString() == idEspelho).Subject;
        upsert.Id.Should().Be("m1");
        upsert.DeviceId.Should().Be("aparelho-a");
        upsert.Payload.GetProperty("name").GetString().Should().Be("Nome novo do console");
        upsert.Payload.GetProperty("stock").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ProdutoErpComDoisEspelhos_AtualizaAmbosEContinuaOPull()
    {
        var lojaId = Guid.NewGuid();
        var produto = ProdutoErpComEspelho("Nome novo do console", DateTime.UtcNow);
        _db.Add(new Product
        {
            Id = "outro-espelho",
            Name = "Outro nome antigo",
            EmpresaId = _empresaId,
            LojaId = lojaId,
            ErpProductId = produto.Id,
        });
        _db.Add(new Product
        {
            Id = "outra-empresa",
            Name = "Produto de outra empresa",
            EmpresaId = Guid.NewGuid(),
            LojaId = lojaId,
            ErpProductId = produto.Id,
        });
        _db.Add(new Product
        {
            Id = "outra-loja",
            Name = "Produto de outra loja",
            EmpresaId = _empresaId,
            LojaId = Guid.NewGuid(),
            ErpProductId = produto.Id,
        });
        ItemEstoqueDoErp(produto.Id, lojaId, 7m);
        ItemEstoqueDoErp(produto.Id, Guid.NewGuid(), 9m);
        var cliente = Cliente.Criar(_empresaId, "Cliente novo no console");
        _db.Add(cliente);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, lojaId);

        var produtos = mutations.Where(m => m.Type == "product.upsert").ToList();
        produtos.Select(m => m.Payload.GetProperty("id").GetString())
            .Should().BeEquivalentTo("mob-" + produto.Id, "outro-espelho");
        produtos.Should().OnlyContain(m => m.Payload.GetProperty("name").GetString() == "Nome novo do console"
            && m.Payload.GetProperty("stock").GetInt32() == 7);
        mutations.Should().ContainSingle(m => m.Type == "client.upsert"
            && m.Payload.GetProperty("id").GetString() == cliente.Id.ToString());
    }

    public void Dispose() => _db.Dispose();
}
