namespace EasyStock.Application.Demo;

/// <summary>
/// Entrada do planejamento de limpeza da loja-demo: todos os Ids do manifesto
/// (o que e demo) e o subconjunto desses Ids que tem referencia viva, isto e,
/// que alguma linha NAO-demo (criada pelo usuario) referencia.
/// </summary>
public sealed record DemoCleanupRequest(
    IReadOnlySet<Guid> IdsDoManifesto,
    IReadOnlySet<Guid> IdsComReferenciaViva);

/// <summary>Plano de limpeza: o que apagar e o que preservar.</summary>
public sealed record DemoCleanupPlan(
    IReadOnlyList<Guid> Apagar,
    IReadOnlyList<Guid> Preservar);

/// <summary>
/// Decide o que o "limpar" da loja-demo pode apagar, com a invariante de seguranca:
/// so apaga uma linha cujo Id pertence ao manifesto E que nenhuma linha viva (fora
/// do manifesto) referencia. Funcao pura, testavel sem banco. A DETECCAO de
/// referencia viva (joins de FK) e a EXECUCAO do DELETE em ordem topologica, numa
/// transacao SERIALIZABLE sob RLS, ficam na camada de Infra e sao provadas contra
/// Postgres. Aqui provamos a POLITICA: dado real (Id fora do manifesto, ou linha do
/// manifesto referenciada por algo vivo) nunca entra em "Apagar".
/// </summary>
public static class DemoCleanupPlanner
{
    public static DemoCleanupPlan Plan(DemoCleanupRequest request)
    {
        var apagar = new List<Guid>();
        var preservar = new List<Guid>();
        foreach (var id in request.IdsDoManifesto)
        {
            if (request.IdsComReferenciaViva.Contains(id))
                preservar.Add(id);
            else
                apagar.Add(id);
        }
        return new DemoCleanupPlan(apagar, preservar);
    }
}
