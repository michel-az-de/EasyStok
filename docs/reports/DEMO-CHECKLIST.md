# EasyStok — Checklist de Demo

**Última atualização:** 2026-04-11

## Pré-Requisitos

- [ ] API rodando (`dotnet run --project EasyStock.Api`)
- [ ] Web rodando (`dotnet run --project EasyStock.Web`)
- [ ] Banco PostgreSQL acessível (ou SQLite para demo local)
- [ ] Seed executado (automático no startup)
- [ ] Verificar `/health` da API retornando 200

## Credenciais Demo

| Usuário | Email | Senha | Perfil |
|---|---|---|---|
| Felipe Azevedo | admin do seed | `Admin@2026!Secure` | Admin |
| Thatiane Preschlak | gerente do seed | `Thati@2026!Gerente` | Gerente |
| Operador Fone | operador do seed | `OpFone@2026!Access` | Operador |
| Operador Cantina | operador do seed | `OpCantina@2026!Access` | Operador |

## Roteiro de Demo (15-20 min)

### 1. Login e Contexto (2 min)
- [ ] Fazer login com Admin
- [ ] Mostrar seleção de loja (Clube do Fone / Cantina da Thati)
- [ ] Selecionar "Clube do Fone"
- [ ] Verificar que o contexto aparece no layout

### 2. Dashboard (2 min)
- [ ] Mostrar KPIs principais
- [ ] Mostrar gráficos/métricas
- [ ] Destacar que dados são por loja selecionada

### 3. Produtos (3 min)
- [ ] Listar produtos da loja
- [ ] Abrir detalhe de um produto
- [ ] Mostrar variações (se existirem)
- [ ] Criar produto novo (demonstrar formulário)
- [ ] Mostrar categorias

### 4. Estoque (3 min)
- [ ] Listar itens em estoque
- [ ] Usar busca inteligente
- [ ] Abrir detalhe de item (quantidade, lote, validade)
- [ ] Mostrar indicadores visuais (OK, Warning, Critical)

### 5. Entradas e Saídas (3 min)
- [ ] Registrar uma entrada de estoque
- [ ] Verificar que quantidade atualizou
- [ ] Registrar uma saída
- [ ] Verificar histórico de movimentações

### 6. Fornecedores (2 min)
- [ ] Listar fornecedores
- [ ] Abrir detalhe com estatísticas
- [ ] Mostrar pedidos abertos

### 7. Inteligência (2 min)
- [ ] Mostrar alertas de estoque baixo
- [ ] Mostrar itens próximos do vencimento
- [ ] Mostrar sugestão de reposição

### 8. Multi-loja (1 min)
- [ ] Trocar para "Cantina da Thati"
- [ ] Mostrar que dados mudam
- [ ] Voltar para "Clube do Fone"

### 9. Administração (1 min)
- [ ] Mostrar listagem de usuários
- [ ] Mostrar perfis/permissões
- [ ] Mostrar configurações da loja

### 10. Notificações (1 min)
- [ ] Mostrar centro de notificações
- [ ] Mostrar badge de não lidas
- [ ] Marcar como lida

## Pontos de Atenção na Demo

⚠️ **Evitar mostrar:**
- Módulo de Assinatura (incompleto, sem pagamento)
- Anúncios IA se API key não configurada
- Swagger em demo para público não-técnico

⚠️ **Ter cuidado com:**
- Analytics pode parecer vazio se seed não tiver movimentações históricas
- Reposição sugerida depende de dados de velocidade de saída

✅ **Pontos fortes para destacar:**
- Multi-tenant (empresa + lojas)
- RBAC (Admin/Gerente/Operador)
- Busca inteligente de estoque
- Controle de validade
- Inteligência de estoque (alertas, projeções)
- Arquitetura Clean Architecture
- 257 testes automatizados passando
