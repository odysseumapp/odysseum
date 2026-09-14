import { defineConfig } from '@playwright/test'
import path from 'node:path'

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  timeout: 30000,
  expect: { timeout: 10000 },
  reporter: 'list',
  use: {
    baseURL: 'http://127.0.0.1:5081',
    channel: 'msedge',
    headless: true,
    viewport: { width: 1440, height: 1000 },
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'dotnet run --project ../server/Odysseum.Server/Odysseum.Server.csproj --no-build --no-launch-profile -- --urls http://127.0.0.1:5081',
    url: 'http://127.0.0.1:5081/health',
    reuseExistingServer: false,
    timeout: 45000,
    env: {
      ODYSSEUM_WORKSPACE: path.resolve('../.test-data/browser'),
      ODYSSEUM_DEMO: 'true',
      ODYSSEUM_PASSWORD: '',
      ODYSSEUM_SCAN_SECONDS: '1',
      ODYSSEUM_KEYS: path.resolve('../.test-data/keys'),
      ASPNETCORE_ENVIRONMENT: 'Production',
    },
  },
})
