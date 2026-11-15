import { useEffect, useState } from 'react'
import { Icone } from '../../../componentes/Icone'
import { configuracaoDaProducao } from '../../../aplicacao/configuracaoProducao'
import { lerInsumos } from '../../../aplicacao/insumos'
import { lerReceitas } from '../../../aplicacao/receitas'
import { assinarMudancaNaProducao } from '../../../aplicacao/mudancasDaProducao'
import { hashDoModulo } from '../../../dominio/modulos'
import css from './checklistConfiguracao.module.css'

// #2362: no topo do módulo Produção, a ordem em que a loja configura (Insumos → Receitas), enquanto
// faltar algo. Lê de novo a cada troca de tela e a cada gravação de insumo ou receita (#2647).
// Falha de leitura não bloqueia a tela de baixo: o checklist só não aparece.
export function ChecklistConfiguracao() {
  const [config, setConfig] = useState(null)

  useEffect(() => {
    let vivo = true
    const ler = () => Promise.all([lerInsumos(), lerReceitas()])
      .then(([insumos, receitas]) => { if (vivo) setConfig(configuracaoDaProducao({ insumos, receitas })) })
      .catch(() => { if (vivo) setConfig(null) })
    ler()
    // #2647 (A1): gravar insumo ou receita relê na hora, sem esperar a troca de aba.
    const sair = assinarMudancaNaProducao(ler)
    return () => { vivo = false; sair() }
  }, [])

  if (!config?.visivel) return null

  return (
    // #2439: faixa compacta, o rótulo à esquerda e os passos em linha, na ordem da dependência.
    <nav className={css.checklist} aria-label="Configuração inicial da Produção">
      <p className={css.titulo}>Configuração inicial</p>
      <ol className={css.passos}>
        {config.passos.map((p, i) => {
          const ativo = p.id === config.ativo
          return (
            <li key={p.id} className={[css.passo, p.feito ? css.feito : '', ativo ? css.ativo : ''].filter(Boolean).join(' ')}
              aria-current={ativo ? 'step' : undefined}>
              <span className={css.marca} aria-hidden="true">{p.feito ? <Icone nome="circle-check" /> : i + 1}</span>
              <span className={css.texto}>
                <a className={css.link} href={hashDoModulo('producao', p.tela)}>{p.rotulo}</a>
                <span className={css.detalhe}>{p.feito ? `Pronto · ${p.detalhe}` : p.detalhe}</span>
              </span>
            </li>
          )
        })}
      </ol>
    </nav>
  )
}
