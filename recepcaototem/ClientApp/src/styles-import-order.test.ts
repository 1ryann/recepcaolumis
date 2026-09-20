import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { expect, test } from 'vitest'

// Import order decides CSS order in the bundle, and CSS order decides which of two rules
// with equal specificity wins. Page-level stylesheets (styles.totem-room.css,
// styles.totem-professionals.css) refine selectors that also exist in styles.css, so they
// must be emitted AFTER it. They are reached through App, which means App has to be
// imported after styles.css — when it was imported first, every one of those refinements
// was emitted ~190 KB earlier in the bundle and silently lost the tie.
test('styles.css is imported before anything that reaches a page component', () => {
  const main = readFileSync(resolve(process.cwd(), 'src/main.tsx'), 'utf8')
  const styles = main.indexOf("'./styles.css'")
  const app = main.indexOf("from './App'")
  expect(styles).toBeGreaterThanOrEqual(0)
  expect(app).toBeGreaterThanOrEqual(0)
  expect(styles).toBeLessThan(app)
})
