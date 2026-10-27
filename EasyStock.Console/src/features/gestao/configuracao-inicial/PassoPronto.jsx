// Revisão dos Primeiros passos (#2070): Pronto, Falta, Deixado para depois e Sem acesso. Abrir a loja e o caixa fica
// no gesto que já existe em Atendimento; aqui só o caminho até ele.
import { useId } from 'react'
import { resumoDaRevisao } from '../../../dominio/configuracaoInicial'
import { hashDoModulo } from '../../../dominio/modulos'
import { hashDoPasso } from '../../../dominio/rota'
import css from './configuracaoInicial.module.css'

function Grupo({ titulo, itens, children }) {
  const id = useId()
  if (itens.length === 0) return null
  return (
    <section className={css.secao} aria-labelledby={id}>
      <h3 id={id}>{titulo}</h3>
      <ul className={css.itens}>{itens.map(children)}</ul>
    </section>
  )
}

export function PassoPronto({ estado, podeAbrirLoja = false }) {
  const resumo = resumoDaRevisao(estado)
  return (
    <div className={css.bloco}>
      <Grupo titulo="Pronto" itens={resumo.pronto}>{(p) => <li key={p.passo}>{p.rotulo}</li>}</Grupo>
      <Grupo titulo="Falta" itens={resumo.falta}>
        {(p) => <li key={p.passo}><a href={hashDoPasso(p.passo)}>{p.rotulo}</a>: {p.texto}</li>}
      </Grupo>
      <Grupo titulo="Deixado para depois" itens={resumo.depois}>
        {(p) => <li key={p.passo}><a href={hashDoPasso(p.passo)}>{p.rotulo}</a></li>}
      </Grupo>
      <Grupo titulo="Sem acesso" itens={resumo.semAcesso}>{(p) => <li key={p.passo}>{p.rotulo}: {p.texto}</li>}</Grupo>
      <Grupo titulo="Outros ajustes" itens={resumo.desconhecidos}>
        {(p) => <li key={p.passo}><a href={p.href}>{p.texto}</a></li>}
      </Grupo>
      {podeAbrirLoja && (
        <p className={css.destaque}>
          Quando estiver tudo certo, abra a loja e o caixa em <a href={hashDoModulo('atendimento', 'horarios')}>Atendimento › Horários e mensagens</a>.
        </p>
      )}
    </div>
  )
}
