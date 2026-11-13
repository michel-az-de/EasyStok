// Geração da leitura (#1474): cada leitura (ou troca de dia) abre uma geração nova; a resposta que chega de
// uma geração velha (Hoje respondendo depois de Amanhã) é descartada em vez de sobrescrever.
export function criarGeracao() {
  let atual = 0
  return {
    nova: () => { atual += 1; return atual },
    agora: () => atual,
    vale: (geracao) => geracao === atual,
  }
}
