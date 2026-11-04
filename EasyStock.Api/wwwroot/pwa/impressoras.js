(function (global) {
  'use strict';
  // #2202 (impressão v2, F1): várias impressoras no mesmo aparelho.
  //
  // cdb-impressoras.v2 = { versao: 2, impressoras: { [MAC]: impressora }, papeis: { etiqueta, cupom }, legado }
  // papeis diz qual impressora serve cada documento (MAC ou null). As chaves
  // antigas (cdb-printer e cdb-printers) continuam espelhadas, para a versão
  // anterior do app seguir imprimindo depois de um rollback. legado guarda o
  // texto das chaves antigas na última sincronia: se elas mudarem por fora
  // (rollback, restauração de backup), a mudança é trazida de volta.
  const CHAVE = 'cdb-impressoras.v2';
  const LEGADO = 'cdb-printer';
  const LEGADOS = 'cdb-printers';
  const REGRA = 'cdb-regra-impressao.v1';
  const PAPEIS = ['etiqueta', 'cupom'];
  const TSPL_NOMES = /^(OIA|OASIS)|TSPL|\bTSC\b|^TTP|^XP-?(3|4)\d\dB|^L42/i;
  const MODOS = { qr: ['qr'], barcode: ['barras'], both: ['qr', 'barras'], none: [] };

  function catalogo() { return global.cdbPapeis; }
  function copia(v) { return v == null ? v : JSON.parse(JSON.stringify(v)); }
  function cru(storage, k) { try { return storage.getItem(k); } catch (e) { return null; } }
  function json(texto) { try { return texto == null ? null : JSON.parse(texto); } catch (e) { return null; } }
  function positivo(v, padrao) { const n = Number(v); return Number.isFinite(n) && n > 0 ? n : padrao; }

  function linguagemPorNome(nome) { return TSPL_NOMES.test(String(nome || '')) ? 'tspl' : 'escpos'; }

  function vazio() { return { versao: 2, impressoras: {}, papeis: { etiqueta: null, cupom: null } }; }

  function midiaDoPapel(id) { return catalogo().paraMidia(id); }

  // Impressora no formato antigo -> impressora v2.
  function deLegado(p) {
    if (!p || typeof p !== 'object' || !p.address) return null;
    const manual = !!p.perfilManual;
    let linguagem = p.profile === 'oasis-pending' ? 'tspl' : p.profile;
    if (!manual && (!linguagem || linguagem === 'escpos')) linguagem = linguagemPorNome(p.name);
    if (linguagem !== 'tspl') linguagem = 'escpos';
    const e = p.etiqueta && typeof p.etiqueta === 'object' ? p.etiqueta : null;
    let midia = null;
    let confirmada = true;
    if (p.midia && typeof p.midia === 'object' && catalogo().daMidia(p.midia)) midia = catalogo().paraMidia(catalogo().daMidia(p.midia));
    else if (linguagem === 'escpos') midia = midiaDoPapel(catalogo().PADRAO_ESCPOS);
    else if (!e) { midia = midiaDoPapel(catalogo().PADRAO_TSPL); confirmada = false; }
    else if (e.continuo) midia = catalogo().paraMidia(catalogo().daMidia({ tipo: 'continuo', larguraMm: positivo(e.larguraMm, 80) }));
    else if (Number(e.larguraMm) === 80 && Number(e.alturaMm) === 40 && (e.gapMm == null || Number(e.gapMm) === 2)) {
      // P5: 80 x 40 era o valor que o select aplicava sem o operador
      // escolher. D1 manda rolo contínuo; Ajustes pede para confirmar.
      midia = midiaDoPapel(catalogo().PADRAO_TSPL);
      confirmada = false;
    } else {
      const papel = catalogo().daMidia({ tipo: 'etiqueta', larguraMm: e.larguraMm, alturaMm: e.alturaMm, gapMm: e.gapMm });
      midia = papel ? catalogo().paraMidia(papel) : midiaDoPapel(catalogo().PADRAO_TSPL);
      if (!papel) confirmada = false;
    }
    return {
      address: String(p.address),
      nome: String(p.name || p.address),
      apelido: String(p.apelido || ''),
      linguagem,
      linguagemManual: manual,
      midia,
      midiaConfirmada: p.midiaConfirmada === false ? false : confirmada,
      densidade: positivo(p.densidade, 8),
      velocidade: positivo(p.velocidade, 4),
      inverter: !!(p.inverter || (e && e.inverter)),
      calibradaEm: p.calibradaEm || null,
      offsetMm: p.offsetMm && typeof p.offsetMm === 'object' ? { x: Number(p.offsetMm.x) || 0, y: Number(p.offsetMm.y) || 0 } : { x: 0, y: 0 }
    };
  }

  // Impressora v2 -> formato antigo (o que o app anterior lê em cdb-printer).
  function paraLegado(imp) {
    if (!imp) return null;
    const m = imp.midia || {};
    const continuo = m.tipo !== 'etiqueta';
    return {
      name: imp.nome,
      address: imp.address,
      profile: imp.linguagem,
      perfilManual: !!imp.linguagemManual,
      etiqueta: continuo
        ? { larguraMm: positivo(m.larguraMm, 80), alturaMm: 0, gapMm: 0, continuo: true, inverter: !!imp.inverter }
        : { larguraMm: m.larguraMm, alturaMm: m.alturaMm, gapMm: m.gapMm, continuo: false, inverter: !!imp.inverter },
      midia: copia(m),
      midiaConfirmada: imp.midiaConfirmada !== false,
      densidade: imp.densidade,
      velocidade: imp.velocidade,
      calibradaEm: imp.calibradaEm || null,
      apelido: imp.apelido || '',
      inverter: !!imp.inverter,
      offsetMm: copia(imp.offsetMm || { x: 0, y: 0 })
    };
  }

  function valido(v) {
    if (!v || typeof v !== 'object' || v.versao !== 2 || !v.impressoras || typeof v.impressoras !== 'object') return null;
    const out = vazio();
    for (const [mac, imp] of Object.entries(v.impressoras)) {
      if (!imp || typeof imp !== 'object') continue;
      const papel = imp.midia && catalogo().daMidia(imp.midia);
      out.impressoras[mac] = Object.assign({
        address: mac, nome: mac, apelido: '', linguagem: 'escpos', linguagemManual: false, midiaConfirmada: true,
        densidade: 8, velocidade: 4, inverter: false, calibradaEm: null, offsetMm: { x: 0, y: 0 }
      }, imp, { address: mac });
      const i = out.impressoras[mac];
      if (i.linguagem !== 'tspl') i.linguagem = 'escpos';
      i.midia = papel ? catalogo().paraMidia(papel) : midiaDoPapel(i.linguagem === 'tspl' ? catalogo().PADRAO_TSPL : catalogo().PADRAO_ESCPOS);
    }
    for (const p of PAPEIS) {
      const mac = v.papeis && v.papeis[p];
      out.papeis[p] = mac && out.impressoras[mac] ? mac : null;
    }
    if (v.legado && typeof v.legado === 'object') out.legado = { printer: v.legado.printer == null ? null : String(v.legado.printer), printers: v.legado.printers == null ? null : String(v.legado.printers) };
    return out;
  }

  // Chaves antigas -> v2. cdb-printer vence o duplicado e serve aos dois papéis.
  function migrar(atual, lista) {
    const v2 = vazio();
    const todas = (Array.isArray(lista) ? lista : []).concat(atual && atual.address ? [atual] : []);
    for (const p of todas) { const imp = deLegado(p); if (imp) v2.impressoras[imp.address] = imp; }
    if (atual && atual.address && v2.impressoras[atual.address]) PAPEIS.forEach(p => { v2.papeis[p] = String(atual.address); });
    return v2;
  }

  function marca(storage) { return { printer: cru(storage, LEGADO), printers: cru(storage, LEGADOS) }; }

  function gravar(storage, v2) { try { storage.setItem(CHAVE, JSON.stringify(v2)); } catch (e) {} }

  // Lê o v2, migrando na primeira vez e trazendo mudanças feitas nas chaves antigas.
  function ler(storage) {
    const agora = marca(storage);
    let v2 = valido(json(cru(storage, CHAVE)));
    if (!v2) {
      v2 = migrar(json(agora.printer), json(agora.printers));
      v2.legado = agora;
      // Sem impressora nenhuma não grava: a chave vazia apagaria a
      // preferência que o backup do aparelho ainda vai restaurar.
      if (Object.keys(v2.impressoras).length) gravar(storage, v2);
      return v2;
    }
    if (!v2.legado) { v2.legado = agora; gravar(storage, v2); return v2; }
    let mudou = false;
    if (v2.legado.printers !== agora.printers) {
      const lista = json(agora.printers);
      (Array.isArray(lista) ? lista : []).forEach(p => {
        const imp = deLegado(p);
        if (imp && !v2.impressoras[imp.address]) v2.impressoras[imp.address] = imp;
      });
      mudou = true;
    }
    if (v2.legado.printer !== agora.printer) {
      const p = json(agora.printer);
      const imp = deLegado(p);
      if (imp) {
        const antiga = v2.impressoras[imp.address];
        // Mesma impressora sem mídia nova: preserva o que só o v2 sabe.
        v2.impressoras[imp.address] = antiga && !(p.midia || p.etiqueta) ? Object.assign({}, antiga, { nome: imp.nome, linguagem: imp.linguagem, linguagemManual: imp.linguagemManual }) : Object.assign({}, antiga || {}, imp);
        PAPEIS.forEach(papel => { v2.papeis[papel] = imp.address; });
      } else PAPEIS.forEach(papel => { v2.papeis[papel] = null; });
      mudou = true;
    }
    if (mudou) { v2.legado = agora; gravar(storage, v2); }
    return v2;
  }

  // Grava o v2 e espelha nas chaves antigas.
  function salvar(storage, v2) {
    const limpo = valido(Object.assign({}, v2, { versao: 2 })) || vazio();
    const ativa = limpo.impressoras[limpo.papeis.etiqueta] || limpo.impressoras[limpo.papeis.cupom] || null;
    const printer = ativa ? JSON.stringify(paraLegado(ativa)) : null;
    const printers = JSON.stringify(Object.values(limpo.impressoras).map(paraLegado));
    try {
      if (printer == null) storage.removeItem(LEGADO); else storage.setItem(LEGADO, printer);
      storage.setItem(LEGADOS, printers);
    } catch (e) {}
    limpo.legado = { printer, printers };
    gravar(storage, limpo);
    return limpo;
  }

  // Cadastra ou atualiza. Campos ausentes preservam o que já existia.
  function cadastrar(v2, dados) {
    if (!dados || !dados.address) return v2;
    const mac = String(dados.address);
    const antiga = v2.impressoras[mac];
    const base = antiga || deLegado({ address: mac, name: dados.nome || dados.name || mac });
    const nova = Object.assign({}, base);
    ['nome', 'apelido', 'linguagem', 'linguagemManual', 'midia', 'midiaConfirmada', 'densidade', 'velocidade', 'inverter', 'calibradaEm', 'offsetMm']
      .forEach(k => { if (dados[k] !== undefined) nova[k] = copia(dados[k]); });
    if (dados.linguagem !== undefined && dados.midia === undefined && (!antiga || antiga.linguagem !== dados.linguagem)) {
      nova.midia = midiaDoPapel(dados.linguagem === 'tspl' ? catalogo().PADRAO_TSPL : catalogo().PADRAO_ESCPOS);
      nova.midiaConfirmada = false;
    }
    v2.impressoras[mac] = nova;
    return v2;
  }

  function esquecer(v2, mac) {
    delete v2.impressoras[mac];
    PAPEIS.forEach(p => { if (v2.papeis[p] === mac) v2.papeis[p] = null; });
    return v2;
  }

  function definirPapel(v2, papel, mac) {
    if (PAPEIS.indexOf(papel) < 0) return v2;
    v2.papeis[papel] = mac && v2.impressoras[mac] ? mac : null;
    return v2;
  }

  function classeDaMidia(imp) {
    const p = imp && imp.midia && catalogo().daMidia(imp.midia);
    return p ? p.classe : null;
  }

  // Impressora que serve um papel: a escolhida; senão a única com mídia
  // daquela classe; senão a única do aparelho; senão nenhuma (abre o seletor).
  function resolver(v2, papel) {
    const imps = v2.impressoras;
    const mac = v2.papeis[papel];
    if (mac && imps[mac]) return imps[mac];
    const lista = Object.values(imps);
    const daClasse = lista.filter(i => classeDaMidia(i) === papel);
    if (daClasse.length === 1) return daClasse[0];
    return lista.length === 1 ? lista[0] : null;
  }

  // Documento do app -> papel. Produção vai para a etiqueta; pedido e teste
  // de recibo, para o cupom.
  function papelDoDocumento(documento) {
    return documento === 'etiqueta-producao' || documento === 'production' || documento === 'producao' ? 'etiqueta' : 'cupom';
  }

  // #2209: o papel da regra é o do catálogo da loja (etiqueta-52, cupom-58...); no aparelho
  // ele escolhe a impressora pela classe. Folha (a4, a5) não sai na térmica: fica a do documento.
  function classeDoPapel(papel, documento) {
    if (typeof papel === 'string' && /^etiqueta(-|$)/.test(papel)) return 'etiqueta';
    if (typeof papel === 'string' && /^cupom(-|$)/.test(papel)) return 'cupom';
    return papelDoDocumento(documento);
  }

  // Regra da loja (do servidor desde a F4, guardada pelo sync.js). Códigos: a regra; senão o
  // modo de leitura salvo; senão só o QR (D4: a barra espera o número curto do lote).
  function regra(storage, documento) {
    const r = json(cru(storage, REGRA));
    const d = (r && r.documentos && r.documentos[documento]) || {};
    let codigos = Array.isArray(d.codigos) ? d.codigos.filter(c => c === 'qr' || c === 'barras') : null;
    if (!codigos) { const modo = cru(storage, 'cdb-prod-scan-mode'); codigos = MODOS[modo] ? MODOS[modo].slice() : ['qr']; }
    const orient = d.orientacao || cru(storage, 'cdb-etq-orient');
    return {
      papel: classeDoPapel(d.papel, documento),
      orientacao: orient === 'horizontal' ? 'horizontal' : 'vertical',
      codigos,
      copias: Math.max(1, Math.floor(positivo(d.copias, 1)))
    };
  }

  // #2209 (F4): a forma canônica do servidor (RegraImpressaoNormalizador). Documentos
  // e códigos na ordem do catálogo, o que não é do contrato sai, as chaves na ordem
  // do texto canônico. Nunca lança: a regra ruim vira o padrão, não derruba a impressão.
  const CATALOGO_REGRA = {
    documentos: ['etiqueta-producao', 'etiqueta-pedido', 'pedido'],
    papeis: ['cupom-58', 'cupom-80', 'etiqueta-52', 'etiqueta-80', 'etiqueta-10x15', 'a4', 'a5'],
    orientacoes: ['vertical', 'horizontal'],
    codigos: ['qr', 'barras'],
    origens: ['Sistema', 'Empresa'],
    copias: { min: 1, max: 10, padrao: 1 }
  };
  const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  const GUID_VAZIO = '00000000-0000-0000-0000-000000000000';

  function templateDaRegra(t) {
    if (!t || typeof t !== 'object' || CATALOGO_REGRA.origens.indexOf(t.origem) < 0) return null;
    const id = typeof t.id === 'string' && GUID.test(t.id) ? t.id.toLowerCase() : GUID_VAZIO;
    return id === GUID_VAZIO ? null : { origem: t.origem, id };
  }

  // Para a loja nunca configurada o servidor devolve impressao null: trate antes,
  // porque aqui null vira a regra vazia e a diferença some.
  function normalizarRegra(entrada) {
    const docs = entrada && typeof entrada.documentos === 'object' && entrada.documentos ? entrada.documentos : {};
    const documentos = {};
    const C = CATALOGO_REGRA;
    C.documentos.forEach(nome => {
      const d = Object.prototype.hasOwnProperty.call(docs, nome) ? docs[nome] : null;
      if (!d || typeof d !== 'object' || C.papeis.indexOf(d.papel) < 0) return;
      const copias = Number.isInteger(d.copias) ? d.copias : C.copias.padrao;
      documentos[nome] = {
        papel: d.papel,
        template: templateDaRegra(d.template),
        orientacao: C.orientacoes.indexOf(d.orientacao) >= 0 ? d.orientacao : null,
        codigos: Array.isArray(d.codigos) ? C.codigos.filter(c => d.codigos.indexOf(c) >= 0) : null,
        copias: Math.min(C.copias.max, Math.max(C.copias.min, copias))
      };
    });
    return { v: 1, documentos };
  }

  global.cdbImpressoras = {
    CHAVE, REGRA, PAPEIS: PAPEIS.slice(),
    linguagemPorNome, deLegado, paraLegado, migrar, ler, salvar, cadastrar, esquecer, definirPapel, resolver, papelDoDocumento, regra,
    normalizarRegra
  };
})(typeof window !== 'undefined' ? window : globalThis);
