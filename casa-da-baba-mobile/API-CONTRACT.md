# API Contract — Casa da Baba Mobile

Duas rotas REST sob `/api/mobile/sync/`. Ambas usam JSON.

---

## POST /api/mobile/sync

Recebe uma batch de mutations do PWA e aplica no banco.

### Request

```json
{
  "deviceId": "dev-abc123-xyz456",
  "mutations": [
    {
      "id": "m-1714070400000-a1b2c",
      "deviceId": "dev-abc123-xyz456",
      "type": "order.upsert",
      "payload": { ... },
      "ts": 1714070400000
    }
  ]
}
```

### Response (200)

```json
{
  "acceptedIds": ["m-1714070400000-a1b2c"],
  "rejected": null
}
```

Ou em caso de conflito parcial:

```json
{
  "acceptedIds": ["m-1"],
  "rejected": [
    {"mutationId": "m-2", "reason": "conflict: version mismatch"}
  ]
}
```

---

## GET /api/mobile/sync/pull?since={ms}&deviceId={id}

Retorna mutations feitas no servidor (por outros devices ou pela UI web do EasyStock) após o timestamp `since`, excluindo as feitas pelo próprio `deviceId` que está pedindo (evita eco).

### Response (200)

```json
{
  "serverTime": 1714070500000,
  "mutations": [
    {
      "id": "<guid-gerado>",
      "deviceId": "outro-device",
      "type": "product.upsert",
      "payload": { ... },
      "ts": 1714070450000
    }
  ]
}
```

O cliente atualiza `lastFullSync` com `serverTime` pra próxima chamada.

---

## Tipos de Mutation

### product.upsert

Criar ou atualizar produto do catálogo.

```json
{
  "id": "lasanha",
  "name": "Lasanha Bolonhesa",
  "emoji": "🍲",
  "category": "massa",
  "unit": "300g",
  "price": 30.00,
  "stock": 5,
  "custom": false
}
```

Categoria: `"massa" | "molho" | "extra"`.

### client.upsert

Criar ou atualizar cliente.

```json
{
  "id": "c-1714070400000",
  "name": "Fernanda",
  "apt": "506",
  "address": null,
  "phone": "(11) 99999-0000",
  "lastOrder": 1714070400000,
  "orderCount": 3
}
```

`apt` XOR `address` (nunca os dois; morador do prédio tem apt, externo tem address).

### order.upsert

Criar pedido novo ou atualizar status/itens de existente.

```json
{
  "id": "o-1714070400000",
  "clientId": "c1",
  "clientSnapshot": { "name": "Lurde", "ref": "apto 204" },
  "items": [
    {
      "productId": "lasanha",
      "name": "Lasanha Bolonhesa",
      "emoji": "🍲",
      "unit": "300g",
      "qty": 2,
      "unitPrice": 30.00
    }
  ],
  "notes": "pra gratinar, retira 19h",
  "total": 60.00,
  "status": "aguardando",
  "createdAt": 1714070400000,
  "updatedAt": 1714070400000
}
```

Estados possíveis: `"aguardando" | "preparando" | "pronto" | "entregue" | "cancelado"`.

**Regras de estoque aplicadas pelo backend**:
- Transição para `pronto` (vindo de aguardando/preparando): decrementa `product.stock` pelos qty dos items
- Transição para `cancelado` (vindo de pronto/entregue): devolve ao estoque
- Demais transições: não mexem em estoque

`clientId = null` indica pedido avulso (não cadastrado).

### batch.upsert

Lote de produção. **Imutável** — re-envio é ignorado (idempotente).

```json
{
  "id": "b-1714070400000",
  "code": "LASA-220426-001",
  "items": [
    {
      "productId": "lasanha",
      "name": "Lasanha Bolonhesa",
      "emoji": "🍲",
      "unit": "300g",
      "qty": 4,
      "photo": "data:image/jpeg;base64,..."
    }
  ],
  "batchPhoto": "data:image/jpeg;base64,...",
  "createdAt": 1714070400000
}
```

**Regra automática**: criar batch **incrementa** `product.stock` pelos qty de cada item.

### cashEntry.upsert

Lançamento de caixa. **Imutável**.

```json
{
  "id": "cash-1714070400000",
  "type": "expense",
  "amount": 45.00,
  "description": "Farinha 00, 5kg",
  "createdAt": 1714070400000
}
```

Tipo: `"expense" | "income"`.

---

## Convenções

- **Ids**: string. Cliente gera no formato `<prefix>-<timestamp>[-<random>]`. Servidor aceita como-está.
- **Timestamps**: Unix epoch em milissegundos (not seconds, not ISO 8601).
- **Valores monetários**: `decimal` (2 casas).
- **Device Id**: gerado no primeiro boot, persiste em localStorage, enviado em todo request no header `X-Device-Id` e no body.

---

## Strategy: Last-Write-Wins

Se 2 devices editam a mesma entidade simultaneamente, quem chegou por último no backend vence. Não há vector clocks, não há versionamento otimista, não há UI de resolução de conflito. Para o caso de uso (2 pessoas na mesma cozinha, raramente editando a mesma coisa), é suficiente.

Auditoria preserva quem fez cada mutação via `last_device_id` na tabela.

---

## Segurança

Nenhuma autenticação. Uso pessoal em rede local. Se for expor via internet, adicione:
- HTTPS obrigatório (service worker exige)
- API key simples no header (`X-Api-Key`) ou OAuth2 se quiser robusto
- Rate limit no `/sync` pra evitar spam
