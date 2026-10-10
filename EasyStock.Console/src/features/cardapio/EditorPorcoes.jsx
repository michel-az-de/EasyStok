import { Botao } from '../../componentes/Botao'
import { comPadrao, porcaoNova } from '../../dominio/porcoes'
import css from './cardapio.module.css'

// Porções do prato (M1.4a, #1529): 300 g × 800 g com preço próprio. Uma é a padrão (a que já
// vem escolhida); "tem" desligado marca a porção como esgotada sem tirar o prato do cardápio.
export function EditorPorcoes({ linhas, aoMudar, desabilitado = false }) {
  const mudar = (chave, campo, valor) => aoMudar(linhas.map((l) => (l.chave === chave ? { ...l, [campo]: valor } : l)))

  return (
    <fieldset className={css.camposAdicionais} disabled={desabilitado}>
      <legend>Porções</legend>
      {linhas.length === 0
        ? <p>Sem porções: o prato vende pelo preço e pela porção acima.</p>
        : <p>Com porções, o preço e a porção do prato acompanham a porção padrão.</p>}
      {linhas.map((l) => (
        <div key={l.chave} className={css.opcaoAdicional}>
          <input aria-label="Nome da porção" placeholder="Ex.: 300 g" maxLength={60} value={l.rotulo} onChange={(e) => mudar(l.chave, 'rotulo', e.target.value)} />
          <input aria-label="Peso para mostrar" placeholder="Peso (opcional)" maxLength={40} value={l.peso} onChange={(e) => mudar(l.chave, 'peso', e.target.value)} />
          <input aria-label={`Preço de ${l.rotulo || 'porção'}`} inputMode="decimal" placeholder="Preço" value={l.preco} onChange={(e) => mudar(l.chave, 'preco', e.target.value)} />
          <label>
            <input type="radio" name="porcao-padrao" checked={l.padrao} onChange={() => aoMudar(comPadrao(linhas, l.chave))} />
            {' '}Padrão
          </label>
          <label>
            <input type="checkbox" checked={l.disponivel} onChange={(e) => mudar(l.chave, 'disponivel', e.target.checked)} />
            {' '}Tem
          </label>
          <Botao variante="texto" onClick={() => aoMudar(linhas.filter((x) => x.chave !== l.chave))}>Tirar</Botao>
        </div>
      ))}
      <Botao onClick={() => aoMudar([...linhas, porcaoNova()])}>Mais uma porção</Botao>
    </fieldset>
  )
}
