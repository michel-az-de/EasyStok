// Financeiro › Fechamentos do aparelho (#2813, B4, só no modo API): os caixas que o aparelho antigo fechou, SÓ PARA CONSULTA.
// É arquivo histórico: o aparelho nunca mais envia fechamento (#2030), então a tela não edita, não apaga nem reenvia nada.
// A rota é da policy Gerente; quem não pode nem vê a tela (`acao` em dominio/modulos.js).
import { Botao } from '../../../componentes/Botao'
import { EsqueletoDeLista } from '../../../componentes/Esqueleto'
import { Vazio } from '../../../componentes/Vazio'
import { useFechamentosLegados } from '../../../aplicacao/fechamentosLegados'
import { avisoDeIgnorados, diaDoFechamento, quemFechou, totaisDoFechamento } from '../../../dominio/fechamentosLegados'
import css from './fechamentosLegados.module.css'

// Só desenha o estado recebido; a leitura mora no hook. Carregando, erro e vazio nunca aparecem juntos: lista vazia
// sem leitura concluída é "carregando" ou "erro", não "nenhum fechamento".
export function CorpoDosFechamentosLegados({ carregando, erro, itens, ignorados, tentarDeNovo }) {
  const aviso = avisoDeIgnorados(ignorados)
  return (
    <div className={css.aba}>
      <p className={css.descricao}>
        Caixas fechados no aparelho antigo, do mais recente para o mais antigo. É um arquivo só para consulta: o aparelho
        não envia mais fechamentos, e o fechamento do dia continua no Caixa do dia.
      </p>
      {aviso && <output className={css.aviso}>{aviso}</output>}
      {erro && (
        <p className={css.erro} role="alert">
          Os fechamentos não carregaram: {erro}{' '}
          <Botao variante="texto" onClick={tentarDeNovo}>Tentar de novo</Botao>
        </p>
      )}
      {carregando && <EsqueletoDeLista linhas={4}>Carregando os fechamentos…</EsqueletoDeLista>}
      {!carregando && !erro && itens.length === 0 && (
        <Vazio titulo="Nenhum fechamento do aparelho">
          Os caixas fechados no aparelho antigo aparecem aqui quando o histórico dele estiver no EasyStok.
        </Vazio>
      )}
      {itens.length > 0 && (
        <ul className={css.lista}>
          {itens.map((fechamento) => (
            <li key={fechamento.id} className={css.fechamento}>
              <div className={css.cabecalho}>
                <h3 className={css.dia}><time dateTime={fechamento.dia}>{diaDoFechamento(fechamento)}</time></h3>
                <span className={css.quem}>{quemFechou(fechamento)}</span>
              </div>
              {fechamento.observacoes && <p className={css.observacoes}><strong>Observações:</strong> {fechamento.observacoes}</p>}
              <dl className={css.totais}>
                {totaisDoFechamento(fechamento).map((total) => (
                  <div key={total.rotulo} className={css.total}>
                    <dt>{total.rotulo}</dt>
                    <dd>{total.valor}</dd>
                  </div>
                ))}
              </dl>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export function AbaFechamentosLegados() {
  const leitura = useFechamentosLegados()
  return <CorpoDosFechamentosLegados {...leitura} />
}
