import { execFile, spawn } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readdirSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'

// Launches the unpacked app from dist/, waits for the bundled server and UI to answer,
// then closes the app and checks the server went with it. Run after `npm run dist`.
const PORT = 47615
const origin = `http://127.0.0.1:${PORT}`
const dist = path.join(import.meta.dirname, 'dist')

function findApp() {
  for (const entry of readdirSync(dist)) {
    const candidates = {
      win32: [path.join(dist, entry, 'Odysseum.exe')],
      darwin: [path.join(dist, entry, 'Odysseum.app', 'Contents', 'MacOS', 'Odysseum')],
      linux: [path.join(dist, entry, 'odysseum-desktop')]
    }[process.platform] ?? []
    const found = candidates.find(existsSync)
    if (found) return found
  }
  throw new Error(`No unpacked app found in ${dist}`)
}

async function answers(url) {
  try { return (await fetch(url, { signal: AbortSignal.timeout(2000) })).ok } catch { return false }
}

async function until(condition, seconds, message) {
  for (let elapsed = 0; elapsed < seconds * 2; elapsed++) {
    if (await condition()) return
    await new Promise(resolve => setTimeout(resolve, 500))
  }
  throw new Error(message)
}

if (await answers(`${origin}/health`)) throw new Error(`Something is already listening on ${origin}; close it first.`)

// A scratch profile and workspace keep the test away from the real Documents folder.
const scratch = mkdtempSync(path.join(tmpdir(), 'odysseum-smoke-'))
const workspace = path.join(scratch, 'workspace')
mkdirSync(path.join(scratch, 'profile', 'server'), { recursive: true })
mkdirSync(workspace)
writeFileSync(path.join(scratch, 'profile', 'server', 'server-settings.json'), JSON.stringify({ workspace, demo: true }))

const env = { ...process.env }
delete env.ELECTRON_RUN_AS_NODE // Set by VS Code terminals; it would make the app start as plain Node.
const args = [`--user-data-dir=${path.join(scratch, 'profile')}`]
if (process.platform === 'linux') args.push('--no-sandbox') // The unpacked chrome-sandbox helper is not setuid.

const binary = findApp()
console.log(`Launching ${binary}`)
const app = spawn(binary, args, { env, stdio: 'inherit' })
let exited = false
app.on('exit', () => { exited = true })

try {
  await until(() => exited ? Promise.reject(new Error('The app exited before the server answered.')) : answers(`${origin}/health`),
    90, 'The server did not answer /health within 90 seconds.')
  console.log('Server is healthy')
  if (!await answers(`${origin}/`)) throw new Error('The web UI did not load.')
  console.log('Web UI is served')
  if (!existsSync(path.join(workspace, 'Sample manuscript'))) throw new Error('The demo project was not seeded into the workspace.')
  console.log('Demo project seeded')
} catch (error) {
  app.kill('SIGKILL')
  throw error
}

// Ask the app to close the way a user would; a forced kill would skip its quit handler.
if (process.platform === 'win32') execFile('taskkill', ['/PID', String(app.pid)])
else app.kill('SIGTERM')
await until(async () => exited && !await answers(`${origin}/health`), 30, 'The server was still running 30 seconds after the app closed.')
console.log('App closed and the server stopped')
