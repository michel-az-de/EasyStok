import { useEffect } from 'react'

// Rodada 10, item "falta construir" 8: pedido literal do dono ("não tem
// notificações de sistema") e US-010 (saber sem olhar), agora para quando
// ela trocou de aba/janela NO MESMO computador (tela apagada é limite
// conhecido, `auditoria/decisoes/27-som.md`).
//
// #1582: o texto vem pronto de `dominio/tituloDaAba.js`. O Balcão escreve o dele, com a
// contagem; as outras telas recebem o nome pela rota, no `App`. Título nulo não escreve:
// é como o `App` deixa o Balcão ser o único a escrever enquanto está na tela.
export function useTituloDaAba(titulo) {
  useEffect(() => {
    if (titulo) document.title = titulo
  }, [titulo])
}
