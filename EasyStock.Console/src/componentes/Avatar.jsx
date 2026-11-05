import { useState } from 'react'
import css from './Avatar.module.css'

// Foto real quando fornecida pelo chamador. Sem foto, ou se a URL falhar,
// mostra as iniciais; nunca busca retratos em serviços externos.

const MATIZES = [8, 32, 96, 150, 190, 262, 300, 338]

// #2232 (banca UX, L7): só palavra que começa com letra vira inicial. Telefone no lugar do nome
// ("555434619500") dava "5"; agora cai no "?" do contato sem nome.
const iniciais = (nome) => nome
  .split(/\s+/)
  .filter((p) => p.length > 2 && /^\p{L}/u.test(p))
  .slice(0, 2)
  .map((p) => p[0].toUpperCase())
  .join('')

function matizDe(nome) {
  const soma = [...nome].reduce((acc, ch) => acc + ch.charCodeAt(0), 0)
  return MATIZES[soma % MATIZES.length]
}

export function Avatar({ nome, foto, tamanho = 'medio', selo }) {
  const nomeDoContato = String(nome ?? '').trim() || 'Contato'
  // Foto que não carrega (link expirado, CDN fora) volta para a inicial em vez
  // de deixar o ícone de imagem quebrada. Guarda QUAL endereço falhou: outra
  // foto (outra conversa) tenta de novo.
  const [falhou, setFalhou] = useState(null)
  const estilo = { '--matiz': matizDe(nomeDoContato) }
  const mostrarFoto = Boolean(foto) && falhou !== foto
  return (
    <span className={`${css.envelope} ${css[tamanho]}`}>
      {mostrarFoto
        ? <img className={css.foto} src={foto} alt={'Foto de ' + nomeDoContato} onError={() => setFalhou(foto)} />
        : (
          <span className={css.inicial} style={estilo} aria-hidden="true">
            {iniciais(nomeDoContato) || '?'}
          </span>
        )}
      {selo && <span className={css.selo} title={selo.titulo}>{selo.icone}</span>}
    </span>
  )
}
