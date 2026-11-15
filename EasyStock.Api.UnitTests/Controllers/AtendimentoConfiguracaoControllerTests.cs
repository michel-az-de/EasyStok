using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class AtendimentoConfiguracaoControllerTests
{
    [Fact]
    public async Task PutRecusaConfiguracaoDeOutraEmpresaMesmoSeRepositorioDevolver()
    {
        var alheia = ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid());
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(alheia);
        var resultado = await _controller.Put(new AtualizarConfiguracaoAtendimentoBody(
            null, null, null, null, null, null, null, null, null, SlaRespostaMinutos: 20));
        resultado.Should().BeOfType<BadRequestObjectResult>();
        alheia.SlaRespostaMinutos.Should().Be(5);
        await _unitOfWork.DidNotReceive().CommitAsync();
    }
    private readonly IConfiguracaoAtendimentoRepository _repository = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly AtendimentoConfiguracaoController _controller;
    private readonly Guid _empresaId = Guid.NewGuid();

    public AtendimentoConfiguracaoControllerTests()
    {
        var obterUseCase = new ObterConfiguracaoAtendimentoUseCase(_repository);
        var aprovados = new ModelosAprovadosAgenteOptions();
        aprovados.Modelos.Add(new ModeloAprovadoAgente("claude-sonnet-5", "Padrão", "padrão"));
        aprovados.Modelos.Add(new ModeloAprovadoAgente("claude-haiku-5", "Econômico", "econômico"));
        var atualizarUseCase = new AtualizarConfiguracaoAtendimentoUseCase(_repository, _unitOfWork,
            modelos: Microsoft.Extensions.Options.Options.Create(aprovados));
        _currentUser.EmpresaId.Returns(_empresaId);
        _controller = new AtendimentoConfiguracaoController(obterUseCase, atualizarUseCase,
            new DefinirModeloRetomadaUseCase(_repository, _unitOfWork), _currentUser);
    }

    // S68 (#2597, ADR-0066): o modelo da loja só vale dentro da lista aprovada.
    [Fact]
    public async Task PutComModeloForaDaListaAprovada400ENaoGrava()
    {
        var config = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(config);

        var resultado = await _controller.Put(Corpo(ModeloAgente: "claude-opus-5"));

        resultado.Should().BeOfType<BadRequestObjectResult>();
        config.ModeloAgente.Should().BeNull();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    // Revisão da #2618: a recusa do modelo vem antes de qualquer mudança na entidade rastreada.
    [Fact]
    public async Task PutComModeloForaDaListaNaoMudaNadaNaEntidade()
    {
        var config = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        var tomAntes = config.Tom;
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(config);

        var resultado = await _controller.Put(new AtualizarConfiguracaoAtendimentoBody(
            "Tom novo", null, null, null, null, null, null, null, null, ModeloAgente: "claude-opus-5"));

        resultado.Should().BeOfType<BadRequestObjectResult>();
        config.Tom.Should().Be(tomAntes);
    }

    // Revisão da #2618: o modelo já gravado que saiu da lista volta igual no PUT sem travar os outros campos.
    [Fact]
    public async Task PutComOModeloJaGravadoQueSaiuDaListaGravaOResto()
    {
        var config = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        config.DefinirAssistente(null, "modelo-retirado", null, null, null);
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(config);

        var resultado = await _controller.Put(Corpo(NomeDona: "Michele", ModeloAgente: "modelo-retirado"));

        resultado.Should().BeOfType<OkObjectResult>();
        config.NomeDona.Should().Be("Michele");
        config.ModeloAgente.Should().Be("modelo-retirado");
    }

    [Fact]
    public async Task PutGravaAConfiguracaoDoAssistenteEOGetDevolve()
    {
        var config = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(config);
        _repository.GetOrDefaultAsync(_empresaId).Returns(config);

        var put = await _controller.Put(Corpo(NomeDona: "Michele", ModeloAgente: "claude-haiku-5", CotaTokensMes: 2_000_000,
            Roteiro: [new EtapaRoteiroAtendimento("Saudar", "Cumprimente pelo nome.")], ObservacoesLoja: "Sem glúten só por encomenda."));

        put.Should().BeOfType<OkObjectResult>();
        await _unitOfWork.Received(1).CommitAsync();
        var dados = DadosDe(await _controller.Get());
        dados.NomeDona.Should().Be("Michele");
        dados.ModeloAgente.Should().Be("claude-haiku-5");
        dados.CotaTokensMes.Should().Be(2_000_000);
        dados.RoteiroPersonalizado.Should().BeTrue();
        dados.Roteiro.Should().Equal(new EtapaRoteiroAtendimento("Saudar", "Cumprimente pelo nome."));
        dados.ObservacoesLoja.Should().Be("Sem glúten só por encomenda.");
    }

    [Fact]
    public async Task GetSemRoteiroDaLojaDevolveOPadrao()
    {
        _repository.GetOrDefaultAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));

        var dados = DadosDe(await _controller.Get());

        dados.RoteiroPersonalizado.Should().BeFalse();
        dados.Roteiro.Should().Equal(RoteiroAtendimento.Padrao);
        dados.NomeDona.Should().BeNull();
    }

    [Fact]
    public async Task PutComRoteiroVazioVoltaAoPadrao()
    {
        var config = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        config.DefinirAssistente(null, null, null, RoteiroAtendimento.Serializar([new("Saudar", "Oi.")]), null);
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(config);

        (await _controller.Put(Corpo(Roteiro: []))).Should().BeOfType<OkObjectResult>();

        config.RoteiroJson.Should().BeNull();
    }

    [Theory]
    [InlineData("", "Cumprimente.")]
    [InlineData("Saudar", "")]
    public async Task PutComEtapaSemNomeOuInstrucao400(string nome, string instrucao)
    {
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));

        (await _controller.Put(Corpo(Roteiro: [new EtapaRoteiroAtendimento(nome, instrucao)])))
            .Should().BeOfType<BadRequestObjectResult>();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    private static AtualizarConfiguracaoAtendimentoBody Corpo(string? NomeDona = null, string? ModeloAgente = null,
        long? CotaTokensMes = null, IReadOnlyList<EtapaRoteiroAtendimento>? Roteiro = null, string? ObservacoesLoja = null) =>
        new(null, null, null, null, null, null, null, null, null, NomeDona: NomeDona, ModeloAgente: ModeloAgente,
            CotaTokensMes: CotaTokensMes, Roteiro: Roteiro, ObservacoesLoja: ObservacoesLoja);

    private static ConfiguracaoAtendimentoResult DadosDe(IActionResult resultado) =>
        resultado.Should().BeOfType<OkObjectResult>().Subject.Value.Should()
            .BeOfType<EasyStock.Api.Http.ApiResponse<ConfiguracaoAtendimentoResult>>().Subject.Data!;

    [Fact]
    public async Task DefineOModeloDeRetomada()
    {
        var config = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(config);

        var result = await _controller.PutModeloRetomada(new ModeloRetomadaBody("retomar_conversa", null));

        result.Should().BeOfType<OkObjectResult>();
        config.ModeloRetomada.Should().Be(new ModeloRetomada("retomar_conversa", "pt_BR"));
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ModeloForaDoFormatoDaMeta400()
    {
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));

        var result = await _controller.PutModeloRetomada(new ModeloRetomadaBody("Retomar Conversa", null));

        result.Should().BeOfType<BadRequestObjectResult>();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task GetSemRegistroDevolvePadrao()
    {
        _repository.GetOrDefaultAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));

        var result = await _controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var data = (ConfiguracaoAtendimentoResult)ok.Value!.GetType().GetProperty("Data")!.GetValue(ok.Value)!;
        data.RespiroMinutos.Should().Be(40);
        data.TempoPreparoPadraoMinutos.Should().Be(60);
        data.SlaRespostaMinutos.Should().Be(5);
    }

    [Fact]
    public async Task PutValida()
    {
        _repository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);

        var body = new AtualizarConfiguracaoAtendimentoBody(
            "direto e simpático", NivelSugestaoAtendimento.Ativo,
            null, null, null, null, RespiroMinutos: 20, TempoPreparoPadraoMinutos: 90, Ativo: null);

        var result = await _controller.Put(body);

        result.Should().BeOfType<OkObjectResult>();
        await _repository.Received(1).AddAsync(Arg.Is<ConfiguracaoAtendimento>(c =>
            c.EmpresaId == _empresaId && c.Tom == "direto e simpático" &&
            c.NivelSugestao == NivelSugestaoAtendimento.Ativo &&
            c.RespiroMinutos == 20 && c.TempoPreparoPadraoMinutos == 90));
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task PutComRespiroNegativoDevolveBadRequest()
    {
        _repository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);

        var body = new AtualizarConfiguracaoAtendimentoBody(
            null, null, null, null, null, null, RespiroMinutos: -1, TempoPreparoPadraoMinutos: null, Ativo: null);

        var result = await _controller.Put(body);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task PutComTempoPreparoZeroDevolveBadRequest()
    {
        _repository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);

        var body = new AtualizarConfiguracaoAtendimentoBody(
            null, null, null, null, null, null, RespiroMinutos: null, TempoPreparoPadraoMinutos: 0, Ativo: null);

        var result = await _controller.Put(body);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task PutGravaOSlaDeResposta()
    {
        var config = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(config);

        var body = new AtualizarConfiguracaoAtendimentoBody(
            null, null, null, null, null, null, null, null, null, SlaRespostaMinutos: 15);

        var result = await _controller.Put(body);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var data = (ConfiguracaoAtendimentoResult)ok.Value!.GetType().GetProperty("Data")!.GetValue(ok.Value)!;
        data.SlaRespostaMinutos.Should().Be(15);
        config.SlaRespostaMinutos.Should().Be(15);
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public async Task PutComSlaForaDaFaixaDevolveBadRequest(int minutos)
    {
        _repository.GetByEmpresaIdAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));

        var body = new AtualizarConfiguracaoAtendimentoBody(
            null, null, null, null, null, null, null, null, null, SlaRespostaMinutos: minutos);

        var result = await _controller.Put(body);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }
}
