import { Botao } from '../../componentes/Botao'
import { CampoReais } from '../../componentes/CampoReais'
import { centavosDoTexto, lerMoeda, textoDosCentavos } from '../../dominio/formato'
import { comPadrao, porcaoNova } from '../../dominio/porcoes'
import css from './editorItem.module.css'

// Porções do prato (M1.4a, #1529): 300 g × 800 g com preço próprio. Uma é a padrão (a que já
// vem escolhida); "tem" desligado marca a porção como esgotada sem tirar o prato do cardápio.
// M1.4e (#1552): cada porção pode ter a sua embalagem (bandeja de 300 g), que desce do estoque a cada
// porção produzida. `embalagens` vem vazio para quem não lê Insumos, e a escolha então não aparece.
export function EditorPorcoes({ linhas, aoMudar, desabilitado = false, embalagens = [] }) {
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
          <label className={css.campo}>Nome da porção<input placeholder="Ex.: Individual" maxLength={60} value={l.rotulo} onChange={(e) => mudar(l.chave, 'rotulo', e.target.value)} /></label>
          <label className={css.campo}>Peso para mostrar<input placeholder="Ex.: 300 g" maxLength={40} value={l.peso} onChange={(e) => mudar(l.chave, 'peso', e.target.value)} /></label>
          <CampoReais rotulo="Preço" aria-label={`Preço de ${l.rotulo || 'porção'}`} centavos={centavosDoTexto(l.preco)} ler={lerMoeda}
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
