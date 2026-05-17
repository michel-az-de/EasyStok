// Schema migration — no-op for v=1; expand when breaking changes ship
export function migrateLayoutJson(layout) {
  if (!layout || typeof layout !== 'object') return layout;
  // v=1 is the only version — nothing to migrate yet
  if ((layout.v ?? 1) === 1) return layout;
  return layout;
}

if (typeof window !== 'undefined') window.migrateLayoutJson = migrateLayoutJson;
