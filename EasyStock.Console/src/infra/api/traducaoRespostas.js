import { GATILHOS_DA_API, REGRAS_PADRAO } from '../../dominio/automacao'

// Respostas prontas e automáticas entre o console e o EasyStok (S42, #1441).
// O console mostra o atalho sempre com "/" na frente; a API guarda o que a
// dona escreveu, normalizado (minúsculo, sem espaço), e aceita com ou sem "/".

const comBarra = (atalho) => {
  const limpo = String(atalho ?? '').trim()
  return limpo.startsWith('/') ? limpo : `/${limpo}`
}

export const respostaDaApi = (r) => ({
  id: r.id,
  titulo: r.titulo,
  categoria: 'Respostas',
  atalho: comBarra(r.atalho),
  texto: r.texto,
  arquivada: Boolean(r.arquivada),
})

export const corpoDaResposta = ({ titulo, atalho, texto }) => ({
  titulo: String(titulo ?? '').trim(),
  atalho: String(atalho ?? '').trim().replace(/^\/+/, ''),
  texto: String(texto ?? '').trim(),
})

// Gatilho sem regra na API vem desligado e sem texto: a sugestão é o texto da
// demonstração, que a dona pode aproveitar antes de ligar.
export function regraDaApi(r) {
  const mapa = GATILHOS_DA_API[r.gatilho] ?? { id: String(r.gatilho), gatilho: String(r.gatilho), nome: String(r.gatilho), descricao: '' }
  const padrao = REGRAS_PADRAO.find((p) => p.gatilho === mapa.gatilho)
  return {
    ...mapa,
    gatilhoApi: r.gatilho,
    ativa: Boolean(r.ligada),
    texto: r.texto ?? '',
    sugestao: r.texto ? null : padrao?.texto ?? null,
    alteradaEm: r.alteradaEm ?? null,
  }
}
