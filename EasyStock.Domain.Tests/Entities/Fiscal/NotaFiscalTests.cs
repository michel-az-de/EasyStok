using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.Exceptions.Fiscal;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.ValueObjects.Fiscal;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Fiscal;

public class NotaFiscalTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LojaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PedidoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UsuarioId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static ChaveAcessoNFe ChaveValida() =>
        ChaveAcessoNFe.Construir("35", new DateTime(2026, 5, 8), "12345678000190",
            ModeloDocumentoFiscal.NFCe, 1, 1, TipoEmissao.Normal, "00000001");

    private static NotaFiscal NovaNotaEmEmissao(DateTime? dataEmissao = null) =>
        NotaFiscal.CriarParaEmissao(
            empresaId: EmpresaId,
            lojaId: LojaId,
            pedidoId: PedidoId,
            modelo: ModeloDocumentoFiscal.NFCe,
            serie: 1,
            numero: 1,
            chaveAcesso: ChaveValida(),
            tipoEmissao: TipoEmissao.Normal,
            ambiente: AmbienteSefaz.Homologacao,
            dataEmissao: dataEmissao ?? DateTime.UtcNow,
            valorTotal: Dinheiro.FromDecimal(49.90m),
            clienteCpfCnpj: null,
            idempotencyKey: $"{EmpresaId:N}:{LojaId:N}:{PedidoId:N}",
            origem: "test",
            criadoPorUsuarioId: UsuarioId);

    [Fact]
    public void CriarParaEmissao_inicializa_status_EmEmissao()
    {
        var nota = NovaNotaEmEmissao();

        nota.Status.Should().Be(StatusNotaFiscal.EmEmissao);
        nota.Modelo.Should().Be(ModeloDocumentoFiscal.NFCe);
        nota.TipoEmissao.Should().Be(TipoEmissao.Normal);
        nota.Numero.Should().Be(1);
        nota.IdempotencyKey.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CriarParaEmissao_com_empresaId_vazio_lanca_exception()
    {
        var act = () => NotaFiscal.CriarParaEmissao(
            empresaId: Guid.Empty, lojaId: LojaId, pedidoId: PedidoId,
            modelo: ModeloDocumentoFiscal.NFCe, serie: 1, numero: 1,
            chaveAcesso: ChaveValida(),
            tipoEmissao: TipoEmissao.Normal, ambiente: AmbienteSefaz.Homologacao,
            dataEmissao: DateTime.UtcNow,
            valorTotal: Dinheiro.FromDecimal(10), clienteCpfCnpj: null,
            idempotencyKey: "k", origem: "test", criadoPorUsuarioId: UsuarioId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CriarParaEmissao_com_idempotency_vazio_lanca_exception()
    {
        var act = () => NotaFiscal.CriarParaEmissao(
            empresaId: EmpresaId, lojaId: LojaId, pedidoId: PedidoId,
            modelo: ModeloDocumentoFiscal.NFCe, serie: 1, numero: 1,
            chaveAcesso: ChaveValida(),
            tipoEmissao: TipoEmissao.Normal, ambiente: AmbienteSefaz.Homologacao,
            dataEmissao: DateTime.UtcNow,
            valorTotal: Dinheiro.FromDecimal(10), clienteCpfCnpj: null,
            idempotencyKey: "", origem: "test", criadoPorUsuarioId: UsuarioId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CriarParaEmissao_com_numero_zero_lanca_exception()
    {
        var act = () => NotaFiscal.CriarParaEmissao(
            empresaId: EmpresaId, lojaId: LojaId, pedidoId: PedidoId,
            modelo: ModeloDocumentoFiscal.NFCe, serie: 1, numero: 0,
            chaveAcesso: ChaveValida(),
            tipoEmissao: TipoEmissao.Normal, ambiente: AmbienteSefaz.Homologacao,
            dataEmissao: DateTime.UtcNow,
            valorTotal: Dinheiro.FromDecimal(10), clienteCpfCnpj: null,
            idempotencyKey: "k", origem: "test", criadoPorUsuarioId: UsuarioId);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarcarAutorizada_de_emEmissao_aceita()
    {
        var nota = NovaNotaEmEmissao();
        var dh = DateTime.UtcNow;

        nota.MarcarAutorizada("PROT123", "<nfe>...</nfe>", dh);

        nota.Status.Should().Be(StatusNotaFiscal.Autorizada);
        nota.ProtocoloAutorizacao.Should().Be("PROT123");
        nota.XmlAutorizado.Should().Be("<nfe>...</nfe>");
        nota.DataAutorizacao.Should().Be(dh);
        nota.Eventos.Should().ContainSingle(e => e.Tipo == "Autorizada");
    }

    [Fact]
    public void MarcarAutorizada_sem_protocolo_lanca_exception()
    {
        var nota = NovaNotaEmEmissao();
        var act = () => nota.MarcarAutorizada("", "<xml/>", DateTime.UtcNow);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarcarAutorizada_sem_xml_lanca_exception()
    {
        var nota = NovaNotaEmEmissao();
        var act = () => nota.MarcarAutorizada("PROT", "", DateTime.UtcNow);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarcarEmContingencia_seta_tpEmis_9()
    {
        var nota = NovaNotaEmEmissao();

        nota.MarcarEmContingencia("<xml>local</xml>", "Focus indisponivel");

        nota.Status.Should().Be(StatusNotaFiscal.EmContingencia);
        nota.TipoEmissao.Should().Be(TipoEmissao.OfflineNFCe);
        nota.XmlAssinadoLocal.Should().Be("<xml>local</xml>");
    }

    [Fact]
    public void MarcarEmContingencia_de_autorizada_lanca_TransicaoInvalida()
    {
        var nota = NovaNotaEmEmissao();
        nota.MarcarAutorizada("PROT", "<xml/>", DateTime.UtcNow);

        var act = () => nota.MarcarEmContingencia("<xml/>", "qualquer");
        act.Should().Throw<TransicaoNotaFiscalInvalidaException>();
    }

    [Fact]
    public void MarcarAutorizadaPosContingencia_aceita_de_emContingencia()
    {
        var nota = NovaNotaEmEmissao();
        nota.MarcarEmContingencia("<xml/>", "timeout");

        nota.MarcarAutorizadaPosContingencia("PROT", "<xmlauth/>", DateTime.UtcNow);

        nota.Status.Should().Be(StatusNotaFiscal.Autorizada);
        nota.Eventos.Should().Contain(e => e.Tipo == "AutorizadaAposContingencia");
    }

    [Fact]
    public void IniciarCancelamento_de_autorizada_dentro_do_prazo_aceita()
    {
        var nota = NovaNotaEmEmissao();
        var dh = DateTime.UtcNow.AddMinutes(-5);
        nota.MarcarAutorizada("PROT", "<xml/>", dh);

        nota.IniciarCancelamento(
            justificativa: "Erro do operador no caixa.",
            usuarioId: UsuarioId,
            now: DateTime.UtcNow);

        nota.Status.Should().Be(StatusNotaFiscal.CancelamentoEmAndamento);
        nota.JustificativaCancelamento.Should().Be("Erro do operador no caixa.");
    }

    [Fact]
    public void IniciarCancelamento_apos_30min_lanca_PrazoCancelamentoExpirado()
    {
        var nota = NovaNotaEmEmissao();
        var dhAuth = DateTime.UtcNow.AddMinutes(-31);
        nota.MarcarAutorizada("PROT", "<xml/>", dhAuth);

        var act = () => nota.IniciarCancelamento(
            "Erro do operador no caixa.", UsuarioId, DateTime.UtcNow);

        act.Should().Throw<PrazoCancelamentoExpiradoException>();
    }

    [Fact]
    public void IniciarCancelamento_com_justificativa_curta_lanca_exception()
    {
        var nota = NovaNotaEmEmissao();
        nota.MarcarAutorizada("PROT", "<xml/>", DateTime.UtcNow);

        var act = () => nota.IniciarCancelamento("curto", UsuarioId, DateTime.UtcNow);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IniciarCancelamento_de_emEmissao_lanca_TransicaoInvalida()
    {
        var nota = NovaNotaEmEmissao();

        var act = () => nota.IniciarCancelamento(
            "Justificativa qualquer válida.", UsuarioId, DateTime.UtcNow);

        act.Should().Throw<TransicaoNotaFiscalInvalidaException>();
    }

    [Fact]
    public void MarcarCancelada_de_cancelamentoEmAndamento_aceita()
    {
        var nota = NovaNotaEmEmissao();
        nota.MarcarAutorizada("PROT", "<xml/>", DateTime.UtcNow.AddMinutes(-2));
        nota.IniciarCancelamento("Erro do operador no caixa.", UsuarioId, DateTime.UtcNow);

        nota.MarcarCancelada("PROTCANC", "<canc/>", DateTime.UtcNow);

        nota.Status.Should().Be(StatusNotaFiscal.Cancelada);
        nota.ProtocoloCancelamento.Should().Be("PROTCANC");
        nota.XmlEventoCancelamento.Should().Be("<canc/>");
    }

    [Fact]
    public void ReverterCancelamento_volta_para_autorizada_quando_cancelamento_em_andamento()
    {
        var nota = NovaNotaEmEmissao();
        nota.MarcarAutorizada("PROT", "<xml/>", DateTime.UtcNow.AddMinutes(-2));
        nota.IniciarCancelamento("Erro do operador no caixa.", UsuarioId, DateTime.UtcNow);

        nota.ReverterCancelamento("Focus rejeitou cancelamento");

        nota.Status.Should().Be(StatusNotaFiscal.Autorizada);
        nota.Eventos.Should().Contain(e => e.Tipo == "CancelamentoFalhou");
    }

    [Fact]
    public void DentroDoPrazoCancelamento_falso_quando_passou_de_30min()
    {
        var nota = NovaNotaEmEmissao();
        var dhAuth = DateTime.UtcNow.AddMinutes(-31);
        nota.MarcarAutorizada("PROT", "<xml/>", dhAuth);

        nota.DentroDoPrazoCancelamento(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void DentroDoPrazoCancelamento_verdadeiro_quando_dentro_de_30min()
    {
        var nota = NovaNotaEmEmissao();
        var dhAuth = DateTime.UtcNow.AddMinutes(-10);
        nota.MarcarAutorizada("PROT", "<xml/>", dhAuth);

        nota.DentroDoPrazoCancelamento(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void AdicionarItem_em_emEmissao_aceita()
    {
        var nota = NovaNotaEmEmissao();
        var item = NotaFiscalItem.Criar(
            notaFiscalId: nota.Id,
            empresaId: EmpresaId,
            ordem: 1,
            produtoId: null,
            descricaoSnapshot: "Produto teste",
            codigoProduto: "P-001",
            ean: null,
            ncm: NCM.Parse("19059020"),
            cfop: CFOP.VendaIntraEstado(),
            cest: null,
            unidadeComercial: "UN",
            quantidade: 1m,
            precoUnitario: 49.90m,
            desconto: 0m,
            origem: OrigemMercadoria.Nacional,
            cstCsosn: CSTouCSOSN.ParaSimples("102"),
            cstPis: "07",
            cstCofins: "07");

        nota.AdicionarItem(item);

        nota.Itens.Should().ContainSingle();
    }

    [Fact]
    public void AdicionarItem_apos_autorizada_lanca_TransicaoInvalida()
    {
        var nota = NovaNotaEmEmissao();
        nota.MarcarAutorizada("PROT", "<xml/>", DateTime.UtcNow);

        var item = NotaFiscalItem.Criar(
            nota.Id, EmpresaId, 1, null, "P", "C", null,
            NCM.Parse("19059020"), CFOP.VendaIntraEstado(), null,
            "UN", 1m, 10m, 0m,
            OrigemMercadoria.Nacional, CSTouCSOSN.ParaSimples("102"), "07", "07");

        var act = () => nota.AdicionarItem(item);
        act.Should().Throw<TransicaoNotaFiscalInvalidaException>();
    }

    [Fact]
    public void Arquivar_marca_arquivado_e_e_idempotente()
    {
        var nota = NovaNotaEmEmissao();

        nota.Arquivar();
        nota.Arquivado.Should().BeTrue();

        var alteradoEm = nota.AlteradoEm;
        nota.Arquivar();
        nota.AlteradoEm.Should().Be(alteradoEm);
    }
}
