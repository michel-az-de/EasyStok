import { Icone } from '../../componentes/Icone'
import { CAMPOS, automaticoConduzindo } from '../../dominio/captura'
import css from './ficha.module.css'

// Rodada 11 (issue #8, registro 92). O pedido do dono: "atendimento ao
// cliente deveria ser automatizado cadastro do cliente conversas eu nao vi
// isso funcionar". Esta faixa diz, no topo da ficha, onde o automático está
// no caminho do UC-01 (nome, telefone, endereço, cadastro, pedido), para a
// dona acompanhar sem reler a conversa inteira (US-017) e decidir se assume
// (o toque "Assumir" continua um só, na barra da conversa: US-004).
const PASSOS = [
  { chave: CAMPOS.NOME, rotulo: 'Nome' },
  { chave: CAMPOS.TELEFONE, rotulo: 'Telefone' },
  { chave: CAMPOS.ENDERECO, rotulo: 'Endereço' },
  { chave: 'cadastro', rotulo: 'Cadastro' },
  { chave: CAMPOS.PEDIDO, rotulo: 'Pedido' },
]

function feito(chave, conversa) {
  const { cliente, captura } = conversa
  if (chave === CAMPOS.NOME) return Boolean(cliente.captado?.nome)
  if (chave === CAMPOS.TELEFONE) return Boolean(cliente.telefone || cliente.telefoneCanal)
  if (chave === CAMPOS.ENDERECO) return Boolean(cliente.endereco)
  if (chave === 'cadastro') return Boolean(captura.cadastroEm)
  return Boolean(conversa.pedido)
}

export function BlocoCapturaAutomatica({ conversa, pausado }) {
  const { captura } = conversa
  const conduzindo = automaticoConduzindo(conversa, pausado)
  const tudoFeito = PASSOS.every((p) => feito(p.chave, conversa))
  const esperaVoce = Boolean(conversa.passagem && !conversa.passagem.assumida) || Boolean(conversa.cliente.enderecoCapturado)
  let status = 'Conduzindo a conversa sozinho. Assuma pela barra da conversa quando quiser.'
  if (conversa.cliente.enderecoCapturado) status = 'Endereço fora da área: a decisão é sua, logo abaixo.'
  else if (esperaVoce) status = 'Passou para você: o automático parou aqui. O que ele captou continua na ficha.'
  else if (!conduzindo) status = 'Você está no controle: o automático parou aqui. O que ele captou continua na ficha.'
  else if (tudoFeito) status = 'Pedido anotado pelo automático. Confira a comanda e gere a cobrança.'
  const selo = esperaVoce ? 'com você' : conduzindo ? 'ativo' : 'parado'

  return (
    <section className={css.capturaAuto} aria-label="Atendimento automático">
      <p className={css.capturaTitulo}>
        <Icone nome="raio" /> Atendimento automático
        <span className={conduzindo ? css.capturaAtivo : css.capturaParado}>{selo}</span>
      </p>
      <ol className={css.capturaPassos}>
        {PASSOS.map((passo) => {
          const pronto = feito(passo.chave, conversa)
          const agora = !pronto && conduzindo && captura.aguardando === passo.chave
          const classe = pronto ? css.passoFeito : agora ? css.passoAgora : ''
          return (
            <li key={passo.chave} className={classe}>
              <span className={css.passoMarca} aria-hidden="true">
                {pronto ? <Icone nome="check" tamanho={12} /> : null}
              </span>
              {passo.rotulo}
              <span className="sr">{pronto ? ', feito' : agora ? ', pedindo agora' : ', falta'}</span>
            </li>
          )
        })}
      </ol>
      <p className={css.capturaStatus}>{status}</p>
    </section>
  )
}
