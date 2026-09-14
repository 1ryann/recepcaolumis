JSON.stringify((() => {
  const viewport = document.querySelector('.totem-carousel-viewport');
  const active = document.querySelector('.totem-carousel-card.is-active');
  const rect = active?.getBoundingClientRect();
  const viewRect = viewport?.getBoundingClientRect();
  return {
    size: [innerWidth, innerHeight], document: [document.documentElement.scrollWidth, document.documentElement.scrollHeight],
    scrollY, count: document.querySelectorAll('[role=option]').length,
    active: active?.querySelector('strong')?.textContent,
    centerError: rect ? Math.round((rect.x + rect.width / 2 - (viewRect.x + viewRect.width / 2)) * 100) / 100 : null,
    card: rect?.toJSON(), stage: viewRect?.toJSON(),
    touchAction: viewport ? getComputedStyle(viewport).touchAction : null,
    reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches,
    scrollBehavior: viewport ? getComputedStyle(viewport).scrollBehavior : null,
    activeTransform: active ? getComputedStyle(active).transform : null,
    focus: document.activeElement?.getAttribute('aria-label'),
    focusVisible: document.activeElement?.matches(':focus-visible'),
    neighbors: Array.from(document.querySelectorAll('.totem-carousel-card')).filter(c => ['-1', '1'].includes(c.dataset.offset)).map(c => {
      const r = c.getBoundingClientRect();
      return {name:c.querySelector('strong').textContent,visibleWidth:Math.max(0,Math.min(r.right,viewRect.right)-Math.max(r.left,viewRect.left))};
    })
  };
})())
