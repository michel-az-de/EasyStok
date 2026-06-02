/** @type {import('tailwindcss').Config} */
const { navy, orange } = require('../design/tokens.colors');

module.exports = {
  content: [
    './Views/**/*.cshtml',
    './Pages/**/*.cshtml',
    './wwwroot/js/**/*.js',
  ],
  darkMode: ['class', '[data-theme="dark"]'],
  // Safelist: classes geradas dinamicamente em código C#/Razor que o purge não detecta.
  safelist: [
    'theme-dark',
    { pattern: /^(bg|text|border)-(navy|orange|primary|accent)-(50|100|200|400|500|600|700|800|900)$/ },
  ],
  theme: {
    extend: {
      colors: {
        primary:        '#1E48A8',
        'primary-hover':'#0E2A6E',
        'primary-light':'#EDF1F9',
        accent:         '#E85814',
        'accent-hover': '#BF4307',
        sidebar:        '#06143A',
        'sidebar-text': 'rgba(255,255,255,.74)',
        'sidebar-active':'#F26B25',

        // Rampas navy/orange vem do modulo compartilhado design/tokens.colors.js
        // (E2: fonte unica). Valores identicos aos de antes -> dist byte-estavel.
        navy,
        // indigo default -> navy: classes bg-indigo-*/text-indigo-* existentes nas
        // Views renderizam em navy, alinhando ao DS sem reescrever cada cshtml.
        indigo: navy,
        // Web emite 7 shades de orange (sem 800/900/950/300). O safelist do Web
        // forcaria 800/900 se presentes -> pick explicito preserva o dist atual.
        orange: {
          700: orange[700], 600: orange[600], 500: orange[500], 400: orange[400],
          200: orange[200], 100: orange[100], 50: orange[50],
        },
        ink: {
          900: '#0A1530', 800: '#122042', 700: '#2A3556', 600: '#4A5470',
          500: '#707892', 400: '#98A0B4', 300: '#C2C8D6',
          200: '#DEE2EB', 100: '#ECEFF5', 50: '#F5F7FB',
        },
        ok:   { 700: '#0E6B3D', 600: '#18874E', 100: '#D6EFDF', 50: '#ECF7F0' },
        warn: { 700: '#8A5A00', 600: '#B57A00', 100: '#FBEBC4', 50: '#FCF5E0' },
        crit: { 700: '#8E2A1E', 600: '#C03B2A', 100: '#F8D8D2', 50: '#FBEAE6' },

        // Surfaces theme-aware via tokens.css
        surface:     'var(--bg-surface)',
        elevated:    'var(--bg-elevated)',
        'app-bg':    'var(--bg-app)',
      },
      fontFamily: {
        sans:    ['Inter', 'system-ui', '-apple-system', 'sans-serif'],
        display: ['Fraunces', 'Inter', 'serif'],
        serif:   ['Fraunces', 'serif'],
        mono:    ['"JetBrains Mono"', 'ui-monospace', 'SF Mono', 'monospace'],
      },
      boxShadow: {
        'es-sm': 'var(--shadow-sm)',
        'es-md': 'var(--shadow-md)',
        'es-lg': 'var(--shadow-lg)',
      },
      borderRadius: {
        'es-sm':   'var(--r-sm)',
        'es-md':   'var(--r-md)',
        'es-lg':   'var(--r-lg)',
        'es-pill': 'var(--r-pill)',
      },
    },
  },
  plugins: [],
};
