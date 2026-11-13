import * as acao from '../acoes'
import { acoesDaConversa } from '../../dominio/acoesDaConversa'
import { canalDaConversa } from '../../dominio/canal'
import { envioBloqueado } from '../../dominio/janela'

// #2185 (F12, E09): uma regra só de "responder é assumir", no caminho do envio. Texto, foto, galeria,
// cardápio, Assistente, despedida e cobrança chegam todos aqui antes de falar com o cliente; antes só o
// texto e o áudio do campo assumiam, e as outras portas deixavam duas vozes falando ao mesmo tempo.
//
// `assumirNaApi(id)` fala com o EasyStok e devolve `true` quando a conversa passou a ser dela.
// - `assumir`: o botão Assumir e o campo de mensagem (que mostra o próprio aviso). Uma ida por conversa.
// - `paraEnviar`: `true` na hora quando não há o que assumir; senão, a promessa do assumir.
//
// O estado do React atrasa um quadro depois da confirmação: quem acabou de assumir não assume de novo
// enquanto o estado for o mesmo objeto em que a confirmação aconteceu.
export const SEM_ASSUMIR = 'Não consegui assumir a conversa, então a mensagem não saiu.'

export function criarAssumirAntesDeEnviar({ despachar, estadoRef, agoraRef, assumirNaApi }) {
  const emVoo = new Map()
  const assumidaCom = new Map()

  const assumir = (id) => {
    if (!emVoo.has(id)) {
      emVoo.set(id, assumirNaApi(id).then((ok) => {
        if (ok) assumidaCom.set(id, estadoRef.current)
        return ok
      }).finally(() => emVoo.delete(id)))
    }
    return emVoo.get(id)
  }

  const precisa = (id) => {
    const estado = estadoRef.current
    if (assumidaCom.has(id) && assumidaCom.get(id) !== estado) assumidaCom.delete(id)
    // Sem o mapa de pausas o estado ainda não sabe quem atende: não inventa um assumir.
    if (assumidaCom.has(id) || !estado.automaticoPausado) return false
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa) return false
    const canal = canalDaConversa(estado.catalogo?.canais, conversa)
    return acoesDaConversa({
      conversa, canal, pausado: estado.automaticoPausado[id] ?? false,
      bloqueada: envioBloqueado(conversa, agoraRef.current, canal),
    }).assumirAntesDeEnviar
  }

  function paraEnviar(id) {
    if (!emVoo.has(id) && !precisa(id)) return true
    return assumir(id).then((ok) => {
      if (!ok) despachar({ tipo: acao.AVISO_API, mensagem: SEM_ASSUMIR })
      return ok
    })
  }

  // Ação que fala com o cliente: assume antes e só então roda `fazer`. `recusa` é o que volta se não assumiu.
  function antesDeEnviar(id, fazer, recusa = false) {
    const assumida = paraEnviar(id)
    return assumida === true ? fazer() : assumida.then((ok) => (ok ? fazer() : recusa))
  }

  return { assumir, paraEnviar, antesDeEnviar }
}
