// Avisos neste aparelho (#2070): ligar e desligar os avisos deste celular ou computador. A ativação é a do App, a
// mesma do sino: chega pela prop `aparelho` ou pelo contexto de acesso. Sem ela não cria outra (seriam duas inscrições
// e dois estados); aponta para o sino. O que chega e quando fica na aba Avisos (#2096).
import { useId } from 'react'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { TEXTO_DO_APARELHO, podeAtivarNoAparelho, podeDesligarNoAparelho } from '../../../aplicacao/avisosNoAparelho'
import { Botao } from '../../../componentes/Botao'
import css from '../formularioDeAjuste.module.css'

export function AvisosNesteAparelho({ aparelho: informado = null }) {
  const { aparelho: doApp } = useAcessoModulos()
  return <SecaoDoAparelho aparelho={informado ?? doApp ?? null} />
}

function SecaoDoAparelho({ aparelho }) {
  const id = useId()
  return (
    <section className={css.secao} aria-labelledby={`${id}-aparelho`}>
      <h3 id={`${id}-aparelho`}>Avisos neste aparelho</h3>
      {/* Esta aba pode estar num modal ou em tela cheia, sem o sino à vista. */}
      {!aparelho && <p className={css.descricao}>Ligar os avisos neste aparelho não está disponível nesta tela. Ao sair dela, use o sino de Avisos no topo do Console.</p>}
      {aparelho && (aparelho.estado === 'erro'
        ? <p className={css.erro} role="alert">{aparelho.mensagem ?? 'Não foi possível ligar os avisos neste aparelho.'}</p>
        : <output className={css.descricao}>{aparelho.mensagem ?? TEXTO_DO_APARELHO[aparelho.estado]}</output>)}
      <p className={css.descricao}>Vale só para este celular ou computador: ligue em cada aparelho que você usa.</p>
      {aparelho && (
        <div className={css.acoes}>
          {podeAtivarNoAparelho(aparelho.estado) && (
            <Botao variante="primario" className={css.toque} disabled={aparelho.estado === 'ativando'} onClick={aparelho.ativar}>Ativar avisos neste aparelho</Botao>
          )}
          {podeDesligarNoAparelho(aparelho.estado) && (
            <Botao className={css.toque} disabled={aparelho.estado === 'desativando'} onClick={aparelho.desligar}>Desativar avisos neste aparelho</Botao>
          )}
        </div>
      )}
    </section>
  )
}
