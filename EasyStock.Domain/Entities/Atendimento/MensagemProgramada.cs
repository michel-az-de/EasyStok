using System.Text.Json;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>Modelo aprovado (template da Meta) para envio fora da janela, com os parâmetros do corpo.</summary>
public sealed record ModeloMensagem(string Nome, string Idioma, IReadOnlyList<string> Parametros);

/// <summary>
/// Mensagem ao cliente agendada pela dona (S39, ADR-0051), em qualquer canal. As regras do canal são
/// conferidas ao agendar e de novo no disparo (<see cref="GarantirPodeSairPor"/>), porque a janela
/// de 24 h pode vencer entre um e outro. O consentimento (S38) é conferido pela Application, que tem
/// acesso ao cadastro.
/// </summary>
public class MensagemProgramada
{
    public const int TextoTamanhoMaximo = 4096;
    public const int ErroTamanhoMaximo = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ClienteId { get; private set; }

    /// <summary>Conversa de onde saiu, quando há. Instagram, Messenger e chat do site exigem uma.</summary>
    public Guid? ConversaId { get; private set; }

    public CanalConversa Canal { get; private set; }
    public FinalidadeContato Finalidade { get; private set; }
    public string? Texto { get; private set; }
    public string? ModeloNome { get; private set; }
    public string? ModeloIdioma { get; private set; }
    public string ModeloParametrosJson { get; private set; } = "[]";
    public DateTime AgendadaPara { get; private set; }
    public SituacaoMensagemProgramada Situacao { get; private set; }
    public int Tentativas { get; private set; }
    public string? IdExterno { get; private set; }
    public string? Erro { get; private set; }
    public Guid CriadaPorUsuarioId { get; private set; }
    public DateTime CriadaEm { get; private set; }
    public DateTime? EnviadaEm { get; private set; }
    public DateTime AlteradaEm { get; private set; }

    public ModeloMensagem? Modelo => ModeloNome is null
        ? null
        : new ModeloMensagem(ModeloNome, ModeloIdioma ?? "pt_BR",
            JsonSerializer.Deserialize<List<string>>(ModeloParametrosJson, JsonOptions) ?? []);

    // EF Core ctor sem parâmetros
    private MensagemProgramada() { }

    public static MensagemProgramada Agendar(
        Guid empresaId, Guid clienteId, Guid? conversaId, CanalConversa canal, FinalidadeContato finalidade,
        string? texto, ModeloMensagem? modelo, DateTime agendadaPara, Guid criadaPorUsuarioId, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (clienteId == Guid.Empty) throw new RegraDeDominioVioladaException("ClienteId é obrigatório.");
        if (!Enum.IsDefined(finalidade)) throw new RegraDeDominioVioladaException($"Finalidade inválida: {(int)finalidade}.");

        var capacidades = CapacidadesCanal.Para(canal);
        var temTexto = !string.IsNullOrWhiteSpace(texto);
        if (temTexto == (modelo is not null))
            throw new RegraDeDominioVioladaException("Informe o texto ou o modelo aprovado, um dos dois.");
        if (modelo is not null && !capacidades.AceitaModelo)
            throw new RegraDeDominioVioladaException($"O canal {canal} não tem modelo aprovado: envie texto.");
        if (modelo is not null && string.IsNullOrWhiteSpace(modelo.Nome))
            throw new RegraDeDominioVioladaException("Nome do modelo é obrigatório.");
        if (temTexto && texto!.Trim().Length > TextoTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Texto acima de {TextoTamanhoMaximo} caracteres.");

        var para = Utc(agendadaPara);
        if (para <= Utc(agora))
            throw new RegraDeDominioVioladaException("Horário no passado: agende para depois de agora.");

        return new MensagemProgramada
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ClienteId = clienteId,
            ConversaId = conversaId,
            Canal = canal,
            Finalidade = finalidade,
            Texto = temTexto ? texto!.Trim() : null,
            ModeloNome = modelo?.Nome.Trim(),
            ModeloIdioma = modelo?.Idioma,
            ModeloParametrosJson = JsonSerializer.Serialize(modelo?.Parametros ?? [], JsonOptions),
            AgendadaPara = para,
            Situacao = SituacaoMensagemProgramada.Agendada,
            CriadaPorUsuarioId = criadaPorUsuarioId,
            CriadaEm = Utc(agora),
            AlteradaEm = Utc(agora),
        };
    }

    /// <summary>
    /// Regra da janela do canal no <paramref name="instante"/> do envio. Canal sem janela (e-mail,
    /// SMS) sempre passa. Com janela: modelo aprovado passa; texto só com a conversa dentro da
    /// janela. A tag de agente humano (Instagram, Messenger) não vale para envio agendado.
    /// </summary>
    public void GarantirPodeSairPor(Conversa? conversa, DateTime instante)
    {
        var capacidades = CapacidadesCanal.Para(Canal);
        if (!capacidades.TemJanela) return;
        if (ModeloNome is not null) return;
        if (conversa is not null && conversa.Canal == Canal && conversa.DentroDaJanela(instante)) return;

        throw new RegraDeDominioVioladaException(capacidades.AceitaModelo
            ? "Fora da janela de atendimento no horário do envio: use um modelo aprovado."
            : $"Fora da janela de atendimento no horário do envio: o {Canal} não aceita mensagem agendada fora dela.");
    }

    /// <summary>Um disparador pega a mensagem para enviar. Cancelada ou já enviada não sai.</summary>
    public void Reservar(DateTime agora)
    {
        if (Situacao != SituacaoMensagemProgramada.Agendada)
            throw new RegraDeDominioVioladaException($"Só mensagem agendada pode ser enviada (situação: {Situacao}).");
        Situacao = SituacaoMensagemProgramada.Enviando;
        Tentativas++;
        AlteradaEm = Utc(agora);
    }

    public void RegistrarEnvio(string? idExterno, DateTime em)
    {
        if (Situacao != SituacaoMensagemProgramada.Enviando)
            throw new RegraDeDominioVioladaException($"Envio registrado fora de hora (situação: {Situacao}).");
        Situacao = SituacaoMensagemProgramada.Enviada;
        IdExterno = idExterno;
        EnviadaEm = Utc(em);
        Erro = null;
        AlteradaEm = Utc(em);
    }

    public void RegistrarFalha(string motivo, DateTime em)
    {
        Situacao = SituacaoMensagemProgramada.Falhou;
        var limpo = string.IsNullOrWhiteSpace(motivo) ? "falha sem detalhe" : motivo.Trim();
        Erro = limpo.Length > ErroTamanhoMaximo ? limpo[..ErroTamanhoMaximo] : limpo;
        AlteradaEm = Utc(em);
    }

    public void Cancelar(DateTime em)
    {
        if (Situacao != SituacaoMensagemProgramada.Agendada)
            throw new RegraDeDominioVioladaException($"Só mensagem agendada pode ser cancelada (situação: {Situacao}).");
        Situacao = SituacaoMensagemProgramada.Cancelada;
        AlteradaEm = Utc(em);
    }

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
