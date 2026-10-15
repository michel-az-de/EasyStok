// Fixture de UI da #1839 pacote D: as oito ilustrações do hall no claro e no escuro, para aprovação.
import React from 'react'
import { createRoot } from 'react-dom/client'
import { IlustracaoDoModulo } from '../src/features/hall/IlustracaoDoModulo'
import { MODULOS } from '../src/dominio/modulos'
import '../src/estilos/tokens.css'
import '../src/estilos/base.css'

const cores = { '--il-fundo': 'var(--marca-claro)', '--il-tinta': 'var(--tinta)', '--il-ouro': 'var(--trigo)', '--il-papel': 'var(--papel)', '--il-marca': 'var(--marca)', '--il-chao': 'var(--linha)' }

function Folha({ tema }) {
  return <section style={{ colorScheme: tema, background: 'var(--fundo)', color: 'var(--tinta)', padding: 24 }}>
    <ul style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 16, margin: 0, padding: 0, listStyle: 'none' }}>
      {MODULOS.map(({ id, nome }) => <li key={id} style={{ ...cores, background: 'var(--papel)', border: '1px solid var(--linha)', borderRadius: 12, padding: 16 }}>
        <IlustracaoDoModulo id={id} className="il" />
        <strong style={{ display: 'block', fontSize: 20, marginTop: 8 }}>{nome}</strong>
      </li>)}
    </ul>
  </section>
}

createRoot(document.getElementById('root')).render(<>
  <style>{'.il{display:block;width:100%;height:auto;max-height:140px}'}</style>
  <Folha tema="light" /><Folha tema="dark" />
</>)
