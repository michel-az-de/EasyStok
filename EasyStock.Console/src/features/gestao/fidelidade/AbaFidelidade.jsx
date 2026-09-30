// Aba Fidelidade e cupons (rodada 13, issue #45, registro 107). Lugar único
// de configuração (Aceite da issue: "Configuração num lugar só"): cupons,
// regra de pontos, catálogo de recompensas e sorteios. O dia a dia (saldo,
// resgate, aplicar cupom) mora na ficha/Cobrança, não aqui — ver
// `features/ficha-cliente/BlocoCliente.jsx`, `BlocoCobranca.jsx` e
// `ModalFidelidade.jsx`.
import { useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../../componentes/Campo'
import { Icone } from '../../../componentes/Icone'
import { Pilula } from '../../../componentes/Pilula'
import { Vazio } from '../../../componentes/Vazio'
import { useAcoes, useAtendimento, useCatalogo } from '../../../aplicacao/contextos'
import { TIPOS_RECOMPENSA } from '../../../dominio/fidelidade'
import { moeda } from '../../../dominio/formato'
import css from './AbaFidelidade.module.css'

function Secao({ titulo, descricao, children, acao }) {
  return (
    <section className={css.secao}>
      <header className={css.cabecalhoSecao}>
        <div>
          <h4>{titulo}</h4>
          {descricao && <p>{descricao}</p>}
        </div>
        {acao}
      </header>
      {children}
    </section>
  )
}

// ---------------------------------------------------------------------------
// Cupons.
// ---------------------------------------------------------------------------

const CUPOM_VAZIO = {
  codigo: '', tipoDesconto: 'percentual', valor: '', validade: '', limiteUsos: '', minimoPedido: '',
}

function FormCupom({ aoSalvar, aoCancelar }) {
  const [form, setForm] = useState(CUPOM_VAZIO)
  const valido = form.codigo.trim().length >= 3 && Number(form.valor) > 0

  return (
    <div className={css.formLinha}>
      <CampoTexto rotulo="Código" placeholder="Ex.: BEMVINDA10" value={form.codigo}
        onChange={(e) => setForm((f) => ({ ...f, codigo: e.target.value }))} />
      <CampoSelecao
        rotulo="Tipo"
        opcoes={[{ valor: 'percentual', rotulo: '% percentual' }, { valor: 'valor', rotulo: 'R$ valor fixo' }]}
        value={form.tipoDesconto}
        onChange={(e) => setForm((f) => ({ ...f, tipoDesconto: e.target.value }))}
      />
      <CampoTexto rotulo="Valor" tipo="number" value={form.valor}
        onChange={(e) => setForm((f) => ({ ...f, valor: e.target.value }))} />
      <CampoTexto rotulo="Validade" tipo="date" value={form.validade}
        onChange={(e) => setForm((f) => ({ ...f, validade: e.target.value }))} />
      <CampoTexto rotulo="Limite de usos" tipo="number" placeholder="Sem limite" value={form.limiteUsos}
        onChange={(e) => setForm((f) => ({ ...f, limiteUsos: e.target.value }))} />
      <CampoTexto rotulo="Pedido mínimo (R$)" tipo="number" value={form.minimoPedido}
        onChange={(e) => setForm((f) => ({ ...f, minimoPedido: e.target.value }))} />
      <div className={css.acoesForm}>
        <Botao onClick={aoCancelar}>Cancelar</Botao>
        <Botao
          variante="primario"
          disabled={!valido}
          onClick={() => aoSalvar({
            ...form,
            validade: form.validade || null,
            limiteUsos: form.limiteUsos === '' ? null : form.limiteUsos,
          })}
        >
          Salvar cupom
        </Botao>
      </div>
    </div>
  )
}

function ListaCupons({ cupons, aoAlternarAtivo }) {
  if (cupons.length === 0) return <Vazio titulo="Nenhum cupom cadastrado">Crie o primeiro cupom acima.</Vazio>
  return (
    <ul className={css.lista}>
      {cupons.map((cupom) => (
        <li key={cupom.id} className={css.linha}>
          <span className={css.codigoCupom}>{cupom.codigo}</span>
          <span className={css.detalheLinha}>
            {cupom.tipoDesconto === 'percentual' ? `${cupom.valor}%` : moeda(cupom.valor)}
            {cupom.minimoPedido > 0 && ` · mín. ${moeda(cupom.minimoPedido)}`}
            {cupom.validade && ` · até ${cupom.validade.split('-').reverse().join('/')}`}
            {' · '}{cupom.usos}{cupom.limiteUsos != null ? `/${cupom.limiteUsos}` : ''} usos
          </span>
          <Pilula tom={cupom.ativo ? 'ok' : 'neutro'} fina>{cupom.ativo ? 'Ativo' : 'Inativo'}</Pilula>
          <Botao variante="texto" onClick={() => aoAlternarAtivo(cupom.id)}>
            {cupom.ativo ? 'Desativar' : 'Ativar'}
          </Botao>
        </li>
      ))}
    </ul>
  )
}

// ---------------------------------------------------------------------------
// Regra de fidelidade (proposta ajustável, ver dominio/fidelidade.js).
// ---------------------------------------------------------------------------

function RegraFidelidade({ config, aoSalvar }) {
  const [rascunho, setRascunho] = useState(config)
  const mudou = JSON.stringify(rascunho) !== JSON.stringify(config)

  return (
    <div className={css.formLinha}>
      <CampoSelecao
        rotulo="Como pontua"
        opcoes={[
          { valor: 'valor', rotulo: 'Ponto por real gasto' },
          { valor: 'pedidos', rotulo: 'Ponto a cada N pedidos' },
        ]}
        value={rascunho.tipo}
        onChange={(e) => setRascunho((r) => ({ ...r, tipo: e.target.value }))}
      />
      {rascunho.tipo === 'valor' ? (
        <>
          <CampoTexto rotulo="A cada R$" tipo="number" value={rascunho.valorPorPonto}
            onChange={(e) => setRascunho((r) => ({ ...r, valorPorPonto: Number(e.target.value) || 0 }))} />
          <CampoTexto rotulo="Ganha (pontos)" tipo="number" value={rascunho.pontosPorFaixa}
            onChange={(e) => setRascunho((r) => ({ ...r, pontosPorFaixa: Number(e.target.value) || 0 }))} />
        </>
      ) : (
        <>
          <CampoTexto rotulo="A cada X pedidos" tipo="number" value={rascunho.pedidosPorPonto}
            onChange={(e) => setRascunho((r) => ({ ...r, pedidosPorPonto: Number(e.target.value) || 0 }))} />
          <CampoTexto rotulo="Ganha (pontos)" tipo="number" value={rascunho.pontosPorPedidos}
            onChange={(e) => setRascunho((r) => ({ ...r, pontosPorPedidos: Number(e.target.value) || 0 }))} />
        </>
      )}
      <div className={css.acoesForm}>
        <Botao variante="primario" disabled={!mudou} onClick={() => aoSalvar(rascunho)}>Salvar regra</Botao>
      </div>
      <p className={css.notaProposta}>
        <Icone nome="dica" /> Só conta pedido com pagamento confirmado.
        Os números acima são um ponto de partida: ajuste quando quiser.
      </p>
    </div>
  )
}

// ---------------------------------------------------------------------------
// Catálogo de recompensas.
// ---------------------------------------------------------------------------

const RECOMPENSA_VAZIA = { tipo: 'produto', rotulo: '', custoPontos: '', skuProduto: '', sorteioId: '' }

function FormRecompensa({ cardapio, sorteios, aoSalvar, aoCancelar }) {
  const [form, setForm] = useState(RECOMPENSA_VAZIA)
  const precisaSorteio = form.tipo === 'sorteio'
  const valido = form.rotulo.trim() && Number(form.custoPontos) > 0
    && (!precisaSorteio || form.sorteioId)

  return (
    <div className={css.formLinha}>
      <CampoSelecao rotulo="Tipo" opcoes={TIPOS_RECOMPENSA.map((t) => ({ valor: t.valor, rotulo: t.rotulo }))}
        value={form.tipo} onChange={(e) => setForm((f) => ({ ...f, tipo: e.target.value }))} />
      <CampoTexto rotulo="Nome da recompensa" value={form.rotulo}
        onChange={(e) => setForm((f) => ({ ...f, rotulo: e.target.value }))} />
      <CampoTexto rotulo="Custo em pontos" tipo="number" value={form.custoPontos}
        onChange={(e) => setForm((f) => ({ ...f, custoPontos: e.target.value }))} />
      {form.tipo === 'produto' && (
        <CampoSelecao
          rotulo="Produto do cardápio"
          opcoes={[{ valor: '', rotulo: 'Escolha o produto' }, ...cardapio.map((p) => ({ valor: p.sku, rotulo: p.nome }))]}
          value={form.skuProduto}
          onChange={(e) => setForm((f) => ({ ...f, skuProduto: e.target.value }))}
        />
      )}
      {precisaSorteio && (
        <CampoSelecao
          rotulo="Sorteio"
          opcoes={[{ valor: '', rotulo: 'Escolha o sorteio' }, ...sorteios.map((s) => ({ valor: s.id, rotulo: s.nome }))]}
          value={form.sorteioId}
          onChange={(e) => setForm((f) => ({ ...f, sorteioId: e.target.value }))}
        />
      )}
      <div className={css.acoesForm}>
        <Botao onClick={aoCancelar}>Cancelar</Botao>
        <Botao variante="primario" disabled={!valido} onClick={() => aoSalvar(form)}>Salvar recompensa</Botao>
      </div>
    </div>
  )
}

const ICONE_POR_TIPO = { produto: 'package', 'frete-gratis': 'moto', sorteio: 'estrela' }

function ListaRecompensas({ recompensas, aoAlternarAtivo }) {
  if (recompensas.length === 0) return <Vazio titulo="Nenhuma recompensa cadastrada">Crie a primeira acima.</Vazio>
  return (
    <ul className={css.lista}>
      {recompensas.map((r) => (
        <li key={r.id} className={css.linha}>
          <Icone nome={ICONE_POR_TIPO[r.tipo] ?? 'presente'} tamanho={16} />
          <span className={css.detalheLinha}>{r.rotulo} · {r.custoPontos} pts</span>
          <Pilula tom={r.ativo ? 'ok' : 'neutro'} fina>{r.ativo ? 'Ativa' : 'Inativa'}</Pilula>
          <Botao variante="texto" onClick={() => aoAlternarAtivo(r.id)}>
            {r.ativo ? 'Desativar' : 'Ativar'}
          </Botao>
        </li>
      ))}
    </ul>
  )
}

// ---------------------------------------------------------------------------
// Sorteios com lista de participantes.
// ---------------------------------------------------------------------------

function FormSorteio({ aoSalvar, aoCancelar }) {
  const [form, setForm] = useState({ nome: '', dataSorteio: '' })
  return (
    <div className={css.formLinha}>
      <CampoTexto rotulo="Nome do sorteio" value={form.nome}
        onChange={(e) => setForm((f) => ({ ...f, nome: e.target.value }))} />
      <CampoTexto rotulo="Data do sorteio" tipo="date" value={form.dataSorteio}
        onChange={(e) => setForm((f) => ({ ...f, dataSorteio: e.target.value }))} />
      <div className={css.acoesForm}>
        <Botao onClick={aoCancelar}>Cancelar</Botao>
        <Botao variante="primario" disabled={!form.nome.trim()} onClick={() => aoSalvar({ ...form, dataSorteio: form.dataSorteio || null })}>
          Salvar sorteio
        </Botao>
      </div>
    </div>
  )
}

function ListaSorteios({ sorteios }) {
  const [aberto, setAberto] = useState(null)
  if (sorteios.length === 0) return <Vazio titulo="Nenhum sorteio cadastrado">Crie o primeiro acima.</Vazio>
  return (
    <ul className={css.lista}>
      {sorteios.map((s) => (
        <li key={s.id} className={css.linhaSorteio}>
          <button type="button" className={css.linha} onClick={() => setAberto(aberto === s.id ? null : s.id)}>
            <Icone nome={aberto === s.id ? 'chevron-up' : 'chevron-right'} />
            <span className={css.detalheLinha}>
              {s.nome}{s.dataSorteio && ` · ${s.dataSorteio.split('-').reverse().join('/')}`}
            </span>
            <Pilula tom="neutro" fina>{s.participantes.length} participantes</Pilula>
          </button>
          {aberto === s.id && (
            s.participantes.length === 0 ? (
              <p className={css.semParticipantes}>Ninguém resgatou número da sorte ainda.</p>
            ) : (
              <ol className={css.listaParticipantes}>
                {s.participantes.map((p) => (
                  <li key={`${p.cadastroId}-${p.numero}`}>
                    <b>Nº {p.numero}</b> · {p.nome}
                  </li>
                ))}
              </ol>
            )
          )}
        </li>
      ))}
    </ul>
  )
}

// ---------------------------------------------------------------------------
// Aba.
// ---------------------------------------------------------------------------

export function AbaFidelidade() {
  const { fidelidade } = useAtendimento()
  const { cardapio } = useCatalogo()
  const {
    criarCupom, alternarCupomAtivo, editarRegraFidelidade, criarRecompensa, alternarRecompensaAtiva, criarSorteio,
  } = useAcoes()
  const [formAberto, setFormAberto] = useState(null)

  return (
    <div className={css.raiz}>
      <Secao
        titulo="Cupons"
        descricao="Código, percentual ou valor, validade, limite de usos e pedido mínimo."
        acao={formAberto !== 'cupom' && (
          <Botao icone="plus" onClick={() => setFormAberto('cupom')}>Novo cupom</Botao>
        )}
      >
        {formAberto === 'cupom' && (
          <FormCupom
            aoCancelar={() => setFormAberto(null)}
            aoSalvar={(dados) => { criarCupom(dados); setFormAberto(null) }}
          />
        )}
        <ListaCupons cupons={fidelidade.cupons} aoAlternarAtivo={alternarCupomAtivo} />
      </Secao>

      <Secao
        titulo="Regra de fidelidade"
        descricao="A cada pedido pago, o cliente ganha ponto. Ajuste a conta abaixo."
      >
        <RegraFidelidade config={fidelidade.config} aoSalvar={editarRegraFidelidade} />
      </Secao>

      <Secao
        titulo="Catálogo de recompensas"
        descricao="Produto do cardápio, frete grátis ou número da sorte num sorteio, cada um com custo em pontos."
        acao={formAberto !== 'recompensa' && (
          <Botao icone="plus" onClick={() => setFormAberto('recompensa')}>Nova recompensa</Botao>
        )}
      >
        {formAberto === 'recompensa' && (
          <FormRecompensa
            cardapio={cardapio}
            sorteios={fidelidade.sorteios}
            aoCancelar={() => setFormAberto(null)}
            aoSalvar={(dados) => { criarRecompensa(dados); setFormAberto(null) }}
          />
        )}
        <ListaRecompensas recompensas={fidelidade.recompensas} aoAlternarAtivo={alternarRecompensaAtiva} />
      </Secao>

      <Secao
        titulo="Sorteios"
        descricao="Quem resgatou número da sorte entra na lista de participantes."
        acao={formAberto !== 'sorteio' && (
          <Botao icone="plus" onClick={() => setFormAberto('sorteio')}>Novo sorteio</Botao>
        )}
      >
        {formAberto === 'sorteio' && (
          <FormSorteio
            aoCancelar={() => setFormAberto(null)}
            aoSalvar={(dados) => { criarSorteio(dados); setFormAberto(null) }}
          />
        )}
        <ListaSorteios sorteios={fidelidade.sorteios} />
      </Secao>
    </div>
  )
}
