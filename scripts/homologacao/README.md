# Bateria de homologação do atendimento (#1318)

Simula clientes reais contra uma API local isolada e mede a aderência do atendimento. Nada sai para a Meta nem para o Mercado Pago de verdade.

| Peça | Função |
|---|---|
| `compose.homologacao.yml` | Sobrepõe `docker-compose.local.yml`: API na porta 18510, Meta falsa, Mercado Pago falso, agente pelo proxy. Segredos vêm do ambiente (`FW_KEY`, `META_APPSECRET`), nunca do arquivo |
| `fake_meta.py` | Graph API falsa (porta 18620): grava envios, devolve `wamid`, manda `sent`, `delivered`, `read` por webhook assinado, serve mídia. Telefone terminado em 9999 ou 8888 injeta erro |
| `fake_mp.py` | Mercado Pago falso (18630): preferência, consulta, busca, estorno; `POST /_pagar` aprova o pagamento e dispara o webhook assinado |
| `proxy_modelo.py` | Proxy do agente (18610): registra latência e tokens por chamada, sem gravar cabeçalhos |
| `sim.py` | Cliente WhatsApp (`wa`, `btn`, `img`) e chat do site (`site`) |
| `bateria1.py`, `bateria2.py`, `seg.py`, `fluxo_pedido.py` | Cenários de cliente, casos difíceis, segurança do webhook, pedido completo |
| `matriz.py`, `render.py`, `spec_corpo.md` | Matriz de cenários, nota de aderência e geração de `docs/plan/atendimento-whatsapp/13-homologacao-organica.md` |

Pré-requisitos: Docker, Python 3.12, o segredo de teste do app da Meta em `~/.easystok/meta-appsecret-teste-local` e a chave do modelo em `FW_KEY`. Suba o Worker junto (sem ele o outbox não anda). O agente é estocástico: para aceite, repita 5 rodadas.
