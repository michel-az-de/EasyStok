using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// O que um canal de atendimento suporta (S34, ADR-0051). Espelha a tabela de canais do protótipo
/// do console (<c>infra/catalogo.js</c>): canal não declarado não envia.
/// </summary>
public sealed record CapacidadesCanal(
    CanalConversa Canal,
    int? HorasJanela,
    bool AceitaModelo,
    IReadOnlyList<string> TagsForaDaJanela,
    int? DiasMaximosComTag,
    bool AceitaImagem,
    bool AceitaAudio,
    bool AceitaDocumento,
    bool AceitaBotoes)
{
    public bool TemJanela => HorasJanela is not null;

    /// <summary>Tag da Meta para resposta humana fora da janela no Messenger e no Instagram.</summary>
    public const string TagAgenteHumano = "HUMAN_AGENT";

    private static readonly string[] SemTags = [];
    private static readonly string[] SoAgenteHumano = [TagAgenteHumano];

    /// <summary>
    /// Fora da janela: o WhatsApp exige modelo aprovado; Messenger e Instagram aceitam só
    /// <see cref="TagAgenteHumano"/> até 7 dias (política da Meta, conferir na S35). Chat do site,
    /// e-mail e SMS não têm janela.
    /// </summary>
    public static CapacidadesCanal Para(CanalConversa canal) => canal switch
    {
        CanalConversa.WhatsApp => new(canal, 24, AceitaModelo: true, SemTags, null,
            AceitaImagem: true, AceitaAudio: true, AceitaDocumento: true, AceitaBotoes: true),
        CanalConversa.Instagram => new(canal, 24, AceitaModelo: false, SoAgenteHumano, 7,
            AceitaImagem: true, AceitaAudio: true, AceitaDocumento: false, AceitaBotoes: false),
        CanalConversa.Messenger => new(canal, 24, AceitaModelo: false, SoAgenteHumano, 7,
            AceitaImagem: true, AceitaAudio: true, AceitaDocumento: true, AceitaBotoes: true),
        CanalConversa.ChatSite => new(canal, null, AceitaModelo: false, SemTags, null,
            AceitaImagem: false, AceitaAudio: false, AceitaDocumento: false, AceitaBotoes: false),
        CanalConversa.Email => new(canal, null, AceitaModelo: false, SemTags, null,
            AceitaImagem: true, AceitaAudio: false, AceitaDocumento: true, AceitaBotoes: false),
        CanalConversa.Sms => new(canal, null, AceitaModelo: false, SemTags, null,
            AceitaImagem: false, AceitaAudio: false, AceitaDocumento: false, AceitaBotoes: false),
        _ => throw new RegraDeDominioVioladaException($"Canal nao declarado: {(int)canal}."),
    };
}
