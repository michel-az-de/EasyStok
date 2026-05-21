namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Normaliza <see cref="DateTime"/> para UTC antes de persistir.
///
/// <para>
/// O Postgres <c>timestamp with time zone</c> (timestamptz), via Npgsql, rejeita
/// valores com <see cref="DateTimeKind.Unspecified"/> lançando
/// "Cannot write DateTime with Kind=Unspecified". Datas que chegam do cliente
/// (JSON desserializado) vêm justamente com Kind=Unspecified — então TODO ponto
/// de fronteira command→entidade que grava uma data deve passar por aqui.
/// </para>
///
/// <para>
/// Centraliza o que antes era um helper privado duplicado em CriarPedido/
/// CriarContaPagar/CriarContaReceber — e que faltava nos caminhos de edição,
/// parcela e pagamento (mesma classe de bug de 500 ao salvar).
/// </para>
/// </summary>
public static class DataUtc
{
    /// <summary>Converte para UTC: Utc fica igual, Local converte, Unspecified é tratado como UTC.</summary>
    public static DateTime ParaUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };

    /// <summary>Versão para datas opcionais — null permanece null.</summary>
    public static DateTime? ParaUtcOpcional(DateTime? d) => d.HasValue ? ParaUtc(d.Value) : null;
}
