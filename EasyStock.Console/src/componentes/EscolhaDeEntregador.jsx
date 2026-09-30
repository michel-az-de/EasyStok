import { useState } from 'react'
import { Botao } from './Botao'
import { Chip } from './Chip'
import { CampoSelecao, CampoTexto } from './Campo'
import css from './EscolhaDeEntregador.module.css'

// US-040: pergunta quem leva só quando o despacho ainda não sabe (ficha,
// cozinha ou o cartão solto de Entregas; `dominio/viagem.js:
// entregadorResolvido` decide se isto precisa aparecer).
//
// Rodada 12 (issue #17, feedback da Thatiane): o entregador vira registro com
// nome, veículo, placa e empresa (própria, 99, Lalamove, iFood Entregas),
// preenchido à mão. Três jeitos de responder: ela mesma leva; um toque num
// entregador já conhecido (cadastrado ou digitado hoje, com a placa no rótulo
// para separar "José" de "José"); ou "Outro entregador", com os quatro campos.
// Nenhuma plataforma está ligada: o aviso diz isso na própria tela.
//
// Componente não importa `dominio` (`ferramentas/verificar-camadas.mjs`): a
// lista de empresas e veículos, os conhecidos, a validação e a montagem do
// registro chegam por `dominio/despacho.js: opcoesDoDespacho`, que cada tela
// que despacha espalha aqui.
export function EscolhaDeEntregador({
  aoEscolher, pergunta = 'Quem vai entregar, para avisar o cliente?',
  empresas = [], veiculos = [], conhecidos = [], validar = () => null, montar, aviso,
}) {
  const [outroAberto, setOutroAberto] = useState(conhecidos.length === 0)
  const [dados, setDados] = useState({
    nome: '', veiculo: veiculos[0]?.valor ?? 'moto', placa: '', empresa: empresas[0]?.valor ?? 'propria',
  })
  const [tentou, setTentou] = useState(false)
  const motivo = validar(dados)

  const mudar = (campo) => (e) => setDados((atual) => ({ ...atual, [campo]: e.target.value }))

  const confirmar = () => {
    setTentou(true)
    if (motivo) return
    aoEscolher(montar ? montar(dados) : { tipo: 'motoboy', nome: dados.nome.trim() })
  }
  const aoTeclar = (e) => { if (e.key === 'Enter') confirmar() }

  return (
    <div className={css.escolha}>
      <p className={css.pergunta}>{pergunta}</p>
      <div className={css.opcoes}>
        <Chip papel="escolha" onClick={() => aoEscolher({ tipo: 'propria' })}>
          Eu mesma levo
        </Chip>
        {conhecidos.map((c) => (
          <Chip key={c.chave} papel="escolha" onClick={() => aoEscolher(c.entregador)}>
            {c.rotulo}
          </Chip>
        ))}
        <Chip papel="filtro" ativo={outroAberto} onClick={() => setOutroAberto((v) => !v)}>
          Outro entregador
        </Chip>
      </div>

      {outroAberto && (
        <div className={css.formulario}>
          <div className={css.campos}>
            <CampoTexto
              rotulo="Nome" placeholder="Nome de quem leva" value={dados.nome}
              onChange={mudar('nome')} onKeyDown={aoTeclar}
            />
            <CampoSelecao rotulo="Veículo" opcoes={veiculos} value={dados.veiculo} onChange={mudar('veiculo')} />
            <CampoTexto
              rotulo="Placa" placeholder="ABC1D23" value={dados.placa} maxLength={8} autoCapitalize="characters"
              onChange={mudar('placa')} onKeyDown={aoTeclar}
            />
            <CampoSelecao rotulo="Empresa" opcoes={empresas} value={dados.empresa} onChange={mudar('empresa')} />
          </div>
          {tentou && motivo && <p className={css.motivo} role="alert">{motivo}</p>}
          <div className={css.rodape}>
            <Botao variante="primario" onClick={confirmar}>Confirmar entregador</Botao>
          </div>
          {aviso && <p className={css.aviso}>{aviso}</p>}
        </div>
      )}
    </div>
  )
}
