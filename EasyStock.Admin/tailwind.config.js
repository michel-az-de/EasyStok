/** @type {import('tailwindcss').Config} */
const { navy, orange } = require('../design/tokens.colors');

module.exports = {
  content: [
    './Pages/**/*.cshtml',
    './Pages/**/*.cs',
    './wwwroot/js/**/*.js',
  ],
  // Admin é dark-first via <html data-theme="dark">.
  darkMode: ['class', '[data-theme="dark"]'],
  safelist: [
    { pattern: /^(bg|text|border)-(navy|orange)-(50|100|200|300|400|500|600|700|800|900|950)$/ },
    { pattern: /^(bg|text|border)-intent-(action|destructive|success|info|warn)$/ },
    { pattern: /^shadow-elev-(0|1|2|3|4)$/ },
    { pattern: /^bg-dv-(1|2|3|4|5|6|7|8|9|10)$/ },
  ],
  theme: {
    extend: {
      colors: {
        // ── Brand: navy (do modulo compartilhado design/tokens.colors.js — E2) ──
        // 8 shades que o Admin ja emitia; o safelist forcaria 950/400/300 se
        // presentes, entao o pick explicito preserva o dist atual.
        navy: {
          900: navy[900], 800: navy[800], 700: navy[700],
          600: navy[600], 500: navy[500],
          200: navy[200], 100: navy[100], 50: navy[50],
        },
        // ── Brand: orange escala completa 50→950 (do modulo compartilhado) ──
        orange,
        // ── Aliases legacy via CSS vars do tokens.css (theme-aware) ──
        'es-bg':        'var(--bg-app)',
        'es-surface':   'var(--bg-surface)',
        'es-surface-2': '#202b42',
        'es-card':      'var(--bg-elevated)',
        'es-accent':    '#F26B25',
        'es-accent-h':  '#E85814',
        'es-basil':     '#4fbf9f',
        'es-gold':      '#f0b84a',
        'es-cream':     '#f1dcc0',
        'es-ink':       'var(--text-primary)',
        'es-ink-dim':   'var(--text-secondary)',
        'es-ink-mute':  'var(--text-muted)',
        // ── Intent (semantic, theme-aware) ──
        intent: {
          action:      'var(--intent-action)',
          'action-fg': 'var(--intent-action-fg)',
          destructive: 'var(--intent-destructive)',
          'destructive-fg': 'var(--intent-destructive-fg)',
          success:     'var(--intent-success)',
          'success-fg':'var(--intent-success-fg)',
          info:        'var(--intent-info)',
          'info-fg':   'var(--intent-info-fg)',
          warn:        'var(--intent-warn)',
          'warn-fg':   'var(--intent-warn-fg)',
        },
        // ── Surface (theme-aware) ──
        surface: {
          app:      'var(--surface-app)',
          elevated: 'var(--surface-elevated)',
          sunken:   'var(--surface-sunken)',
          overlay:  'var(--surface-overlay)',
          inverted: 'var(--surface-inverted)',
        },
        // ── Data viz palette ──
        dv: {
          1: 'var(--dv-1)', 2: 'var(--dv-2)', 3: 'var(--dv-3)', 4: 'var(--dv-4)',
          5: 'var(--dv-5)', 6: 'var(--dv-6)', 7: 'var(--dv-7)', 8: 'var(--dv-8)',
          9: 'var(--dv-9)', 10: 'var(--dv-10)',
        },
      },
      fontFamily: {
        sans:    ['Inter', '-apple-system', 'sans-serif'],
        display: ['Fraunces', 'serif'],
        serif:   ['Fraunces', 'serif'],
        mono:    ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
      // ── Elevation (sombras hierárquicas, theme-aware via tokens.css) ──
      boxShadow: {
        'elev-0': 'var(--elev-0)',
        'elev-1': 'var(--elev-1)',
        'elev-2': 'var(--elev-2)',
        'elev-3': 'var(--elev-3)',
        'elev-4': 'var(--elev-4)',
      },
      // ── Motion ──
      transitionDuration: {
        instant: '80ms',
        fast:    '150ms',
        base:    '200ms',
        slow:    '320ms',
        slower:  '500ms',
      },
      transitionTimingFunction: {
        'out-soft':  'cubic-bezier(0.16, 1, 0.3, 1)',
        'in-soft':   'cubic-bezier(0.4, 0, 1, 1)',
        'in-out':    'cubic-bezier(0.4, 0, 0.2, 1)',
        spring:      'cubic-bezier(0.34, 1.56, 0.64, 1)',
      },
      // ── Border radius (alinhado a tokens.css) ──
      borderRadius: {
        'es-sm':   '6px',
        'es-md':   '10px',
        'es-lg':   '14px',
        'es-xl':   '20px',
        'es-pill': '999px',
      },
      // ── Z-index canonical (igual a tokens.css) ──
      zIndex: {
        dropdown:        '20',
        sticky:          '30',
        'bottom-nav':    '35',
        drawer:          '40',
        'modal-backdrop':'50',
        modal:           '55',
        toast:           '60',
        command:         '70',
        loading:         '80',
      },
    },
  },
  plugins: [],
};
