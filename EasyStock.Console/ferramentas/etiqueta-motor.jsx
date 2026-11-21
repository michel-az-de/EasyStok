// Fixture de UI da #2247 (impressão v3, G1 item 7): a prévia das etiquetas com o motor de verdade,
// sem API nem impressora. Abra /ferramentas/etiqueta-motor.html no `npm run dev`.
import React, { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { PreviaDasEtiquetas } from '../src/features/gestao/producao/etiqueta/PreviaDasEtiquetas'
import '../src/estilos/tokens.css'
import '../src/estilos/base.css'

const etiqueta = (i, extra = {}) => ({
  id: `e-${i}`, sequencial: i, codigo: `LOT-261105-001-000${i}`, nome: 'Lasanha Bolonhesa Clássica 1 kg', pesoG: 1000,
  alergenos: [], lote: 'LOT-261105-001', produzidoEm: '2026-11-05T15:00:00Z', validadeEm: '2026-11-10T15:00:00Z', ...extra,
})
const CENARIOS = {
  simples: [etiqueta(1), etiqueta(2), etiqueta(3)],
  alergenos: [etiqueta(1, { alergenos: ['gluten', 'lactose', 'ovo'] }), etiqueta(2, { alergenos: ['gluten', 'lactose', 'ovo'] })],
  longo: [etiqueta(1, { nome: 'Escondidinho de carne seca com mandioca e queijo coalho gratinado 1 kg', alergenos: ['lactose', 'gluten', 'soja', 'castanhas', 'crustaceos', 'peixe', 'amendoim'] })],
}

function Prova() {
  const cenario = new URLSearchParams(location.search).get('c') ?? 'simples'
  const [aberta, setAberta] = useState(true)
  const folha = { empresa: 'Casa da Baba', etiquetas: CENARIOS[cenario] ?? CENARIOS.simples }
  return (
    <main style={{ padding: 24 }}>
      <p>Cenários: {Object.keys(CENARIOS).map((c) => <a key={c} href={`?c=${c}`} style={{ marginRight: 12 }}>{c}</a>)}</p>
      <button type="button" onClick={() => setAberta(true)}>Abrir a prévia</button>
      {aberta && <PreviaDasEtiquetas loteId="l-1" codigo="LOT-261105-001" quantas={folha.etiquetas.length}
        impressao={{ loteId: 'l-1', folha }} aoFechar={() => setAberta(false)} />}
    </main>
  )
}

createRoot(document.getElementById('root')).render(<Prova />)
