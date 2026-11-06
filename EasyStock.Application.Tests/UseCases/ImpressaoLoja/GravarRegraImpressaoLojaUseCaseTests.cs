using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ImpressaoLoja;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.ImpressaoLoja;

public class GravarRegraImpressaoLojaUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid LojaId = Guid.NewGuid();
    private static readonly DateTimeOffset Agora = new(2026, 11, 3, 21, 0, 0, TimeSpan.Zero);

    private const string CanonicoA4 =
        """{"v":1,"documentos":{"pedido":{"papel":"a4","template":null,"orientacao":null,"codigos":null,"copias":1}}}""";
    private const string CanonicoA5 =
        """{"v":1,"documentos":{"pedido":{"papel":"a5","template":null,"orientacao":null,"codigos":null,"copias":1}}}""";

    private readonly IRegraImpressaoLojaRepository _repo = Substitute.For<IRegraImpressaoLojaRepository>();
    private readonly IEtiquetaTemplateRepository _templates = Substitute.For<IEtiquetaTemplateRepository>();
    private readonly GravarRegraImpressaoLojaUseCase _useCase;

    public GravarRegraImpressaoLojaUseCaseTests()
    {
        _useCase = new GravarRegraImpressaoLojaUseCase(_repo, _templates, new FakeTimeProvider(Agora));
    }

    [Fact]
    public async Task Loja_de_outra_empresa_ou_inexistente_da_nao_encontrada_sem_gravar()
    {
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns((RegraImpressaoSalva?)null);

        var resultado = await _useCase.ExecuteAsync(Comando(versao: 0, Pedido("a4")));

        resultado.Desfecho.Should().Be(DesfechoGravacaoImpressao.LojaNaoEncontrada);
        await NaoGravou();
    }

    [Fact]
    public async Task Primeira_gravacao_com_versao_zero_grava_o_texto_canonico()
    {
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns(new RegraImpressaoSalva(null, 0, null));
        _repo.GravarAsync(EmpresaId, LojaId, 0, CanonicoA4, Agora.UtcDateTime, Arg.Any<CancellationToken>())
            .Returns(new GravacaoRegraImpressao(true, 1));

        var resultado = await _useCase.ExecuteAsync(Comando(versao: 0, Pedido("a4")));

        resultado.Desfecho.Should().Be(DesfechoGravacaoImpressao.Gravada);
        resultado.Atual!.Versao.Should().Be(1);
        resultado.Atual.Impressao!.Value.GetRawText().Should().Be(CanonicoA4);
        resultado.Atual.AlteradoEm.Should().Be(Agora.UtcDateTime);
    }

    [Fact]
    public async Task Mesmo_corpo_da_versao_atual_responde_sem_mudar_a_versao()
    {
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>())
            .Returns(new RegraImpressaoSalva(CanonicoA4, 4, Agora.UtcDateTime.AddHours(-1)));

        var resultado = await _useCase.ExecuteAsync(Comando(versao: 4, Pedido("a4")));

        resultado.Desfecho.Should().Be(DesfechoGravacaoImpressao.SemMudanca);
        resultado.Atual!.Versao.Should().Be(4);
        await NaoGravou();
    }

    [Fact]
    public async Task Reenvio_do_mesmo_corpo_com_versao_velha_nao_e_conflito()
    {
        // Retentativa depois de um timeout: o corpo já foi gravado, então repetir não perde nada de ninguém.
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>())
            .Returns(new RegraImpressaoSalva(CanonicoA4, 5, Agora.UtcDateTime));

        var resultado = await _useCase.ExecuteAsync(Comando(versao: 4, Pedido("a4")));

        resultado.Desfecho.Should().Be(DesfechoGravacaoImpressao.SemMudanca);
        resultado.Atual!.Versao.Should().Be(5);
        await NaoGravou();
    }

    [Fact]
    public async Task Corpo_novo_com_versao_velha_e_conflito_com_o_estado_atual()
    {
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>())
            .Returns(new RegraImpressaoSalva(CanonicoA4, 5, Agora.UtcDateTime));

        var resultado = await _useCase.ExecuteAsync(Comando(versao: 4, Pedido("a5")));

        resultado.Desfecho.Should().Be(DesfechoGravacaoImpressao.Conflito);
        resultado.Atual!.Versao.Should().Be(5);
        resultado.Atual.Impressao!.Value.GetRawText().Should().Be(CanonicoA4);
        await NaoGravou();
    }

    [Fact]
    public async Task Corrida_perdida_no_banco_vira_conflito()
    {
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>())
            .Returns(new RegraImpressaoSalva(CanonicoA4, 5, Agora.UtcDateTime),
                     new RegraImpressaoSalva(CanonicoA4, 6, Agora.UtcDateTime));
        _repo.GravarAsync(EmpresaId, LojaId, 5, CanonicoA5, Agora.UtcDateTime, Arg.Any<CancellationToken>())
            .Returns(new GravacaoRegraImpressao(false, 6));

        var resultado = await _useCase.ExecuteAsync(Comando(versao: 5, Pedido("a5")));

        resultado.Desfecho.Should().Be(DesfechoGravacaoImpressao.Conflito);
        resultado.Atual!.Versao.Should().Be(6);
    }

    [Fact]
    public async Task Corrida_perdida_para_o_mesmo_corpo_nao_e_conflito()
    {
        // Banca #2205: duplo clique ou retry em voo, dois PUTs iguais com o mesmo If-Match. O perdedor encontra o seu corpo gravado.
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>())
            .Returns(new RegraImpressaoSalva(CanonicoA4, 5, Agora.UtcDateTime),
                     new RegraImpressaoSalva(CanonicoA5, 6, Agora.UtcDateTime));
        _repo.GravarAsync(EmpresaId, LojaId, 5, CanonicoA5, Agora.UtcDateTime, Arg.Any<CancellationToken>())
            .Returns(new GravacaoRegraImpressao(false, 6));

        var resultado = await _useCase.ExecuteAsync(Comando(versao: 5, Pedido("a5")));

        resultado.Desfecho.Should().Be(DesfechoGravacaoImpressao.SemMudanca);
        resultado.Atual!.Versao.Should().Be(6);
    }

    [Fact]
    public async Task Corpo_invalido_e_recusado_com_o_campo()
    {
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns(new RegraImpressaoSalva(null, 0, null));

        var acao = () => _useCase.ExecuteAsync(Comando(versao: 0, Pedido("carta")));

        var erro = (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which;
        erro.Code.Should().Be("IMPRESSAO_INVALIDA");
        erro.Message.Should().Contain("documentos.pedido.papel");
        await NaoGravou();
    }

    [Fact]
    public async Task Template_da_empresa_que_nao_e_dela_e_recusado()
    {
        var idDeOutraEmpresa = Guid.NewGuid();
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns(new RegraImpressaoSalva(null, 0, null));
        _templates.GetEmpresaByIdAsync(EmpresaId, idDeOutraEmpresa).Returns((EtiquetaTemplate?)null);

        var acao = () => _useCase.ExecuteAsync(Comando(versao: 0, Pedido("a4", new TemplateImpressaoEntrada("Empresa", idDeOutraEmpresa))));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message
            .Should().Contain("documentos.pedido.template");
        await NaoGravou();
    }

    [Fact]
    public async Task Template_do_sistema_inexistente_e_recusado()
    {
        var id = Guid.NewGuid();
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns(new RegraImpressaoSalva(null, 0, null));
        _templates.GetSistemaByIdAsync(id).Returns((EtiquetaTemplateSistema?)null);

        var acao = () => _useCase.ExecuteAsync(Comando(versao: 0, Pedido("a4", new TemplateImpressaoEntrada("Sistema", id))));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await NaoGravou();
    }

    [Fact]
    public async Task Template_existente_da_empresa_e_aceito()
    {
        var id = Guid.NewGuid();
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns(new RegraImpressaoSalva(null, 0, null));
        _templates.GetEmpresaByIdAsync(EmpresaId, id).Returns(new EtiquetaTemplate { Id = id, EmpresaId = EmpresaId });
        _repo.GravarAsync(EmpresaId, LojaId, 0, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new GravacaoRegraImpressao(true, 1));

        var resultado = await _useCase.ExecuteAsync(Comando(versao: 0, Pedido("a4", new TemplateImpressaoEntrada("Empresa", id))));

        resultado.Desfecho.Should().Be(DesfechoGravacaoImpressao.Gravada);
    }

    private static GravarRegraImpressaoCommand Comando(int versao, RegraImpressaoLojaEntrada regra) =>
        new(EmpresaId, LojaId, versao, regra);

    private static RegraImpressaoLojaEntrada Pedido(string papel, TemplateImpressaoEntrada? template = null) =>
        new(1, new Dictionary<string, RegraImpressaoDocumentoEntrada?>
        {
            ["pedido"] = new(papel, template, null, null, null),
        });

    private Task NaoGravou() =>
        _repo.DidNotReceiveWithAnyArgs().GravarAsync(default, default, default, default!, default, default);
}
