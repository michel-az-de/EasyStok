import { useEffect, useState } from 'react'
import { baixarMidia } from '../infra/api/conversasApi'
import { EVENTO_SESSAO_ALTERADA, identidadeDaSessao, lerSessao } from '../infra/api/sessao'
import { criarCacheDeMidia } from './cacheDeMidia'

// Um cache para a aba inteira (#1577). Sai tudo quando a conta muda (sair, sessão vencida, outro
// login); a renovação do token dispara o mesmo evento e não mexe em nada.
const cache = criarCacheDeMidia()
// #3209: o balão e a miniatura do cartão mostram a mesma foto e montam juntos; o arquivo trafega uma vez.
const emVoo = new Map()
let dona = identidadeDaSessao(lerSessao())
globalThis.addEventListener?.(EVENTO_SESSAO_ALTERADA, () => {
  const agora = identidadeDaSessao(lerSessao())
  if (agora === dona) return
  dona = agora
  cache.limpar()
  emVoo.clear()
})

function baixarUmaVez(conversaId, mensagemId) {
  let pedido = emVoo.get(mensagemId)
  if (!pedido) {
    pedido = baixarMidia(conversaId, mensagemId).finally(() => { if (emVoo.get(mensagemId) === pedido) emVoo.delete(mensagemId) })
    emVoo.set(mensagemId, pedido)
  }
  return pedido
}

// Mídia recebida na conversa (modo API, #1287): o arquivo mora no storage privado do
// EasyStok e só sai pelo endpoint autenticado, então vem como Blob e vira um endereço
// local do navegador. #1577: só baixa com o balão perto da tela (`naTela`), e o endereço
// fica no cache quando o balão sai, para a volta à conversa não baixar de novo.
export function useMidiaDaMensagem(midia, naTela = true) {
  const conversaId = midia?.conversaId ?? null
  const mensagemId = midia?.mensagemId ?? null
  const [estado, setEstado] = useState({ url: null, erro: null })

  useEffect(() => {
    if (!conversaId || !mensagemId || !naTela) return undefined
    let vivo = true
    // Este balão segura o endereço no cache do acerto (ou do fim do download) até sair da tela.
    const guardada = cache.usar(mensagemId)
    let segura = Boolean(guardada)
    const arquivo = guardada
      ? Promise.resolve(guardada)
      : baixarUmaVez(conversaId, mensagemId).then((blob) => {
        if (!blob) return null
        // Quem dividiu o download com outro encontra o endereço já guardado.
        const url = cache.usar(mensagemId) ?? cache.guardar(mensagemId, URL.createObjectURL(blob))
        // O balão saiu antes de o arquivo chegar: fica guardado para a volta.
        if (vivo) segura = true
        else cache.soltar(mensagemId)
        return url
      })
    arquivo
      .then((url) => { if (vivo && url) setEstado({ url, erro: null }) })
      .catch((erro) => { if (vivo) setEstado({ url: null, erro: erro.message }) })
    return () => {
      vivo = false
      if (segura) cache.soltar(mensagemId)
    }
  }, [conversaId, mensagemId, naTela])

  return estado
}
