const { app, BrowserWindow, dialog, shell } = require('electron')
const { spawn } = require('node:child_process')
const fs = require('node:fs')
const net = require('node:net')
const path = require('node:path')

// A fixed port keeps the origin stable between launches, so browser storage and the service worker survive.
const PREFERRED_PORT = 47615

let server
let window
let quitting = false

// A second launch would start a second server over the same workspace; focus the running app instead.
if (!app.requestSingleInstanceLock()) app.quit()
else {
  app.on('second-instance', () => {
    if (!window) return
    if (window.isMinimized()) window.restore()
    window.focus()
  })
  app.whenReady().then(start).catch(fail)
  app.on('window-all-closed', () => app.quit())
  app.on('quit', () => { quitting = true; server?.kill() })
}

async function start() {
  window = new BrowserWindow({ width: 1280, height: 800, minWidth: 480, minHeight: 400, title: 'Odysseum', autoHideMenuBar: true })
  // Links out of the app belong in the writer's browser, not in a second app window.
  window.webContents.setWindowOpenHandler(({ url }) => { shell.openExternal(url); return { action: 'deny' } })
  await window.loadFile(path.join(__dirname, 'splash.html'))

  const port = await pickPort()
  startServer(port)
  await waitForPort(port)
  await window.loadURL(`http://127.0.0.1:${port}/`)
}

function startServer(port) {
  const data = path.join(app.getPath('userData'), 'server')
  fs.mkdirSync(data, { recursive: true })

  // Environment variables always win over the settings file, so the workspace is seeded here
  // instead of being passed as ODYSSEUM_WORKSPACE; the writer can then move it later.
  const settings = path.join(data, 'server-settings.json')
  if (!fs.existsSync(settings)) {
    const workspace = path.join(app.getPath('documents'), 'Odysseum')
    fs.mkdirSync(workspace, { recursive: true })
    fs.writeFileSync(settings, JSON.stringify({ workspace, demo: true }, null, 2))
  }

  const root = app.isPackaged ? path.join(process.resourcesPath, 'server') : path.join(__dirname, 'server')
  const binary = path.join(root, process.platform === 'win32' ? 'Odysseum.Server.exe' : 'Odysseum.Server')
  server = spawn(binary, [], {
    cwd: data,
    windowsHide: true,
    stdio: 'ignore',
    env: {
      ...process.env,
      ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      ODYSSEUM_SETTINGS: settings,
      ODYSSEUM_KEYS: path.join(data, 'keys'),
      // The server only installs its bundled UI into an empty directory; one per app version makes upgrades pick up the new UI.
      ODYSSEUM_WEBUI: path.join(data, `webui-${app.getVersion()}`)
    }
  })
  server.on('error', fail)
  server.on('exit', code => { if (code && !quitting) fail(new Error(`The Odysseum server stopped unexpectedly (exit code ${code}).`)) })
}

function pickPort() {
  const listen = port => new Promise((resolve, reject) => {
    const probe = net.createServer()
    probe.once('error', reject)
    probe.listen(port, '127.0.0.1', () => { const chosen = probe.address().port; probe.close(() => resolve(chosen)) })
  })
  return listen(PREFERRED_PORT).catch(() => listen(0))
}

async function waitForPort(port) {
  for (let attempt = 0; attempt < 300; attempt++) {
    const open = await new Promise(resolve => {
      const socket = net.connect(port, '127.0.0.1')
      socket.once('connect', () => { socket.destroy(); resolve(true) })
      socket.once('error', () => resolve(false))
    })
    if (open) return
    await new Promise(resolve => setTimeout(resolve, 200))
  }
  throw new Error('The Odysseum server did not start.')
}

function fail(error) {
  dialog.showErrorBox('Odysseum could not start', error.message)
  app.exit(1)
}
