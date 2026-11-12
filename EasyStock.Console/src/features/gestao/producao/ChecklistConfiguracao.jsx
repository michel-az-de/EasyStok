import { useEffect, useState } from 'react'
import { Icone } from '../../../componentes/Icone'
import { configuracaoDaProducao } from '../../../aplicacao/configuracaoProducao'
import { lerInsumos } from '../../../aplicacao/insumos'
import { lerReceitas } from '../../../aplicacao/receitas'
import { hashDoModulo } from '../../../dominio/modulos'
import css from './checklistConfiguracao.module.css'

// #2362: no topo do módulo Produção, a ordem em que a loja configura (Insumos → Receitas), enquanto
// faltar algo. Lê de novo a cada troca de tela, então o passo concluído aparece ao voltar.
// Falha de leitura não bloqueia a tela de baixo: o checklist só não aparece.
export function ChecklistConfiguracao() {
  const [config, setConfig] = useState(null)

  useEffect(() => {
    let vivo = true
    Promise.all([lerInsumos(), lerReceitas()])
      .then(([insumos, receitas]) => { if (vivo) setConfig(configuracaoDaProducao({ insumos, receitas })) })
      .catch(() => { if (vivo) setConfig(null) })
    return () => { vivo = false }
  }, [])

  if (!config?.visivel) return null

  return (
    <nav className={css.checklist} aria-label="Configuração da Produção">
      <p className={css.titulo}>Para a Produção funcionar, configure nesta ordem:</p>
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
