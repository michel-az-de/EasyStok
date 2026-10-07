import { useState } from 'react'
import css from './Avatar.module.css'

// Foto do contato. Quando a integração existir, `foto` (URL) vem do canal ou
// do cadastro e a inicial some. Sem foto, o fallback é determinístico: a mesma
// pessoa recebe sempre a mesma cor, então o reconhecimento visual funciona
// mesmo assim. #1442: a WhatsApp Cloud API não entrega foto de perfil do
// contato; hoje nenhum canal ligado manda foto, então a inicial é a regra.

const MATIZES = [8, 32, 96, 150, 190, 262, 300, 338]

const iniciais = (nome) => nome
  .split(' ')
  .filter((p) => p.length > 2)
  .slice(0, 2)
  .map((p) => p[0].toUpperCase())
  .join('')

function matizDe(nome) {
  const soma = [...nome].reduce((acc, ch) => acc + ch.charCodeAt(0), 0)
  return MATIZES[soma % MATIZES.length]
}

export function Avatar({ nome, foto, tamanho = 'medio', selo }) {
  // Foto que não carrega (link expirado, CDN fora) volta para a inicial em vez
  // de deixar o ícone de imagem quebrada. Guarda QUAL endereço falhou: outra
  // foto (outra conversa) tenta de novo.
  const [falhou, setFalhou] = useState(null)
  const estilo = { '--matiz': matizDe(nome) }
  const mostrarFoto = Boolean(foto) && falhou !== foto
  return (
    <span className={`${css.envelope} ${css[tamanho]}`}>
      {mostrarFoto
        ? <img className={css.foto} src={foto} alt={'Foto de ' + nome} onError={() => setFalhou(foto)} />
        : (
          <span className={css.inicial} style={estilo} aria-hidden="true">
            {iniciais(nome) || '?'}
          </span>
        )}
      {selo && <span className={css.selo} title={selo.titulo}>{selo.icone}</span>}
    </span>
  )
}
