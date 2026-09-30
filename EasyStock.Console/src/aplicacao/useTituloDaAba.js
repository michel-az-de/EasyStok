import { useEffect } from 'react'

// Rodada 10, item "falta construir" 8: pedido literal do dono ("não tem
// notificações de sistema") e US-010 (saber sem olhar), agora para quando
// ela trocou de aba/janela NO MESMO computador (tela apagada é limite
// conhecido, `auditoria/decisoes/27-som.md`). Montado só pelo App.jsx, na
// janela do Balcão: Cozinha e Entregas têm o próprio título fixo.
const TITULO_BASE = 'Casa da Baba'

export function useTituloDaAba(contagemPrecisaDeVoce) {
  useEffect(() => {
    document.title = contagemPrecisaDeVoce > 0
      ? `(${contagemPrecisaDeVoce}) ${TITULO_BASE}`
      : TITULO_BASE
  }, [contagemPrecisaDeVoce])
}
