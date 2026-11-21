// #2377: no Chrome o `print()` segura o JS até o diálogo fechar; em `--kiosk-printing` volta quase na hora.
// Demorou: houve diálogo humano e ninguém sabe se a folha saiu (cancelou, sem papel). Não confirma sozinho.
export const LIMITE_DIALOGO_MS = 1500
export const destinoDoPrint = (ms) => (ms >= LIMITE_DIALOGO_MS ? 'dialogo-lento' : 'dialogo')
export function medirPrint(imprimir, agora = () => performance.now()) {
  const inicio = agora()
  imprimir()
  return destinoDoPrint(agora() - inicio)
}
