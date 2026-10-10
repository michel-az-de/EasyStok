import { Botao } from '../../componentes/Botao'
import { comPadrao, porcaoNova } from '../../dominio/porcoes'
import css from './editorItem.module.css'

// Porções do prato (M1.4a, #1529): 300 g × 800 g com preço próprio. Uma é a padrão (a que já
// vem escolhida); "tem" desligado marca a porção como esgotada sem tirar o prato do cardápio.
export function EditorPorcoes({ linhas, aoMudar, desabilitado = false }) {
  const mudar = (chave, campo, valor) => aoMudar(linhas.map((l) => (l.chave === chave ? { ...l, [campo]: valor } : l)))

  return (
    <fieldset className={css.secao} disabled={desabilitado}>
      <legend className="sr">Porções</legend>
      {linhas.length === 0
        ? <p className={css.ajuda}>Vende em mais de um tamanho? Cada porção pode ter seu próprio preço e disponibilidade.</p>
        : <p className={css.ajuda}>A porção padrão é adicionada no primeiro toque durante o atendimento.</p>}
      {linhas.map((l) => (
        <div key={l.chave} className={css.porcao}>
          <label className={css.campo}>Nome da porção<input placeholder="Ex.: Individual" maxLength={60} value={l.rotulo} onChange={(e) => mudar(l.chave, 'rotulo', e.target.value)} /></label>
          <label className={css.campo}>Peso para mostrar<input placeholder="Ex.: 300 g" maxLength={40} value={l.peso} onChange={(e) => mudar(l.chave, 'peso', e.target.value)} /></label>
          <label className={css.campo}>Preço<input aria-label={`Preço de ${l.rotulo || 'porção'}`} inputMode="decimal" placeholder="0,00" value={l.preco} onChange={(e) => mudar(l.chave, 'preco', e.target.value)} /></label>
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
