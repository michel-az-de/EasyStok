import { useCallback, useSyncExternalStore } from 'react'
import { EVENTO_SESSAO_ALTERADA, lerSessao } from '../infra/api/sessao'

// #2134: chave para ligar e desligar o completar texto com Tab (sugestão de continuação por IA,
// #1773). Cada sugestão manda o que a atendente digitou, e a conversa, ao provedor de IA; quem
// não quer isso desliga. Preferência de quem usa o navegador: uma chave por empresa e usuário,
// no localStorage, sem passar pelo servidor. Padrão ligado, como era antes da chave.
// Leitura e escrita em try/catch (modo privado, cota cheia): sem armazenamento a escolha vale
// só até recarregar, e a tela segue de pé. Mesmo desenho de `preferenciasHall.js`.
const daVisita = new Map()

export function chaveDoComplemento(sessao) {
  const empresa = sessao?.empresa?.id
  const usuario = sessao?.usuario?.id
  if (typeof empresa !== 'string' || !empresa || typeof usuario !== 'string' || !usuario) return null
  return `easystok.complementoTab.v1:${encodeURIComponent(empresa)}:${encodeURIComponent(usuario)}`
}

export function lerComplementoLigado(chave, armazenamento) {
  if (!chave) return true
  try {
    const guardado = (armazenamento ?? globalThis.localStorage).getItem(chave)
    if (guardado === null) return daVisita.get(chave) ?? true
    const ligado = guardado !== 'false'
    daVisita.set(chave, ligado)
    return ligado
  } catch { return daVisita.get(chave) ?? true }
}

export function gravarComplementoLigado(chave, ligado, armazenamento) {
  if (!chave) return false
  daVisita.set(chave, ligado)
  try {
    (armazenamento ?? globalThis.localStorage).setItem(chave, String(ligado))
    return true
  } catch { return false }
}

// Um armazém para todos os campos com sugestão: ligar no Composer vale na hora para o campo de
// observação da comanda, e a troca de usuário ou de aba relê a escolha.
const ouvintes = new Set()
const avisar = () => ouvintes.forEach((ouvinte) => ouvinte())
function assinar(ouvinte) {
  ouvintes.add(ouvinte)
  window.addEventListener(EVENTO_SESSAO_ALTERADA, ouvinte)
  window.addEventListener('storage', ouvinte)
  return () => {
    ouvintes.delete(ouvinte)
    window.removeEventListener(EVENTO_SESSAO_ALTERADA, ouvinte)
    window.removeEventListener('storage', ouvinte)
  }
}
const chaveAtual = () => chaveDoComplemento(lerSessao())
const lerAgora = () => lerComplementoLigado(chaveAtual())

// [ligado, alternar]
export function useComplementoLigado() {
  const ligado = useSyncExternalStore(assinar, lerAgora, () => true)
  const alternar = useCallback(() => {
    gravarComplementoLigado(chaveAtual(), !lerAgora())
    avisar()
  }, [])
  return [ligado, alternar]
}
