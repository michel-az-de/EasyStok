(function (global) {
  'use strict';
  // #2202 (impressão v2, F2): resolvedor da etiqueta de produção por tamanho.
  // Entrada: campos já formatados, a área do papel (cdbPapeis.area) e os
  // códigos pedidos. Saída: LayoutJson v1 no tamanho final, em mm, sem
  // escala posterior. Se nem os campos obrigatórios cabem nos pisos de
  // leitura, devolve recusa com o motivo em vez de imprimir algo ilegível.
  const PT_MM = 25.4 / 72;
  const PONTO = 0.125; // 1 ponto a 203 dpi (8 pontos/mm)
  const MIN = {
    nomePt: 10, dadoPt: 7.5, valPt: 9,
    qrModulo: 3, qrModuloMax: 4, qrQuieta: 4,          // em pontos e módulos (ISO 18004)
    barraModulo: 2, barraModuloMax: 3, barraQuieta: 10, // em pontos e módulos (ISO 15417)
    barraAlturaMm: 8
  };
  const FONTE = "Manrope, 'Noto Sans', Roboto, Arial, sans-serif";
  // Nível Q (25%): aguenta borrão e risco da térmica. No simulador, o nível M
  // deixou 1 em 120 códigos sem leitura no ZXing; o Q leu todos.
  const QR_NIVEL = 'Q';
  const EMPRESA_PT = 8;
  const HRI_PT = 7.5;

  const grade = mm => Math.floor(mm / PONTO + 1e-6) * PONTO;

  function medidor(opcoes) {
    if (opcoes && typeof opcoes.medir === 'function') return opcoes.medir;
    const doc = (opcoes && opcoes.document) || global.document;
    const ctx = doc.createElement('canvas').getContext('2d');
    return (texto, pt, peso) => {
      ctx.font = `${peso} 100px ${FONTE}`;
      return ctx.measureText(texto).width / 100 * pt * PT_MM;
    };
  }

  // Code128 C: início, um símbolo a cada 2 dígitos, verificador e parada.
  function modulosBarras(digitos) { return (digitos.length / 2 + 3) * 11 + 2; }
  function modulosQr(texto) {
    const q = global.qrcode(0, QR_NIVEL);
    q.addData(texto);
    q.make();
    return q.getModuleCount();
  }

  function quebrar(medir, texto, largura, pt, peso, max) {
    const palavras = String(texto || '').split(/\s+/).filter(Boolean);
    const linhas = [];
    let atual = '';
    for (const p of palavras) {
      const t = atual ? atual + ' ' + p : p;
      if (atual && medir(t, pt, peso) > largura) { linhas.push(atual); atual = p; } else atual = t;
    }
    if (atual) linhas.push(atual);
    return linhas.length && linhas.length <= max && linhas.every(l => medir(l, pt, peso) <= largura) ? linhas : null;
  }

  function texto(id, conteudo, x, y, w, h, pt, peso, extra) {
    return Object.assign({ id, type: 'text', content: conteudo, x_mm: x, y_mm: y, w_mm: w, h_mm: h, font: 'sans', size_pt: pt, weight: peso, align: 'left', overflow: 'clip' }, extra || {});
  }

  // campos: { empresa, nome, validade, fabricacao, lote, peso, qr, barras }
  // area: { w_mm, h_mm, continuo, girar } no sentido do desenho.
  // opcoes: { codigos: ['qr','barras'], medir?, document? }
  function resolver(campos, area, opcoes) {
    const o = Object.assign({ codigos: ['qr'] }, opcoes);
    const medir = medidor(o);
    const W = area.w_mm;
    const H = area.h_mm;
    // Rolo contínuo sem giro: a altura é só o teto; o desenho encolhe até o conteúdo.
    const flexivel = !!area.continuo && !area.girar;
    const horiz = W > H;
    const m = Math.min(W, H) < 40 ? 1.5 : 2;
    const avisos = [];
    const omitidos = [];
    const codigos = [];
    const recusar = motivo => ({ layout: null, avisos, omitidos, recusa: motivo });

    const querQr = o.codigos.indexOf('qr') >= 0 && !!campos.qr;
    let querBarras = o.codigos.indexOf('barras') >= 0;
    if (querBarras && !/^\d+$/.test(String(campos.barras || '')) ) {
      // D4: a barra leva o número curto do lote. Sem ele, ela não sai.
      avisos.push('código de barras desligado: o lote ainda não tem número curto');
      querBarras = false;
    }
    const barras = querBarras && String(campos.barras).length % 2 ? '0' + campos.barras : campos.barras;

    // 1. QR: coluna da direita (horizontal) ou acima das barras, à direita dos dados (vertical).
    let qrLado = 0;
    let qrMod = 0;
    let qrN = 0;
    if (querQr) {
      qrN = modulosQr(campos.qr);
      const alvo = horiz
        ? Math.min(H - 2 * m - (querBarras ? MIN.barraAlturaMm + HRI_PT * PT_MM * 1.3 + 1 : 0), (W - 2 * m) * 0.42)
        : Math.min((W - 2 * m) * 0.45, H * 0.3);
      qrMod = Math.min(MIN.qrModuloMax * PONTO, grade(alvo / (qrN + 2 * MIN.qrQuieta)));
      if (qrMod < MIN.qrModulo * PONTO) return recusar(`o QR não cabe: precisaria de ${((qrN + 8) * MIN.qrModulo * PONTO).toFixed(1)} mm e há ${alvo.toFixed(1)} mm`);
      qrLado = qrMod * (qrN + 2 * MIN.qrQuieta);
    }
    const colTexto = horiz && querQr ? W - 2 * m - qrLado : W - 2 * m;

    // 2. Barras no rodapé, alinhadas à direita da coluna, com o número embaixo.
    let yBase = H - m;
    if (querBarras) {
      const total = modulosBarras(barras) + 2 * MIN.barraQuieta;
      const largura = horiz ? colTexto : W - 2 * m;
      const mod = Math.min(MIN.barraModuloMax * PONTO, grade(largura / total));
      if (mod < MIN.barraModulo * PONTO) return recusar(`o código de barras não cabe: precisaria de ${(total * MIN.barraModulo * PONTO).toFixed(1)} mm e há ${largura.toFixed(1)} mm`);
      const alt = Math.max(MIN.barraAlturaMm, Math.min(15, Math.min(W, H) * 0.2));
      const hriH = HRI_PT * PT_MM * 1.3;
      const caixa = total * mod;
      const x = m + largura - caixa;
      yBase -= hriH;
      codigos.push(texto('hri', barras, x, yBase, caixa, hriH, HRI_PT, 400, { align: 'center' }));
      yBase -= alt;
      codigos.push({ id: 'barras', type: 'code', format: 'barcode-code128', content: barras, x_mm: x, y_mm: yBase, w_mm: caixa, h_mm: alt, quiet_zone_mm: MIN.barraQuieta * mod, _mod: mod });
      yBase -= 1;
    }
    let qrBox = null;
    if (querQr) {
      const x = W - m - qrLado;
      const y = horiz ? m : yBase - qrLado;
      qrBox = { x, y, lado: qrLado };
      // A caixa leva o símbolo e 1 módulo; o resto da zona quieta fica em volta.
      const q = (MIN.qrQuieta - 1) * qrMod;
      codigos.push({ id: 'qr', type: 'code', format: 'qr', content: campos.qr, x_mm: x + q, y_mm: y + q, w_mm: qrLado - 2 * q, h_mm: qrLado - 2 * q, quiet_zone_mm: qrMod, _mod: qrMod, _n: qrN });
    }
    const fundoTexto = yBase;

    // 3. Texto: empresa, nome (até 3 linhas) e dados por prioridade. Busca
    // conjunta do maior corpo; na altura fixa os dados descem até o fundo.
    const dados = [
      { id: 'val', texto: campos.validade ? `VAL ${campos.validade}` : '', peso: 700, min: MIN.valPt, pri: 1 },
      { id: 'fab', texto: campos.fabricacao ? `FAB ${campos.fabricacao}` : '', peso: 400, min: MIN.dadoPt, pri: 1 },
      { id: 'lote', texto: campos.lote ? `LOTE ${campos.lote}` : '', peso: 700, min: MIN.dadoPt, pri: 1 },
      { id: 'peso', texto: campos.peso || '', peso: 400, min: MIN.dadoPt, pri: 2 },
      { id: 'nota', texto: campos.nota || '', peso: 400, min: MIN.dadoPt, pri: 2 },
      { id: 'empresa', texto: campos.empresa || '', peso: 600, min: EMPRESA_PT, pri: 3, topo: true }
    ].filter(l => l.texto);

    function lim(y, h) {
      if (horiz || !qrBox) return colTexto;
      return y + h > qrBox.y && y < qrBox.y + qrBox.lado ? colTexto - qrBox.lado : colTexto;
    }

    function montar(incluidas) {
      const fixos = [];
      let y0 = m;
      const emp = incluidas.find(l => l.topo);
      if (emp) {
        const h = EMPRESA_PT * PT_MM * 1.25;
        if (medir(emp.texto, EMPRESA_PT, emp.peso) > lim(y0, h)) return null;
        fixos.push(texto(emp.id, emp.texto, m, y0, lim(y0, h), h, EMPRESA_PT, emp.peso));
        y0 += h;
      }
      const linhas = incluidas.filter(l => !l.topo);
      let melhor = null;
      for (let pn = Math.min(20, H * 0.9); pn >= MIN.nomePt; pn -= 0.5) {
        const nome = quebrar(medir, campos.nome, lim(y0, pn * PT_MM * 1.15 * 3), pn, 700, 3);
        if (!nome) continue;
        const hn = pn * PT_MM * 1.15;
        const y1 = y0 + nome.length * hn + 0.8;
        for (let pt = Math.min(13, pn - 1); pt >= MIN.dadoPt; pt -= 0.5) {
          const nota = pt + pn / 2 - (nome.length - 1) * 1.5;
          if (melhor && nota <= melhor.nota) break;
          const corpos = linhas.map(d => (d.id === 'val' ? Math.max(pt + 1, d.min) : Math.max(pt, d.min)));
          const alturas = corpos.map(c => c * PT_MM * 1.3);
          const bloco = alturas.reduce((s, h) => s + h, 0);
          // Altura fixa: os dados descem até o fundo; se ao lado do QR não
          // cabem, sobem para logo abaixo do nome, na largura toda.
          const inicios = flexivel ? [y1] : [Math.max(y1, fundoTexto - bloco), y1];
          let els = null;
          for (const ini of inicios) {
            if (els || ini + bloco > fundoTexto + 0.01) continue;
            const tent = [];
            let y2 = ini;
            let ok = true;
            for (let i = 0; i < linhas.length && ok; i++) {
              const largura = lim(y2, alturas[i]);
              if (medir(linhas[i].texto, corpos[i], linhas[i].peso) > largura) ok = false;
              else tent.push(texto(linhas[i].id, linhas[i].texto, m, y2, largura, alturas[i], corpos[i], linhas[i].peso));
              y2 += alturas[i];
            }
            if (ok) els = tent;
          }
          if (!els) continue;
          const nomes = nome.map((t, i) => texto('nome', t, m, y0 + i * hn, lim(y0 + i * hn, hn), hn, pn, 700));
          melhor = { nota, els: fixos.concat(nomes, els) };
        }
      }
      return melhor && melhor.els;
    }

    let incluidas = dados.slice();
    let elementos = montar(incluidas);
    for (const pri of [3, 2]) {
      if (elementos) break;
      omitidos.push.apply(omitidos, incluidas.filter(l => l.pri === pri).map(l => l.id));
      incluidas = incluidas.filter(l => l.pri < pri);
      elementos = montar(incluidas);
    }
    if (!elementos) return recusar(`nome, validade, fabricação e lote não cabem em ${W} x ${H} mm com letra legível (nome ${MIN.nomePt} pt, dados ${MIN.dadoPt} pt)`);

    // 4. Rolo contínuo: sobe os códigos até o texto e corta a altura no conteúdo.
    let altura = H;
    if (flexivel) {
      const caixaCodigo = c => (c.id === 'qr' ? { x0: qrBox.x, x1: qrBox.x + qrBox.lado, y0: qrBox.y } : { x0: c.x_mm, x1: c.x_mm + c.w_mm, y0: c.y_mm });
      let sobe = Infinity;
      for (const c of codigos) {
        const b = caixaCodigo(c);
        for (const t of elementos) {
          const larg = medir(t.content, t.size_pt, t.weight);
          const fim = t.y_mm + t.h_mm;
          if (fim <= b.y0 + 0.01 && t.x_mm < b.x1 && t.x_mm + larg > b.x0) sobe = Math.min(sobe, b.y0 - fim - 0.5);
        }
      }
      if (!Number.isFinite(sobe)) sobe = codigos.length ? Math.min.apply(null, codigos.map(c => caixaCodigo(c).y0)) - m : 0;
      sobe = Math.max(0, sobe);
      codigos.forEach(c => { c.y_mm -= sobe; });
      if (qrBox) qrBox.y -= sobe;
      const fundo = Math.max.apply(null, elementos.concat(codigos).map(e => e.y_mm + e.h_mm).concat(qrBox ? [qrBox.y + qrBox.lado] : []));
      altura = Math.min(H, Math.ceil((fundo + m) / PONTO) * PONTO);
    }

    const layout = {
      v: 1,
      size: { preset: `${W}x${altura}mm`, w_mm: W, h_mm: altura, orientation: o.orientacao === 'horizontal' || (!o.orientacao && horiz) ? 'horizontal' : 'vertical' },
      elements: elementos.concat(codigos)
    };
    return { layout, avisos, omitidos, recusa: null, qrCaixa: qrBox && { x_mm: qrBox.x, y_mm: qrBox.y, lado_mm: qrBox.lado } };
  }

  global.cdbResolverEtiqueta = { resolver, MIN, FONTE, PT_MM, PONTO, QR_NIVEL, modulosBarras, modulosQr };
})(typeof window !== 'undefined' ? window : globalThis);
