import { useState } from 'react'
import { AcoesPdf } from '../../componentes/AcoesPdf'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { Chip } from '../../componentes/Chip'
import { Icone } from '../../componentes/Icone'
import { Pilula } from '../../componentes/Pilula'
import { dataIsoNoFuso, listaEmPortugues, plural } from '../../dominio/formato'
import { pdfDoRoteiro } from '../../dominio/impressao'
import { resumoDoRoteiro, rotuloDaData, textoDeQuemLeva } from '../../dominio/roteiroDoDia'
import css from './entregasApi.module.css'

// Entregas do dia por janela (issue #1440): para cada janela, os pedidos com a situação, o
// endereço, quem leva e o que falta para sair. O roteiro impresso sai do mesmo dado
// (`dominio/roteiroDoDia.js`), em A4 ou na bobina de 80 mm.
const PAPEIS = [{ valor: 'a4', rotulo: 'Folha A4' }, { valor: 'bobina', rotulo: 'Bobina 80 mm' }]
const UM_DIA_MS = 86400000

export function Endereco({ texto }) {
  return (
    <p className={`${css.linha} ${css.apoio}`}>
      <Icone nome="map-pin" tamanho={16} /> {texto ?? 'Sem endereço no cadastro do cliente'}
    </p>
  )
}

const tomDaEtapa = (p) => {
  if (p.etapa === 'saiu' || p.etapa === 'entregue') return 'ok'
  if (p.etapa === 'pagamento' || p.etapa === 'aprovacao' || p.etapa === 'pronto') return 'aviso'
  return 'neutro'
}

function EscolhaDoDia({ dia, mudarDia }) {
  const [agora] = useState(() => Date.now())
  const hoje = dataIsoNoFuso(agora)
  const amanha = dataIsoNoFuso(agora + UM_DIA_MS)
  return (
    <div className={css.linha}>
      <div className={css.linha} role="radiogroup" aria-label="Dia das entregas">
        <Chip papel="escolha" ativo={dia === hoje} onClick={() => mudarDia(hoje)}>Hoje</Chip>
        <Chip papel="escolha" ativo={dia === amanha} onClick={() => mudarDia(amanha)}>Amanhã</Chip>
      </div>
      <div className={css.campoData}>
        <CampoTexto rotulo="Outro dia" rotuloOculto tipo="date" value={dia} onChange={(e) => mudarDia(e.target.value)} />
      </div>
    </div>
  )
}

function PedidoDoDia({ pedido, montando, ocupado, acoes, aoVerViagens }) {
  const detalhes = [
    pedido.pago ? 'Pago' : 'Não pago',
    pedido.entregador ? `Leva ${textoDeQuemLeva(pedido.entregador)}` : null,
    pedido.falta,
  ].filter(Boolean).join(' · ')
  return (
    <li className={css.cartao}>
      <div className={css.linha}>
        <span className={css.numero}>{pedido.numero}</span>
        <span className={css.cresce}>{pedido.cliente}{pedido.apto ? ` · apto ${pedido.apto}` : ''}</span>
        <Pilula tom={tomDaEtapa(pedido)}>{pedido.rotulo}</Pilula>
      </div>
      <Endereco texto={pedido.endereco} />
      <p className={css.apoio}>{detalhes}</p>
      {pedido.podePorNaViagem && (
        <div className={css.linha}>
          {montando.length === 0 && <>
            <span className={css.apoio}>Abra uma viagem para despachar este pedido.</span>
            <Botao variante="texto" onClick={aoVerViagens}>Ver viagens</Botao>
          </>}
          {montando.map((v, i) => (
            <Botao key={v.id} variante="texto" disabled={ocupado} onClick={() => acoes.incluirParada(v.id, pedido.id)}>
              Pôr na viagem {i + 1}
            </Botao>
          ))}
        </div>
      )}
    </li>
  )
}

function JanelaDoDia({ grupo, montando, ocupado, acoes, aoVerViagens }) {
  const n = grupo.pedidos.length
  const titulo = grupo.chave === 'sem-janela' || grupo.label === grupo.faixa ? grupo.faixa : `${grupo.faixa} · ${grupo.label}`
  return (
    <section className={css.janelaDoDia} aria-label={`Janela ${titulo}`}>
      <div className={css.linha}>
        <h3 className={css.cresce}>{titulo}</h3>
        <span className={css.numero}>{grupo.capacidade ? `${n} de ${grupo.capacidade} vagas` : plural(n, 'entrega', 'entregas')}</span>
      </div>
      {grupo.bloqueio && <p className={css.linha}><Pilula tom="aviso">Bloqueada</Pilula> <span className={css.apoio}>{grupo.bloqueio}</span></p>}
      {n > 0 && (
        <p className={css.apoio}>Quem leva: {grupo.quemLeva.length ? listaEmPortugues(grupo.quemLeva) : 'a definir'}</p>
      )}
      {n === 0 && <p className={css.vazio}>Nenhum pedido nesta janela.</p>}
      <ul className={css.lista}>
        {grupo.pedidos.map((p) => <PedidoDoDia key={p.id} pedido={p} montando={montando} ocupado={ocupado} acoes={acoes} aoVerViagens={aoVerViagens} />)}
      </ul>
    </section>
  )
}

// `roteiro`: `roteiroDoDia` já montado por quem chama (nulo enquanto o dia carrega).
export function EntregasDoDia({ dia, mudarDia, roteiro, montando, ocupado, acoes, aoAbrirCadastro, aoVerViagens }) {
  const [papel, setPapel] = useState('a4')

  return (
    <>
      <section className={css.secao} aria-label="Dia das entregas">
        <EscolhaDoDia dia={dia} mudarDia={mudarDia} />
        {roteiro && (
          <div className={css.cabecalhoDoDia}>
            <div className={css.cresce}>
              <p className={css.dataDoDia}>{rotuloDaData(roteiro.data)}</p>
              <p className={css.apoio}>{resumoDoRoteiro(roteiro)}</p>
              {roteiro.bloqueioDoDia && <p className={css.linha}><Pilula tom="aviso">Dia bloqueado</Pilula> <span className={css.apoio}>{roteiro.bloqueioDoDia}</span></p>}
            </div>
            <div className={css.linha}>
              <div className={css.campoData}>
                <CampoSelecao rotulo="Papel" rotuloOculto opcoes={PAPEIS} value={papel} onChange={(e) => setPapel(e.target.value)} />
              </div>
              <AcoesPdf varianteImprimir="secundario" gerar={() => pdfDoRoteiro({ roteiro, papel, agora: Date.now() })} />
            </div>
          </div>
        )}
      </section>
      {!roteiro && <p className={css.vazio}>Carregando o dia…</p>}
      {roteiro?.grupos.length === 0 && (
        <div className={css.secao}>
          <p className={css.vazio}>Nenhuma janela neste dia e nenhum pedido marcado.</p>
          <Botao variante="secundario" onClick={aoAbrirCadastro}>Cadastrar janelas e frete</Botao>
        </div>
      )}
      {roteiro?.grupos.map((g) => <JanelaDoDia key={g.chave} grupo={g} montando={montando} ocupado={ocupado} acoes={acoes} aoVerViagens={aoVerViagens} />)}
    </>
  )
}
