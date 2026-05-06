using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Domain.Entities.Notifications;

public class VariavelTemplateCatalogo
{
    public Guid Id { get; set; }
    public TipoEventoNotificacao TipoEvento { get; set; }
    public string NomeVariavel { get; set; } = null!;
    public string Tipo { get; set; } = "string";
    public string Descricao { get; set; } = string.Empty;
    public string Exemplo { get; set; } = string.Empty;

    public static VariavelTemplateCatalogo Criar(
        TipoEventoNotificacao tipoEvento,
        string nomeVariavel,
        string tipo,
        string descricao,
        string exemplo) => new()
    {
        Id = Guid.NewGuid(),
        TipoEvento = tipoEvento,
        NomeVariavel = nomeVariavel,
        Tipo = tipo,
        Descricao = descricao,
        Exemplo = exemplo
    };
}
