/*
  Dashboard Charts — Chart.js integration para /dashboard.
  Funções ativas:
    update()        — chamado após reload() dos dados principais
    updateExtras()  — chamado após loadExtras() com dados secundários
*/
(function () {
  'use strict';

  var charts = {};

  function getCSSVar(prop) {
    try { return getComputedStyle(document.documentElement).getPropertyValue(prop).trim(); }
    catch (_) { return ''; }
  }

  // Paleta unificada — consome tokens do EasyStok, com fallback hex se var indisponivel.
  var palette = {
    receita: getCSSVar('--ok-600')     || '#18874E',
    custo:   getCSSVar('--crit-600')   || '#C03B2A',
    lucro:   getCSSVar('--navy-600')   || '#15388A',
    neutro:  getCSSVar('--ink-500')    || '#707892',
    ok:      getCSSVar('--ok-500')     || '#2DA365',
    warn:    getCSSVar('--warn-500')   || '#D89A1A',
    crit:    getCSSVar('--crit-500')   || '#D45744',
    parado:  getCSSVar('--ink-400')    || '#98A0B4',
  };

  var currencyFmt = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

  function fmtCurrency(v) { return currencyFmt.format(v ?? 0); }
  function fmtNum(v) { return new Intl.NumberFormat('pt-BR').format(v ?? 0); }

  function defaultTooltip(extra) {
    return Object.assign({
      backgroundColor: getCSSVar('--ink-900') || '#0A1530',
      titleColor:      getCSSVar('--ink-300') || '#C2C8D6',
      bodyColor:       '#FFFFFF',
      borderColor:     getCSSVar('--ink-700') || '#2A3556',
      borderWidth: 1,
      padding: 10,
      cornerRadius: 6,
    }, extra || {});
  }

  function defaultScales(yCallback) {
    return {
      x: {
        ticks: { color: '#64748B', maxRotation: 45 },
        grid: { display: false }
      },
      y: {
        ticks: {
          color: '#64748B',
          callback: yCallback || function (v) { return fmtCurrency(v); }
        },
        grid: { color: 'rgba(148,163,184,0.18)' }
      }
    };
  }

  function destroyChart(key) {
    if (charts[key]) { charts[key].destroy(); delete charts[key]; }
  }

  function emptyState(wrapperId, msg, ctaText, ctaHref) {
    var el = document.getElementById(wrapperId);
    if (!el) return;
    el.innerHTML =
      '<div class="h-full flex flex-col items-center justify-center text-slate-400 text-sm gap-2">' +
      '<svg class="w-8 h-8" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z"/></svg>' +
      '<p>' + msg + '</p>' +
      (ctaText ? '<a href="' + ctaHref + '" class="text-indigo-600 text-xs hover:underline">' + ctaText + '</a>' : '') +
      '</div>';
  }

  // ── Receita × Custo × Lucro ──────────────────────────────────────────────────

  function loadReceitaCusto(periodo, lojaId) {
    var tz = new Date().getTimezoneOffset();
    var url = '/dashboard/receita-custo?periodo=' + encodeURIComponent(periodo) + '&tz=' + tz;
    if (lojaId) url += '&lojaId=' + encodeURIComponent(lojaId);

    return window.api.get(url).then(function (resp) {
      var serie = resp && resp.data ? resp.data : (Array.isArray(resp) ? resp : []);
      renderReceitaCusto(serie);
    }).catch(function (err) {
      console.error('[dashboard-charts] receita-custo:', err);
    });
  }

  function renderReceitaCusto(serie) {
    var canvas = document.getElementById('canvas-receita-custo');
    if (!canvas) return;
    destroyChart('receitaCusto');

    if (!serie || serie.length === 0) {
      emptyState('chart-receita-custo', 'Sem dados de receita para o período.', 'Registrar venda →', '/saidas/nova');
      return;
    }

    var labels  = serie.map(function (d) { return d.label; });
    var receita = serie.map(function (d) { return d.receita; });
    var custo   = serie.map(function (d) { return d.custo; });
    var lucro   = serie.map(function (d) { return d.lucro; });
    var pts = labels.length <= 14 ? 3 : 0;

    charts.receitaCusto = new Chart(canvas, {
      type: 'line',
      data: {
        labels: labels,
        datasets: [
          { label: 'Receita', data: receita, borderColor: palette.receita, backgroundColor: 'rgba(34,197,94,0.08)', fill: true, tension: 0.4, pointRadius: pts, borderWidth: 2 },
          { label: 'Custo',   data: custo,   borderColor: palette.custo,   backgroundColor: 'rgba(239,68,68,0.06)',  fill: true, tension: 0.4, pointRadius: pts, borderWidth: 2 },
          { label: 'Lucro',   data: lucro,   borderColor: palette.lucro,   backgroundColor: 'transparent', fill: false, tension: 0.4, pointRadius: pts, borderWidth: 2, borderDash: [4, 3] },
        ]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: { display: true, position: 'top', labels: { usePointStyle: true, pointStyle: 'circle', padding: 16, color: '#64748B', font: { size: 12 } } },
          tooltip: Object.assign(defaultTooltip(), { callbacks: { label: function (ctx) { return '  ' + ctx.dataset.label + ': ' + fmtCurrency(ctx.parsed.y); } } })
        },
        scales: defaultScales()
      }
    });
  }

  // ── Estoque Donut ─────────────────────────────────────────────────────────────

  function renderEstoqueDonut(estoqueStatus) {
    var canvas = document.getElementById('canvas-estoque-donut');
    if (!canvas || !estoqueStatus) return;
    destroyChart('estoqueDonut');

    var data  = [estoqueStatus.ok, estoqueStatus.atencao, estoqueStatus.critico, estoqueStatus.parado];
    var total = estoqueStatus.total || data.reduce(function (a, b) { return a + b; }, 0);
    if (total === 0) return;

    charts.estoqueDonut = new Chart(canvas, {
      type: 'doughnut',
      data: {
        labels: ['OK', 'Atenção', 'Crítico', 'Parado'],
        datasets: [{ data: data, backgroundColor: [palette.ok, palette.warn, palette.crit, palette.parado], borderWidth: 2, borderColor: '#fff' }]
      },
      options: {
        responsive: true, maintainAspectRatio: false, cutout: '65%',
        plugins: {
          legend: { position: 'bottom', labels: { usePointStyle: true, pointStyle: 'circle', padding: 12, color: '#64748B', font: { size: 11 } } },
          tooltip: Object.assign(defaultTooltip(), { callbacks: { label: function (ctx) {
            var pct = total > 0 ? Math.round(ctx.parsed / total * 100) : 0;
            return '  ' + ctx.label + ': ' + ctx.parsed + ' (' + pct + '%)';
          }}})
        }
      },
      plugins: [{
        id: 'centerText',
        afterDraw: function (chart) {
          var ctx2 = chart.ctx;
          var cx = chart.chartArea.left + (chart.chartArea.right - chart.chartArea.left) / 2;
          var cy = chart.chartArea.top + (chart.chartArea.bottom - chart.chartArea.top) / 2;
          ctx2.save();
          ctx2.textAlign = 'center'; ctx2.textBaseline = 'middle';
          ctx2.fillStyle = '#1e293b'; ctx2.font = 'bold 22px Inter, sans-serif';
          ctx2.fillText(total, cx, cy - 8);
          ctx2.font = '11px Inter, sans-serif'; ctx2.fillStyle = '#94a3b8';
          ctx2.fillText('lotes', cx, cy + 12);
          ctx2.restore();
        }
      }]
    });
  }

  // ── Fluxo de Caixa ────────────────────────────────────────────────────────────

  function renderFluxoCaixa(serie) {
    var canvas = document.getElementById('canvas-fluxo-caixa');
    if (!canvas) return;
    destroyChart('fluxoCaixa');

    if (!serie || serie.length === 0) {
      // Fluxo de Caixa le de FechamentosCaixa, nao de Pagamentos diretamente. Empty
      // state agora explica o gap pra o dono nao concluir "vi receita de R$ 795 mas
      // o caixa diz zero — sistema quebrado". Conserto longo (reconciliacao
      // automatica) vira no modulo Caixa Conciliado.
      //
      // BUG 7: copy generica que serve tanto para "caixa nao aberto" quanto para
      // "caixa aberto sem movimento" e "caixa fechado". A CTA vai pra /caixa que
      // mostra o estado real e a acao adequada (abrir / lancar / fechar).
      emptyState(
        'chart-fluxo-caixa',
        'Ainda não há fluxo de caixa registrado para hoje. Os lançamentos aparecem aqui depois que o caixa for aberto e fechado.',
        'Ir para o caixa →',
        '/caixa');
      return;
    }

    var labels  = serie.map(function (d) { return d.label; });
    var entradas = serie.map(function (d) { return d.entradas; });
    var saidas   = serie.map(function (d) { return -d.saidas; }); // negativo para visualização
    var saldo    = serie.map(function (d) { return d.saldoAcumulado; });

    charts.fluxoCaixa = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [
          { label: 'Entradas', data: entradas, backgroundColor: 'rgba(34,197,94,0.7)', borderColor: palette.receita, borderWidth: 1, borderRadius: 3, order: 2 },
          { label: 'Saídas',   data: saidas,   backgroundColor: 'rgba(239,68,68,0.65)', borderColor: palette.custo, borderWidth: 1, borderRadius: 3, order: 2 },
          { label: 'Saldo',    data: saldo,    type: 'line', borderColor: palette.lucro, backgroundColor: 'transparent', borderWidth: 2, pointRadius: labels.length <= 14 ? 3 : 0, tension: 0.3, order: 1 }
        ]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: { display: true, position: 'top', labels: { usePointStyle: true, pointStyle: 'circle', padding: 14, color: '#64748B', font: { size: 11 } } },
          tooltip: Object.assign(defaultTooltip(), { callbacks: { label: function (ctx) {
            var val = ctx.dataset.label === 'Saídas' ? -ctx.parsed.y : ctx.parsed.y;
            return '  ' + ctx.dataset.label + ': ' + fmtCurrency(val);
          }}})
        },
        scales: defaultScales()
      }
    });
  }

  // ── Validade Timeline ─────────────────────────────────────────────────────────

  function renderValidadeTimeline(serie) {
    var canvas = document.getElementById('canvas-validade-timeline');
    if (!canvas) return;
    destroyChart('validadeTimeline');

    if (!serie || serie.length === 0 || serie.every(function (s) { return s.quantidade === 0; })) {
      emptyState('chart-validade-timeline', 'Nenhum item vencendo nas próximas 4 semanas.', '', '');
      return;
    }

    var labels = serie.map(function (d) { return d.semana; });
    var qtds   = serie.map(function (d) { return d.quantidade; });
    var colors = serie.map(function (d) {
      if (d.diasMedia <= 7)  return 'rgba(239,68,68,0.75)';
      if (d.diasMedia <= 21) return 'rgba(234,179,8,0.75)';
      return 'rgba(34,197,94,0.75)';
    });

    charts.validadeTimeline = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [{ label: 'Lotes vencendo', data: qtds, backgroundColor: colors, borderRadius: 4, borderWidth: 0 }]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: Object.assign(defaultTooltip(), { callbacks: {
            label: function (ctx) { return '  ' + ctx.parsed.y + ' unidades'; },
            afterBody: function (items) {
              var idx = items[0].dataIndex;
              var nomes = serie[idx].nomesProdutos;
              if (nomes && nomes.length) return ['', 'Produtos:', ...nomes.map(function (n) { return '  · ' + n; })];
              return [];
            }
          }})
        },
        scales: {
          x: { ticks: { color: '#64748B' }, grid: { display: false } },
          y: { ticks: { color: '#64748B', stepSize: 1 }, grid: { color: 'rgba(148,163,184,0.18)' } }
        }
      }
    });
  }

  // ── Top Produtos ──────────────────────────────────────────────────────────────

  function renderTopProdutos(items) {
    var canvas = document.getElementById('canvas-top-produtos');
    if (!canvas) return;
    destroyChart('topProdutos');
    if (!items || items.length === 0) return;

    var labels = items.map(function (d) { return d.nome.length > 20 ? d.nome.substring(0, 18) + '…' : d.nome; });
    var qtds   = items.map(function (d) { return d.quantidade; });
    var receitas = items.map(function (d) { return d.receita; });

    charts.topProdutos = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [{ label: 'Quantidade', data: qtds, backgroundColor: 'rgba(99,102,241,0.75)', borderRadius: 4, borderWidth: 0 }]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        indexAxis: 'y',
        plugins: {
          legend: { display: false },
          tooltip: Object.assign(defaultTooltip(), { callbacks: {
            label: function (ctx) { return '  Qtd: ' + fmtNum(ctx.parsed.x); },
            afterLabel: function (ctx) { return '  Receita: ' + fmtCurrency(receitas[ctx.dataIndex]); }
          }})
        },
        scales: {
          x: { ticks: { color: '#64748B', callback: function (v) { return fmtNum(v); } }, grid: { color: 'rgba(148,163,184,0.18)' } },
          y: { ticks: { color: '#64748B', font: { size: 11 } }, grid: { display: false } }
        }
      }
    });
  }

  // ── Top Clientes ──────────────────────────────────────────────────────────────

  function renderTopClientes(items) {
    var canvas = document.getElementById('canvas-top-clientes');
    if (!canvas) return;
    destroyChart('topClientes');
    if (!items || items.length === 0) return;

    var labels = items.map(function (d) { return d.nome.length > 20 ? d.nome.substring(0, 18) + '…' : d.nome; });
    var totais = items.map(function (d) { return d.totalPago; });
    var pedidos = items.map(function (d) { return d.pedidos; });

    charts.topClientes = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [{ label: 'Receita', data: totais, backgroundColor: 'rgba(20,184,166,0.75)', borderRadius: 4, borderWidth: 0 }]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        indexAxis: 'y',
        plugins: {
          legend: { display: false },
          tooltip: Object.assign(defaultTooltip(), { callbacks: {
            label: function (ctx) { return '  Receita: ' + fmtCurrency(ctx.parsed.x); },
            afterLabel: function (ctx) { return '  Pedidos: ' + pedidos[ctx.dataIndex]; }
          }})
        },
        scales: {
          x: { ticks: { color: '#64748B', callback: function (v) { return fmtCurrency(v); } }, grid: { color: 'rgba(148,163,184,0.18)' } },
          y: { ticks: { color: '#64748B', font: { size: 11 } }, grid: { display: false } }
        }
      }
    });
  }

  // ── Produção por Operador ─────────────────────────────────────────────────────

  function renderProducaoOperador(items) {
    var canvas = document.getElementById('canvas-producao-operador');
    if (!canvas || !items || items.length === 0) return;
    destroyChart('producaoOperador');

    var labels  = items.map(function (d) { return d.operador; });
    var lotes   = items.map(function (d) { return d.lotes; });
    var unidades = items.map(function (d) { return d.unidades; });

    charts.producaoOperador = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [
          { label: 'Lotes', data: lotes, backgroundColor: 'rgba(99,102,241,0.7)', borderRadius: 3, borderWidth: 0 },
          { label: 'Unidades', data: unidades, backgroundColor: 'rgba(20,184,166,0.7)', borderRadius: 3, borderWidth: 0 }
        ]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        plugins: {
          legend: { display: true, position: 'top', labels: { usePointStyle: true, pointStyle: 'circle', padding: 12, color: '#64748B', font: { size: 11 } } },
          tooltip: defaultTooltip()
        },
        scales: {
          x: { ticks: { color: '#64748B' }, grid: { display: false } },
          y: { ticks: { color: '#64748B', stepSize: 1 }, grid: { color: 'rgba(148,163,184,0.18)' } }
        }
      }
    });
  }

  // ── Entradas × Saídas Semanal ─────────────────────────────────────────────────

  function renderEntradasSaidas(items) {
    var canvas = document.getElementById('canvas-entradas-saidas');
    if (!canvas || !items || items.length === 0) return;
    destroyChart('entradasSaidas');

    var labels   = items.map(function (d) { return d.label; });
    var entradas = items.map(function (d) { return d.entradas; });
    var saidas   = items.map(function (d) { return d.saidas; });

    charts.entradasSaidas = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [
          { label: 'Entradas', data: entradas, backgroundColor: 'rgba(34,197,94,0.7)', borderRadius: 3, borderWidth: 0 },
          { label: 'Saídas',   data: saidas,   backgroundColor: 'rgba(239,68,68,0.65)', borderRadius: 3, borderWidth: 0 }
        ]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        plugins: {
          legend: { display: true, position: 'top', labels: { usePointStyle: true, pointStyle: 'circle', padding: 12, color: '#64748B', font: { size: 11 } } },
          tooltip: Object.assign(defaultTooltip(), { callbacks: { label: function (ctx) { return '  ' + ctx.dataset.label + ': ' + fmtCurrency(ctx.parsed.y); } } })
        },
        scales: defaultScales()
      }
    });
  }

  // ── Novos Clientes por Mês ────────────────────────────────────────────────────

  function renderNovosClientes(items) {
    var canvas = document.getElementById('canvas-novos-clientes');
    if (!canvas || !items || items.length === 0) return;
    destroyChart('novosClientes');

    var labels = items.map(function (d) { return d.label; });
    var novos  = items.map(function (d) { return d.novos; });

    charts.novosClientes = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [{ label: 'Novos clientes', data: novos, backgroundColor: 'rgba(99,102,241,0.75)', borderRadius: 4, borderWidth: 0 }]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: defaultTooltip()
        },
        scales: {
          x: { ticks: { color: '#64748B' }, grid: { display: false } },
          y: { ticks: { color: '#64748B', stepSize: 1 }, grid: { color: 'rgba(148,163,184,0.18)' } }
        }
      }
    });
  }

  // ── API pública ───────────────────────────────────────────────────────────────

  function update(data, periodo, lojaId) {
    if (!data) return;
    renderEstoqueDonut(data.estoqueStatus);
    loadReceitaCusto(periodo || 30, lojaId || '');
  }

  function updateExtras(extras) {
    if (!extras) return;
    renderFluxoCaixa(extras.fluxoCaixa);
    renderValidadeTimeline(extras.validadeTimeline);
    renderTopProdutos(extras.topProdutos);
    renderTopClientes(extras.topClientes);
    renderProducaoOperador(extras.producaoPorOperador);
    renderEntradasSaidas(extras.entradasSaidasSemanal);
    renderNovosClientes(extras.novosClientes);
  }

  function destroy() {
    Object.keys(charts).forEach(function (k) {
      if (charts[k]) { charts[k].destroy(); delete charts[k]; }
    });
  }

  window.dashboardCharts = {
    charts: charts,
    palette: palette,
    update: update,
    updateExtras: updateExtras,
    destroy: destroy,
    loadReceitaCusto: loadReceitaCusto,
    renderEstoqueDonut: renderEstoqueDonut,
  };
})();
