import { useMemo, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Pilula } from '../../../componentes/Pilula'
import { useGestaoCardapioApi } from '../../../aplicacao/useGestaoCardapioApi'
import { moeda } from '../../../dominio/formato'
import css from './abaCardapio.module.css'

// M1 › Itens do cardápio (M1.1, #1481). Lista de gestão no modo API: todos os itens da vitrine,
// inclusive os ocultos do site e os desligados hoje (RN-16: desligar não tira da lista). Liga e
// desliga o dia e o site e muda a ordem; cada gravação relê do EasyStok. A edição do item fica
// no balcão (Gerir) até a M1.2 trazer o formulário completo para cá.
const LINHA = { servir: 'Para servir', casa: 'Preparar em casa' }

// "2026-10-20" → "20/10".
const dataCurta = (iso) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}`

function normalizar(texto) {
  return (texto ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
}

export function AbaCardapioApi() {
  const gestao = useGestaoCardapioApi()
  const [busca, setBusca] = useState('')
  const [ocupado, setOcupado] = useState(null)

  const itens = useMemo(() => {
    const termo = normalizar(busca.trim())
    return termo ? gestao.itens.filter((i) => normalizar(`${i.nome} ${i.categoria ?? ''}`).includes(termo)) : gestao.itens
  }, [gestao.itens, busca])

  const fazer = (sku, acao) => {
    setOcupado(sku)
    Promise.resolve(acao()).finally(() => setOcupado(null))
  }

  if (gestao.estado === 'carregando') return <p className={css.descricao}>Carregando o cardápio…</p>
  if (gestao.estado === 'erro' && gestao.itens.length === 0) {
    return (
      <div className={css.aba} role="alert">
        <p>Não consegui ler o cardápio: {gestao.erro}</p>
        <Botao onClick={gestao.recarregar}>Tentar de novo</Botao>
      </div>
    )
  }

  const ordenando = !busca.trim()
  return (
    <div className={css.aba}>
      <div className={css.topo}>
        <p className={css.descricao}>
          Todos os pratos da vitrine, na ordem do cardápio. <strong>Hoje</strong> liga e desliga o prato no dia;
          {' '}<strong>No site</strong> mostra ou esconde na página do cliente; <strong>Tirar</strong> põe fora do
          {' '}cardápio (dá para repor). Item novo fica <strong>em validação</strong>: o assistente só oferece depois
          {' '}que você validar. Nada aqui apaga um item.
        </p>
        <input
          className={css.busca}
          type="search"
          placeholder="Buscar prato ou categoria"
          aria-label="Buscar prato ou categoria"
          value={busca}
          onChange={(e) => setBusca(e.target.value)}
        />
      </div>

      {gestao.aviso && (
        <p className={css.aviso} role="alert">
          {gestao.aviso} <Botao variante="texto" onClick={gestao.fecharAviso}>Fechar</Botao>
        </p>
      )}
      {!ordenando && <p className={css.descricao}>Limpe a busca para mudar a ordem.</p>}

      {itens.length === 0 ? (
        <p className={css.descricao}>{busca ? 'Nenhum prato com esse nome.' : 'A vitrine ainda não tem pratos.'}</p>
      ) : (
        <ol className={css.lista}>
          {itens.map((item, indice) => (
            <li key={item.sku} className={css.linha} aria-busy={ocupado === item.sku}>
              {item.foto
                ? <img className={css.foto} src={item.foto} alt="" loading="lazy" />
                : <span className={css.semFoto} aria-hidden="true" />}
              <div className={css.resumo}>
                <span className={css.nome}>{item.nome}</span>
                <span className={css.detalhe}>
                  {[LINHA[item.linha], item.porcao, item.categoria].filter(Boolean).join(' · ')}
                </span>
              </div>
              <span className={css.preco}>{moeda(item.preco)}</span>
              <div className={css.estados}>
                {item.arquivado && <Pilula tom="neutro" fina>Fora do cardápio</Pilula>}
                {item.emValidacao && <Pilula tom="aviso" fina>Em validação</Pilula>}
                {item.novidadeAte && <Pilula tom="ok" fina>Novidade até {dataCurta(item.novidadeAte)}</Pilula>}
                {!item.hoje && <Pilula tom="aviso" fina>Fora de hoje</Pilula>}
                {!item.noSite && <Pilula tom="neutro" fina>Oculto no site</Pilula>}
              </div>
              <div className={css.acoes}>
                <label className={css.chave}>
                  <input
                    type="checkbox"
                    checked={item.hoje}
                    disabled={ocupado === item.sku}
                    onChange={() => fazer(item.sku, () => gestao.alternarHoje(item.sku))}
                  />
                  Hoje
                </label>
                <label className={css.chave}>
                  <input
                    type="checkbox"
                    checked={item.noSite}
                    disabled={ocupado === item.sku}
                    onChange={() => fazer(item.sku, () => gestao.alternarNoSite(item.sku))}
                  />
                  No site
                </label>
                {item.emValidacao && (
                  <Botao variante="texto" disabled={ocupado === item.sku}
                    onClick={() => fazer(item.sku, () => gestao.validar(item.sku))}>
                    Validar
                  </Botao>
                )}
                <Botao variante="texto" disabled={ocupado === item.sku}
                  onClick={() => fazer(item.sku, () => gestao.alternarArquivado(item.sku))}>
                  {item.arquivado ? 'Repor' : 'Tirar'}
                </Botao>
                {ordenando && (
                  <span className={css.ordem}>
                    <Botao
                      variante="texto"
                      icone="chevron-up"
                      aria-label={`Subir ${item.nome}`}
                      disabled={indice === 0 || ocupado === item.sku}
                      onClick={() => fazer(item.sku, () => gestao.mover(item.sku, 'subir'))}
                    />
                    <Botao
                      variante="texto"
                      icone="chevron-down"
                      aria-label={`Descer ${item.nome}`}
                      disabled={indice === itens.length - 1 || ocupado === item.sku}
                      onClick={() => fazer(item.sku, () => gestao.mover(item.sku, 'descer'))}
                    />
                  </span>
                )}
              </div>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}
