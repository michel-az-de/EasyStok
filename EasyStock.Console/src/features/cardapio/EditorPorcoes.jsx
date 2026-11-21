import { useId } from 'react'
import { Botao } from '../../componentes/Botao'
import { CampoReais } from '../../componentes/CampoReais'
import { centavosDoTexto, lerMoeda, textoDosCentavos } from '../../dominio/formato'
import { erroDoLimite } from '../../componentes/limiteDoCampo'
import { LIMITE_NOME, LIMITE_PESO } from './limitesDaPorcao'
import { comPadrao, porcaoNova } from '../../dominio/porcoes'
import css from './editorItem.module.css'

// Campo da linha com rótulo fora do controle e a frase do limite ligada por aria-describedby.
function CampoDaPorcao({ id, rotulo, limite, valor, ...resto }) {
  const erro = erroDoLimite(valor, limite)
  return (
    <div className={css.campo}>
      <label htmlFor={id}>{rotulo}</label>
      <input id={id} value={valor} aria-invalid={erro ? true : undefined} aria-describedby={erro ? `${id}-erro` : undefined} {...resto} />
      {erro && <small id={`${id}-erro`} className={css.limite} role="status">{erro}</small>}
    </div>
  )
}

// Porções do prato (M1.4a, #1529): 300 g × 800 g com preço próprio. Uma é a padrão (a que já
// vem escolhida); "tem" desligado marca a porção como esgotada sem tirar o prato do cardápio.
// M1.4e (#1552): cada porção pode ter a sua embalagem (bandeja de 300 g), que desce do estoque a cada
// porção produzida. `embalagens` vem vazio para quem não lê Insumos, e a escolha então não aparece.
export function EditorPorcoes({ linhas, aoMudar, desabilitado = false, embalagens = [] }) {
  const base = useId()
  const mudar = (chave, campo, valor) => aoMudar(linhas.map((l) => (l.chave === chave ? { ...l, [campo]: valor } : l)))
  // A embalagem já escolhida continua na lista mesmo que tenha saído de Insumos.
  const opcoesDeEmbalagem = (l) => (!l.embalagemId || embalagens.some((e) => e.id === l.embalagemId)
    ? embalagens
    : [...embalagens, { id: l.embalagemId, nome: 'Embalagem atual (fora da lista de Insumos)' }])

  return (
    <fieldset className={css.secao} disabled={desabilitado}>
      <legend className="sr">Porções</legend>
      {linhas.length === 0
        ? <p className={css.ajuda}>Vende em mais de um tamanho? Cada porção pode ter seu próprio preço e disponibilidade.</p>
        : <p className={css.ajuda}>A porção padrão é adicionada no primeiro toque durante o atendimento.</p>}
      {linhas.length > 0 && embalagens.length > 0 && <p className={css.ajuda}>A embalagem da porção desce do estoque a cada porção produzida, no lugar da embalagem da receita.</p>}
      {linhas.map((l) => (
        <div key={l.chave} className={css.porcao}>
          <CampoDaPorcao id={`${base}-${l.chave}-rotulo`} rotulo="Nome da porção" placeholder="Ex.: Individual" limite={LIMITE_NOME} valor={l.rotulo} onChange={(e) => mudar(l.chave, 'rotulo', e.target.value)} />
          <CampoDaPorcao id={`${base}-${l.chave}-peso`} rotulo="Peso para mostrar" placeholder="Ex.: 300 g" limite={LIMITE_PESO} valor={l.peso} onChange={(e) => mudar(l.chave, 'peso', e.target.value)} />
          <CampoReais rotulo="Preço" aria-label={`Preço de ${l.rotulo || 'porção'}`} centavos={centavosDoTexto(l.preco)} ler={lerMoeda} mostrarZero
            aoMudar={(centavos) => mudar(l.chave, 'preco', textoDosCentavos(centavos))} />
          {opcoesDeEmbalagem(l).length > 0 && (
            <label className={`${css.campo} ${css.embalagemDaPorcao}`}>Embalagem
              <select aria-label={`Embalagem de ${l.rotulo || 'porção'}`} value={l.embalagemId ?? ''} onChange={(e) => mudar(l.chave, 'embalagemId', e.target.value || null)}>
                <option value="">A da receita</option>
                {opcoesDeEmbalagem(l).map((e) => <option key={e.id} value={e.id}>{e.nome}</option>)}
              </select>
            </label>
          )}
          <label className={css.opcao}>
            <input type="radio" name="porcao-padrao" checked={l.padrao} onChange={() => aoMudar(comPadrao(linhas, l.chave))} />
            {' '}Padrão
          </label>
          <label className={css.opcao}>
            <input type="checkbox" checked={l.disponivel} onChange={(e) => mudar(l.chave, 'disponivel', e.target.checked)} />
            {' '}Disponível
          </label>
          <Botao variante="texto" aria-label={`Remover porção ${l.rotulo || 'sem nome'}`} onClick={() => aoMudar(linhas.filter((x) => x.chave !== l.chave))}>Remover</Botao>
        </div>
      ))}
      <Botao icone="mais" onClick={() => aoMudar([...linhas, porcaoNova()])}>Adicionar porção</Botao>
    </fieldset>
  )
}
