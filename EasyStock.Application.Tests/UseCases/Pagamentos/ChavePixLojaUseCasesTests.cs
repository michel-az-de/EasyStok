using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Application.UseCases.Pagamentos.PixLoja;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.ValueObjects.Pix;

namespace EasyStock.Application.Tests.UseCases.Pagamentos;

/// <summary>
/// #2233: cadastro da chave Pix da loja. A chave entra inteira e nunca volta inteira: a resposta, o erro e o
/// <c>ToString</c> do comando a escondem. Só chaves fictícias.
/// </summary>
public class ChavePixLojaUseCasesTests
{
    private const string Email = "financeiro@exemplo.test";
    private static readonly Guid Empresa = Guid.NewGuid();

    private readonly ILojaRepository lojas = Substitute.For<ILojaRepository>();
    private readonly ITenantFeatureFlagRepository flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly IAuditLogRepository auditoria = Substitute.For<IAuditLogRepository>();
    private readonly IUsuarioRepository usuarios = Substitute.For<IUsuarioRepository>();
    private readonly ICurrentUserAccessor atual = Substitute.For<ICurrentUserAccessor>();
    private readonly Usuario gerente = Usuario.Criar("Marina", "marina@exemplo.test", "hash");
    private readonly List<AuditLog> trilha = [];
    private readonly Loja loja = Loja.Criar(Empresa, "Loja Centro");

    public ChavePixLojaUseCasesTests()
    {
        lojas.GetByIdAsync(Empresa, loja.Id).Returns(loja);
        flags.ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        atual.UsuarioId.Returns(gerente.Id);
        usuarios.GetByIdAsync(gerente.Id).Returns(gerente);
        auditoria.AddAsync(Arg.Do<AuditLog>(trilha.Add)).Returns(Task.CompletedTask);
    }

    private ChavePixLojaUseCases Sut() => new(lojas, flags, uow, auditoria, usuarios, atual);

    private DefinirChavePixLojaCommand Comando(string tipo = "email", string chave = Email) =>
        new(Empresa, loja.Id, tipo, chave, "Casa de Doces", "São Paulo");

    [Fact]
    public async Task SemChave_ObterDizQueNaoEstaConfigurada()
    {
        var r = await Sut().ObterAsync(Empresa, loja.Id);

        r!.Configurada.Should().BeFalse();
        r.ChaveMascarada.Should().BeNull();
        r.PixDiretoLigado.Should().BeFalse();
    }

    [Fact]
    public async Task LojaDeOutraEmpresa_NaoEncontrada()
    {
        (await Sut().ObterAsync(Guid.NewGuid(), loja.Id)).Should().BeNull();
        (await Sut().DefinirAsync(Comando() with { EmpresaId = Guid.NewGuid() })).Should().BeNull();
        await uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Definir_GravaNormalizado_EDevolveMascarado()
    {
        flags.ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.PagamentoPixDireto]);

        var r = await Sut().DefinirAsync(Comando(chave: "  Financeiro@Exemplo.test "));

        loja.PixTipo.Should().Be(TipoChavePix.Email);
        loja.PixChave.Should().Be(Email);
        loja.PixNomeRecebedor.Should().Be("Casa de Doces");
        loja.PixCidade.Should().Be("Sao Paulo");
        await lojas.Received(1).UpdateAsync(loja);
        await uow.Received(1).CommitAsync();
        r!.Configurada.Should().BeTrue();
        r.Tipo.Should().Be("email");
        r.ChaveMascarada.Should().Be("fi***@exemplo.test");
        r.NomeRecebedor.Should().Be("Casa de Doces");
        r.Cidade.Should().Be("Sao Paulo");
        r.PixDiretoLigado.Should().BeTrue();
        r.ToString().Should().NotContain(Email);
    }

    [Theory]
    [InlineData("cpf", "529.982.247-25", "***4725")]
    [InlineData("telefone", "(11) 98765-4321", "***4321")]
    [InlineData("aleatoria", "123e4567-e12b-12d1-a456-426655440000", "***0000")]
    public async Task OutrosTipos_MostramSoOFinal(string tipo, string chave, string mascarada)
    {
        var r = await Sut().DefinirAsync(Comando(tipo, chave));

        r!.ChaveMascarada.Should().Be(mascarada);
    }

    [Theory]
    [InlineData("email", "nao-e-email")]
    [InlineData("cpf", "111.111.111-11")]
    [InlineData("1", "529.982.247-25")]
    [InlineData("pix", "529.982.247-25")]
    public async Task ChaveOuTipoInvalido_Recusa_SemRepetirAChave(string tipo, string chave)
    {
        var act = () => Sut().DefinirAsync(Comando(tipo, chave));

        var erro = await act.Should().ThrowAsync<UseCaseValidationException>();
        erro.Which.Message.Should().NotContain(chave);
        loja.PixChave.Should().BeNull();
        await uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task NomeComprido_Recusa()
    {
        var act = () => Sut().DefinirAsync(Comando() with { NomeRecebedor = new string('a', RecebedorPix.NomeTamanhoMaximo + 1) });

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Remover_LimpaAChave()
    {
        await Sut().DefinirAsync(Comando());

        var r = await Sut().RemoverAsync(Empresa, loja.Id);

        r!.Configurada.Should().BeFalse();
        loja.PixChave.Should().BeNull();
        loja.ObterRecebedorPix().Should().BeNull();
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Disponivel_SoComFlagEChave(bool flagLigada, bool comChave, bool esperado)
    {
        if (flagLigada) flags.ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.PagamentoPixDireto]);
        if (comChave) loja.DefinirRecebedorPix(RecebedorPix.Criar(ChavePix.Criar(TipoChavePix.Email, Email), "Casa de Doces", "Sao Paulo"));
        lojas.GetByEmpresaAsync(Empresa).Returns([loja]);

        (await Sut().DisponivelAsync(Empresa)).Should().Be(esperado);
    }

    [Fact]
    public async Task Disponivel_ComFlagDesligada_NemLeAsLojas()
    {
        (await Sut().DisponivelAsync(Empresa)).Should().BeFalse();

        await lojas.DidNotReceive().GetByEmpresaAsync(Arg.Any<Guid>());
    }

    [Fact]
    public void Comando_NaoMostraAChaveNoToString()
    {
        Comando().ToString().Should().NotContain(Email);
    }

    [Fact]
    public async Task Definir_DeixaTrilhaComQuemQuandoLojaEChaveMascarada()
    {
        await Sut().DefinirAsync(Comando());

        var linha = trilha.Should().ContainSingle().Subject;
        linha.Acao.Should().Be(ChavePixLojaUseCases.AcaoDefinida);
        linha.UsuarioId.Should().Be(gerente.Id);
        linha.EmpresaId.Should().Be(Empresa);
        linha.DataHora.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        linha.Detalhes.Should().Contain("Marina").And.Contain("Loja Centro").And.Contain(loja.Id.ToString())
            .And.Contain("fi***@exemplo.test").And.NotContain(Email);
        Received.InOrder(() =>
        {
            auditoria.AddAsync(Arg.Any<AuditLog>());
            uow.CommitAsync();
        });
    }

    [Fact]
    public async Task Trocar_DizAChaveAnteriorMascarada()
    {
        await Sut().DefinirAsync(Comando());
        trilha.Clear();

        await Sut().DefinirAsync(Comando("aleatoria", "123e4567-e89b-12d3-a456-426614174000"));

        trilha.Should().ContainSingle().Which.Detalhes.Should().Contain("fi***@exemplo.test").And.Contain("***4000")
            .And.NotContain(Email).And.NotContain("123e4567");
    }

    [Fact]
    public async Task Remover_DeixaTrilhaComAChaveQueSaiu()
    {
        await Sut().DefinirAsync(Comando());
        trilha.Clear();

        await Sut().RemoverAsync(Empresa, loja.Id);

        var linha = trilha.Should().ContainSingle().Subject;
        linha.Acao.Should().Be(ChavePixLojaUseCases.AcaoRemovida);
        linha.UsuarioId.Should().Be(gerente.Id);
        linha.Detalhes.Should().Contain("Marina").And.Contain("Loja Centro").And.Contain("fi***@exemplo.test").And.NotContain(Email);
    }

    [Fact]
    public async Task RemoverSemChave_NaoDeixaTrilha()
    {
        await Sut().RemoverAsync(Empresa, loja.Id);

        trilha.Should().BeEmpty();
    }

    [Fact]
    public async Task SemUsuarioIdentificado_RecusaETrocaNada()
    {
        atual.UsuarioId.Returns(Guid.Empty);

        var act = () => Sut().DefinirAsync(Comando());

        await act.Should().ThrowAsync<UseCaseValidationException>();
        loja.PixChave.Should().BeNull();
        await uow.DidNotReceive().CommitAsync();
        trilha.Should().BeEmpty();
    }
}
