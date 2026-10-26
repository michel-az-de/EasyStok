// Avisos (#2070, só no modo API): o que a dona recebe e por qual canal, só leitura, e o botão de
// ligar os avisos neste aparelho. Quem monta pode passar o `aparelho` do App (o mesmo do sino);
// sem ele, a aba usa a mesma ativação do sino com a sessão aberta.
import { useId } from 'react'
import { TEXTO_DO_APARELHO, podeAtivarNoAparelho, podeDesligarNoAparelho } from '../../../aplicacao/avisosNoAparelho'
import { useAvisosNoAparelhoDaSessao } from '../../../aplicacao/useAvisosNoAparelho'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { AVISOS_DA_DONA } from '../../../dominio/avisosDaDona'
import css from '../formularioDeAjuste.module.css'

export function AbaAvisos({ aparelho = null, compacto = false }) {
  const id = useId()
  return (
    <div className={css.aba}>
      {aparelho ? <AvisosNesteAparelho aparelho={aparelho} /> : <AvisosNesteAparelhoDaSessao />}
      <section className={css.secao} aria-labelledby={`${id}-recebe`}>
        <h3 id={`${id}-recebe`}>O que você recebe</h3>
        {!compacto && <p className={css.descricao}>Os avisos já vêm prontos; para mudar o que chega, fale com o suporte.</p>}
        <ul className={css.lista}>
          {AVISOS_DA_DONA.map((aviso) => (
            <li key={aviso.titulo} className={css.cartao}>
              <h4>{aviso.titulo}</h4>
              <p className={css.descricao}>{aviso.quando}</p>
              <ul className={css.canais} aria-label={`Canais: ${aviso.titulo}`}>
                {aviso.canais.map((canal) => <li key={canal}><Pilula tom="neutro" fina>{canal}</Pilula></li>)}
              </ul>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}

function AvisosNesteAparelhoDaSessao() {
  return <AvisosNesteAparelho aparelho={useAvisosNoAparelhoDaSessao()} />
}

function AvisosNesteAparelho({ aparelho }) {
  const id = useId()
  return (
    <section className={css.secao} aria-labelledby={`${id}-aparelho`}>
      <h3 id={`${id}-aparelho`}>Avisos neste aparelho</h3>
      {aparelho.estado === 'erro'
        ? <p className={css.erro} role="alert">{aparelho.mensagem ?? 'Não foi possível ligar os avisos neste aparelho.'}</p>
        : <output className={css.descricao}>{aparelho.mensagem ?? TEXTO_DO_APARELHO[aparelho.estado]}</output>}
      <p className={css.descricao}>Vale só para este celular ou computador: ligue em cada aparelho que você usa.</p>
      <div className={css.acoes}>
        {podeAtivarNoAparelho(aparelho.estado) && (
          <Botao variante="primario" className={css.toque} disabled={aparelho.estado === 'ativando'} onClick={aparelho.ativar}>Ativar avisos neste aparelho</Botao>
        )}
        {podeDesligarNoAparelho(aparelho.estado) && (
          <Botao className={css.toque} disabled={aparelho.estado === 'desativando'} onClick={aparelho.desligar}>Desativar avisos neste aparelho</Botao>
        )}
      </div>
    </section>
  )
}
