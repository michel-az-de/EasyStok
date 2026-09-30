using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Campanhas;

/// <summary>S28: ciclo da campanha (rascunho, agendada, enviando, encerrada, cancelada) e seus destinatários.</summary>
public class CampanhaTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid EmpresaId = Guid.NewGuid();

    private static DadosCampanha Dados(string mensagem = "Oi {{nome}}, saiu bolo de fubá!", int? tamanhoOnda = null,
        DateTime? encerramentoEm = null, bool lembrete = false) =>
        new("Bolo de fubá", mensagem, "https://cdn.exemplo/arte.png", "novidade_semana",
            FiltroCampanha.ParaTodos, ["Sem Glúten", "sem_gluten", "vegano"], encerramentoEm, lembrete, tamanhoOnda);

    private static Campanha Nova(DadosCampanha? dados = null) =>
        Campanha.Criar(EmpresaId, Guid.NewGuid(), dados ?? Dados(), Agora);

    [Fact]
    public void CriarNasceRascunhoComTagsDeRestricaoNormalizadas()
    {
        var campanha = Nova();

        campanha.Status.Should().Be(StatusCampanha.Rascunho);
        campanha.EmpresaId.Should().Be(EmpresaId);
        campanha.OndaAtual.Should().Be(0);
        campanha.CriadaEm.Should().Be(Agora);
        campanha.TagsRestricaoExcluidas.Should().Be("sem_gluten,vegano");
        campanha.Filtro.Todos.Should().BeTrue();
    }

    [Fact]
    public void AgendarValida()
    {
        // Disparo no passado ou agora.
        var passado = () => Nova().Agendar(Agora, Agora);
        passado.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*futuro*");

        // Mensagem vazia (rascunho aceita, agendar não).
        var semMensagem = () => Nova(Dados(mensagem: "  ")).Agendar(Agora.AddHours(1), Agora);
        semMensagem.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Mensagem*");

        // Tamanho de onda precisa ser positivo quando informado.
        var ondaZero = () => Nova(Dados(tamanhoOnda: 0)).Agendar(Agora.AddHours(1), Agora);
        ondaZero.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*onda*");

        // Encerramento antes do disparo e lembrete sem encerramento.
        var encerraAntes = () => Nova(Dados(encerramentoEm: Agora.AddMinutes(30))).Agendar(Agora.AddHours(1), Agora);
        encerraAntes.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*encerramento*");
        var lembreteSemFim = () => Nova(Dados(lembrete: true)).Agendar(Agora.AddHours(1), Agora);
        lembreteSemFim.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*lembrete*");

        // Filtro sem critério não vira "todos" por omissão.
        var semPublico = () => Nova(Dados() with { Filtro = FiltroCampanha.Vazio }).Agendar(Agora.AddHours(1), Agora);
        semPublico.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*público*");

        var campanha = Nova(Dados(tamanhoOnda: 50, encerramentoEm: Agora.AddDays(2), lembrete: true));
        campanha.Agendar(Agora.AddHours(1), Agora);

        campanha.Status.Should().Be(StatusCampanha.Agendada);
        campanha.DisparoEm.Should().Be(Agora.AddHours(1));
        campanha.TamanhoOnda.Should().Be(50);
    }

    [Fact]
    public void AtualizarAgendadaMantemAsRegrasDoAgendamento()
    {
        var campanha = Nova();
        campanha.Agendar(Agora.AddHours(1), Agora);

        var esvaziar = () => campanha.Atualizar(Dados(mensagem: ""));
        esvaziar.Should().Throw<RegraDeDominioVioladaException>();
        campanha.Mensagem.Should().NotBeEmpty("a atualização inválida não pode ter sido aplicada");

        campanha.Desagendar();
        campanha.Status.Should().Be(StatusCampanha.Rascunho);
        campanha.DisparoEm.Should().BeNull();
        campanha.Atualizar(Dados(mensagem: ""));
        campanha.Mensagem.Should().BeEmpty();
    }

    [Fact]
    public void IniciarOndaExigeAgendadaEHorarioDoDisparo()
    {
        var rascunho = Nova();
        var semAgenda = () => rascunho.IniciarOnda(Agora);
        semAgenda.Should().Throw<RegraDeDominioVioladaException>();

        var campanha = Nova();
        campanha.Agendar(Agora.AddHours(1), Agora);
        var cedo = () => campanha.IniciarOnda(Agora.AddMinutes(59));
        cedo.Should().Throw<RegraDeDominioVioladaException>();

        campanha.IniciarOnda(Agora.AddHours(1)).Should().Be(1);
        campanha.Status.Should().Be(StatusCampanha.Enviando);
        campanha.OndaAtual.Should().Be(1);

        var atualizarEnviando = () => campanha.Atualizar(Dados());
        atualizarEnviando.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void CancelarPreservaEnviados()
    {
        var campanha = Nova();
        campanha.Agendar(Agora.AddHours(1), Agora);
        campanha.IniciarOnda(Agora.AddHours(1));

        var pendente = CampanhaDestinatario.Criar(campanha, Guid.NewGuid());
        var enviado = CampanhaDestinatario.Criar(campanha, Guid.NewGuid());
        enviado.Enfileirar(1, Guid.NewGuid());
        enviado.MarcarEnviado(Agora.AddHours(1).AddMinutes(1));
        var excluidoAntes = CampanhaDestinatario.Criar(campanha, Guid.NewGuid());
        excluidoAntes.Excluir(MotivoExclusaoCampanha.LimiteSemanal);

        var excluidos = campanha.Cancelar([pendente, enviado, excluidoAntes]);

        excluidos.Should().Be(1);
        campanha.Status.Should().Be(StatusCampanha.Cancelada);
        pendente.Status.Should().Be(StatusCampanhaDestinatario.Excluido);
        pendente.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.Cancelada);
        enviado.Status.Should().Be(StatusCampanhaDestinatario.Enviado);
        enviado.EnviadoEm.Should().Be(Agora.AddHours(1).AddMinutes(1));
        enviado.MotivoExclusao.Should().BeNull();
        excluidoAntes.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.LimiteSemanal, "exclusão anterior é preservada");

        var deNovo = () => campanha.Cancelar([]);
        deNovo.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void CancelarRecusaDestinatarioDeOutraCampanha()
    {
        var campanha = Nova();
        var outra = Nova();
        var alheio = CampanhaDestinatario.Criar(outra, Guid.NewGuid());

        var cancelar = () => campanha.Cancelar([alheio]);

        cancelar.Should().Throw<RegraDeDominioVioladaException>();
        campanha.Status.Should().Be(StatusCampanha.Rascunho);
        alheio.Status.Should().Be(StatusCampanhaDestinatario.Pendente);
    }

    [Fact]
    public void EncerrarSoDepoisDeComecarOEnvio()
    {
        var campanha = Nova();
        var encerrarRascunho = () => campanha.Encerrar();
        encerrarRascunho.Should().Throw<RegraDeDominioVioladaException>();

        campanha.Agendar(Agora.AddHours(1), Agora);
        campanha.IniciarOnda(Agora.AddHours(2));
        campanha.Encerrar();

        campanha.Status.Should().Be(StatusCampanha.Encerrada);
        var cancelarEncerrada = () => campanha.Cancelar([]);
        cancelarEncerrada.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void DestinatarioSoExcluiComMotivoConhecidoEEnquantoPendente()
    {
        var campanha = Nova();
        var destinatario = CampanhaDestinatario.Criar(campanha, Guid.NewGuid());
        destinatario.EmpresaId.Should().Be(EmpresaId);
        destinatario.CampanhaId.Should().Be(campanha.Id);

        var motivoInventado = () => destinatario.Excluir("porque sim");
        motivoInventado.Should().Throw<RegraDeDominioVioladaException>();

        destinatario.Enfileirar(1, Guid.NewGuid());
        var excluirEnfileirado = () => destinatario.Excluir(MotivoExclusaoCampanha.Bloqueado);
        excluirEnfileirado.Should().Throw<RegraDeDominioVioladaException>();
        destinatario.Onda.Should().Be(1);
    }
}
