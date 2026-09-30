// Aba "Janelas de entrega" da Gestão (rodada 13, issue #42, registro 104,
// D9/RN-21/RN-22/Q2). Até aqui a janela só existia como dado de semente
// (infra/catalogo.js) e regra pura (dominio/entrega.js): esta tela é onde a
// Thati cria, edita, pausa/reativa e ajusta capacidade, corte e respiro sem
// mexer em código. Mudar aqui reflete na hora no seletor da ficha, no
// Balcão/Entregas e no que o agente oferece, porque todos leem
// `estado.catalogo.janelas` e `ocupacaoDeHoje` (mesmas funções de sempre).
import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoTexto } from '../../../componentes/Campo'
import { Chip } from '../../../componentes/Chip'
import { Icone } from '../../../componentes/Icone'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../../aplicacao/contextos'
import {
  DIAS_DA_SEMANA, MINUTOS_DE_CORTE, RESPIRO_MINIMO_RN22, TODOS_OS_DIAS,
  ajustarRespiroMinimo, erroDaJanela, janelaTemPedidos, ocupacaoDaJanela, rotuloDaOcupacao,
} from '../../../dominio/entrega'
import css from './janelas.module.css'

const rotuloDosDias = (diasSemana) => {
  if (!diasSemana || diasSemana.length === 7) return 'todo dia'
  const nomes = diasSemana
    .slice().sort((a, b) => a - b)
    .map((v) => DIAS_DA_SEMANA.find((d) => d.valor === v)?.rotulo)
  return nomes.join(', ')
}

// Campos que o formulário edita, num objeto só: nasce da janela existente
// (editar) ou de valores padrão sensatos (nova, capacidade 4 e corte igual ao
// chão de sempre). `capacidadePorLinha` some do formulário quando a Thati não
// liga a Q2, e a tela nunca inventa um número que ela não digitou.
function camposIniciais(janela) {
  if (janela) {
    return {
      horaInicio: janela.horaInicio ?? '',
      horaFim: janela.horaFim ?? '',
      capacidade: String(janela.capacidade ?? ''),
      corteMinutos: String(janela.corteMinutos ?? MINUTOS_DE_CORTE),
      diasSemana: janela.diasSemana ?? TODOS_OS_DIAS,
      porLinha: Boolean(janela.capacidadePorLinha),
      servir: String(janela.capacidadePorLinha?.servir ?? ''),
      casa: String(janela.capacidadePorLinha?.casa ?? ''),
    }
  }
  return {
    horaInicio: '', horaFim: '', capacidade: '4', corteMinutos: String(MINUTOS_DE_CORTE),
    diasSemana: TODOS_OS_DIAS, porLinha: false, servir: '', casa: '',
  }
}

// Converte o rascunho de string (todo campo de formulário chega assim) para o
// formato que `dominio/entrega.js` valida e grava. `null` em vez de NaN
// quando o campo está vazio, para `erroDaJanela` acusar o motivo certo em vez
// de "não é número".
function camposParaSalvar(rascunho) {
  return {
    horaInicio: rascunho.horaInicio,
    horaFim: rascunho.horaFim,
    capacidade: rascunho.capacidade === '' ? null : Number(rascunho.capacidade),
    corteMinutos: rascunho.corteMinutos === '' ? null : Number(rascunho.corteMinutos),
    diasSemana: rascunho.diasSemana,
    capacidadePorLinha: rascunho.porLinha
      ? { servir: rascunho.servir === '' ? null : Number(rascunho.servir), casa: rascunho.casa === '' ? null : Number(rascunho.casa) }
      : null,
  }
}

function ToggleDias({ diasSemana, aoMudar }) {
  const alternar = (valor) => {
    aoMudar(diasSemana.includes(valor)
      ? diasSemana.filter((v) => v !== valor)
      : [...diasSemana, valor])
  }
  // Sem `role="group"`: o rótulo visível "Dias da semana" já vem logo antes
  // (`.rotuloCampo`), e oxlint prefere o elemento semântico (fieldset) a um
  // `role` solto quando ele existe.
  return (
    <div className={css.dias}>
      {DIAS_DA_SEMANA.map((dia) => (
        <Chip key={dia.valor} papel="filtro" ativo={diasSemana.includes(dia.valor)} onClick={() => alternar(dia.valor)}>
          {dia.rotulo}
        </Chip>
      ))}
    </div>
  )
}

// Formulário de criar/editar, um componente só (a diferença é só o valor
// inicial e o texto do botão): duas telas com o mesmo formulário nunca
// divergem em validação.
function FormularioJanela({ valorInicial, rotuloSalvar, aoSalvar, aoCancelar }) {
  const [rascunho, setRascunho] = useState(valorInicial)
  const [erro, setErro] = useState(null)
  const mudar = (campo, valor) => setRascunho((r) => ({ ...r, [campo]: valor }))

  function confirmar() {
    const campos = camposParaSalvar(rascunho)
    const motivo = erroDaJanela(campos)
    if (motivo) { setErro(motivo); return }
    aoSalvar(campos)
  }

  return (
    <div className={css.edicao}>
      <div className={css.campoLinha}>
        <CampoTexto rotulo="Início" tipo="time" value={rascunho.horaInicio} onChange={(e) => mudar('horaInicio', e.target.value)} />
        <CampoTexto rotulo="Fim" tipo="time" value={rascunho.horaFim} onChange={(e) => mudar('horaFim', e.target.value)} />
      </div>
      <div className={css.campos}>
        <CampoTexto
          rotulo="Capacidade (pedidos)" tipo="number" min="1" step="1"
          value={rascunho.capacidade} onChange={(e) => mudar('capacidade', e.target.value)}
        />
        <CampoTexto
          rotulo="Corte (min. antes)" tipo="number" min="0" step="5"
          dica="Sem tempo de preparo dentro disto, a janela some da oferta."
          value={rascunho.corteMinutos} onChange={(e) => mudar('corteMinutos', e.target.value)}
        />
      </div>
      <div>
        <p className={css.rotuloCampo}>Dias da semana</p>
        <ToggleDias diasSemana={rascunho.diasSemana} aoMudar={(dias) => mudar('diasSemana', dias)} />
      </div>

      {/* Q2 em aberto (issue #42, capacidade quando o pedido mistura as duas
          linhas): proposta ajustável, desligada por padrão. Ligar mostra duas
          capacidades, uma por linha de RN-17; um pedido com item das duas
          linhas ocupa vaga nas duas (dominio/entrega.js, `ocupacaoPorLinha`). */}
      <label className={css.porLinhaChave}>
        <input type="checkbox" checked={rascunho.porLinha} onChange={(e) => mudar('porLinha', e.target.checked)} />
        Capacidade separada por linha
        <span className={css.proposta}>pedido com as duas linhas ocupa vaga nas duas</span>
      </label>
      {rascunho.porLinha && (
        <div className={css.porLinha}>
          <div className={css.campoLinha}>
            <CampoTexto
              rotulo="Vagas para servir" tipo="number" min="0" step="1"
              value={rascunho.servir} onChange={(e) => mudar('servir', e.target.value)}
            />
            <CampoTexto
              rotulo="Vagas para preparar em casa" tipo="number" min="0" step="1"
              value={rascunho.casa} onChange={(e) => mudar('casa', e.target.value)}
            />
          </div>
          <p className={css.respiroDica}>
            Pedido com item das duas linhas ocupa vaga nas duas. Ajuste os números até bater com o
            tempo real da cozinha; ninguém decidiu o valor certo ainda.
          </p>
        </div>
      )}

      {erro && <p className={css.aviso} role="alert"><Icone nome="alerta" /> {erro}</p>}
      <div className={css.acoesEdicao}>
        <Botao variante="primario" onClick={confirmar}>{rotuloSalvar}</Botao>
        <Botao onClick={aoCancelar}>Cancelar</Botao>
      </div>
    </div>
  )
}

function CartaoJanela({ janela, ocupacao, temPedidos, aoSalvar, aoPausar, aoReativar, aoExcluir }) {
  const [editando, setEditando] = useState(false)
  const pausada = janela.ativa === false

  function salvar(campos) {
    aoSalvar(campos)
    setEditando(false)
  }

  return (
    <li className={`${css.item} ${pausada ? css.pausada : ''}`}>
      <div className={css.linhaItem}>
        <span className={css.identidade}>
          {pausada && <Pilula tom="neutro" icone="pause">Pausada</Pilula>}
          <strong>{janela.faixa}</strong>
        </span>
        <div className={css.acoesTopo}>
          <Botao variante="texto" icone="lapis" aria-expanded={editando} onClick={() => setEditando((v) => !v)}>
            {editando ? 'Fechar' : 'Editar'}
          </Botao>
          {pausada ? (
            <Botao variante="texto" icone="play" onClick={aoReativar}>Reativar</Botao>
          ) : (
            <Botao variante="texto" icone="pause" onClick={aoPausar}>Pausar</Botao>
          )}
          <Botao
            variante="texto"
            icone="x"
            disabled={temPedidos}
            title={temPedidos ? 'Tem pedido marcado nesta janela hoje. Pause em vez de excluir.' : undefined}
            onClick={aoExcluir}
          >
            Excluir
          </Botao>
        </div>
      </div>

      <p className={css.metaLinha}>
        <span>{janela.capacidade} pedidos de capacidade</span>
        <span>corte {janela.corteMinutos ?? MINUTOS_DE_CORTE} min antes</span>
        <span>{rotuloDosDias(janela.diasSemana)}</span>
        {janela.capacidadePorLinha && (
          <span className={css.proposta}>
            por linha: servir {janela.capacidadePorLinha.servir} · casa {janela.capacidadePorLinha.casa}
          </span>
        )}
      </p>
      {ocupacao && <p className={css.metaLinha}>Hoje: {rotuloDaOcupacao(ocupacao)}</p>}

      {editando && (
        <FormularioJanela
          valorInicial={camposIniciais(janela)}
          rotuloSalvar="Salvar"
          aoSalvar={salvar}
          aoCancelar={() => setEditando(false)}
        />
      )}
    </li>
  )
}

// Respiro mínimo (RN-22): ajustável, mas nunca abaixo do chão de 40 minutos
// que a regra escrita pede. `ajustarRespiroMinimo` (dominio/entrega.js) trava
// isso mesmo se o campo mandar menos.
function RespiroMinimo({ valor, aoAjustar }) {
  const [rascunho, setRascunho] = useState(String(valor))
  const abaixoDoPiso = Number(rascunho) < RESPIRO_MINIMO_RN22

  return (
    <div className={css.respiro}>
      <label htmlFor="janelas-respiro">Respiro mínimo prometido ao cliente</label>
      <input
        id="janelas-respiro"
        className={css.respiroCampo}
        type="number"
        min={RESPIRO_MINIMO_RN22}
        step="5"
        value={rascunho}
        onChange={(e) => setRascunho(e.target.value)}
        onBlur={() => {
          // Eco local com a MESMA trava do reducer (`ajustarRespiroMinimo`):
          // sem isto, digitar 10 e sair do campo travava o valor guardado em
          // 40, mas a caixa continuava mostrando 10, prometendo ao olho o
          // que a regra não deixa acontecer.
          const travado = ajustarRespiroMinimo(Number(rascunho))
          setRascunho(String(travado))
          aoAjustar(travado)
        }}
      />
      <span>min</span>
      <p className={css.respiroDica}>
        O cliente nunca recebe prazo com menos de {RESPIRO_MINIMO_RN22} min entre o pedido e a faixa prometida.
        {abaixoDoPiso && ' O valor digitado fica travado em ' + RESPIRO_MINIMO_RN22 + ' ao salvar.'}
      </p>
    </div>
  )
}

export function AbaJanelas() {
  const { janelas, respiroMinutos, cardapio } = useCatalogo()
  const { conversas, agora } = useAtendimento()
  const {
    criarJanela, editarJanela, pausarJanela, reativarJanela, excluirJanela, ajustarRespiroMinimo,
  } = useAcoes()
  const [criando, setCriando] = useState(false)

  return (
    <div>
      <RespiroMinimo valor={respiroMinutos ?? RESPIRO_MINIMO_RN22} aoAjustar={ajustarRespiroMinimo} />

      <div className={css.barra}>
        <Botao
          variante="primario"
          icone="plus"
          className={css.toqueAcao}
          aria-expanded={criando}
          onClick={() => setCriando((v) => !v)}
        >
          Nova janela
        </Botao>
      </div>

      <ul className={css.lista}>
        {criando && (
          <li className={`${css.item} ${css.itemNovo}`}>
            <FormularioJanela
              valorInicial={camposIniciais(null)}
              rotuloSalvar="Cadastrar"
              aoSalvar={(campos) => { criarJanela(campos); setCriando(false) }}
              aoCancelar={() => setCriando(false)}
            />
          </li>
        )}

        {janelas.map((janela) => (
          <CartaoJanela
            key={janela.id}
            janela={janela}
            ocupacao={ocupacaoDaJanela(janela, conversas, agora, null, cardapio)}
            temPedidos={janelaTemPedidos(janela, conversas)}
            aoSalvar={(campos) => editarJanela(janela.id, campos)}
            aoPausar={() => pausarJanela(janela.id)}
            aoReativar={() => reativarJanela(janela.id)}
            aoExcluir={() => excluirJanela(janela.id)}
          />
        ))}

        {!criando && janelas.length === 0 && (
          <li>
            <Vazio
              titulo="Nenhuma janela cadastrada"
              acao={<Botao variante="primario" icone="plus" onClick={() => setCriando(true)}>Nova janela</Botao>}
            >
              Sem janela nenhuma, a ficha e o agente não têm horário para oferecer.
            </Vazio>
          </li>
        )}
      </ul>
    </div>
  )
}
