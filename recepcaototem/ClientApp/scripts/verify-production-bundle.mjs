import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join, resolve } from 'node:path'

const dist = resolve('dist')
const manifestPath = join(dist, 'manifest.json')

if (!existsSync(manifestPath)) throw new Error('Production manifest was not found. Run npm run build first.')

const manifest = readFileSync(manifestPath, 'utf8')
for (const reference of ['src/dev/', 'AppStore', 'src/data/mock']) {
  if (manifest.includes(reference)) throw new Error(`Production manifest references development-only code: ${reference}`)
}

function javaScriptFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) return javaScriptFiles(path)
    return entry.isFile() && path.endsWith('.js') ? [path] : []
  })
}

const emittedJavaScript = javaScriptFiles(dist).map((path) => readFileSync(path, 'utf8')).join('\n')
for (const marker of ['atrium_professionals', 'atrium_rooms', 'atrium_leases', 'atrium_visits', 'atrium_settings']) {
  if (emittedJavaScript.includes(marker)) throw new Error(`Production bundle contains demonstration storage marker: ${marker}`)
}

if (emittedJavaScript.includes('localhost:5218')) {
  throw new Error('Production bundle contains a development API proxy target.')
}

console.log('Production bundle verifier passed: no development AppStore or mock storage markers were emitted.')
