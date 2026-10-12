import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './estilos/base.css'
import { App } from './app/App'
import { AtualizacaoDoConsole } from './app/AtualizacaoDoConsole'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <AtualizacaoDoConsole />
    <App />
  </StrictMode>,
)
