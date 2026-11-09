// 登录 UI 使用独立 SQL 测试库和真实 API。凭据只在内存/stdin，退出后移除本次测试库。
import { test, expect } from '@playwright/test'
import { execFileSync, spawn, type ChildProcess } from 'node:child_process'
import { readFileSync, writeFileSync, mkdirSync, unlinkSync } from 'node:fs'
import { resolve } from 'node:path'
import { randomUUID } from 'node:crypto'

const root = resolve('../..')
const database = `PortQueueSessionBrowser_${randomUUID().replaceAll('-', '')}`
const connection = `Server=(localdb)\\PortQueueDev;Database=${database};Integrated Security=True;TrustServerCertificate=True`
const api = 'http://127.0.0.1:5280'
const password = randomUUID() + '!a9'
const sqlcmd = 'C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE'
let server: ChildProcess | undefined
let created = false
const env = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ConnectionStrings__PortQueue: connection }
function sql(source: string, db = database) {
  mkdirSync(resolve(root, 'artifacts'), { recursive: true })
  const inputFile = resolve(root, 'artifacts', `${database}.sql`)
  writeFileSync(inputFile, source, 'utf8')
  try {
    return execFileSync(sqlcmd, ['-S', '(localdb)\\PortQueueDev', '-d', db, '-E', '-b', '-f', '65001', '-i', inputFile], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] })
  } catch (failure) {
    const result = failure as { stdout?: string; stderr?: string }
    throw new Error(`SQL fixture failed: ${result.stdout ?? ''} ${result.stderr ?? ''}`)
  } finally { unlinkSync(inputFile) }
}
async function cleanup() {
  if (server && server.exitCode === null) { server.kill(); await new Promise<void>(resolve => server!.once('exit', () => resolve())) }
  if (created && /^PortQueueSessionBrowser_[0-9a-f]{32}$/.test(database)) {
    sql(`ALTER DATABASE [${database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${database}];`, 'master'); created = false
  }
}
// 禁止 trace 记录登录请求体；截图中的 password 输入由浏览器遮蔽。
test.use({ trace: 'off' })
test.beforeAll(async () => {
  test.setTimeout(60_000)
  try {
    sql(`CREATE DATABASE [${database}];`, 'master'); created = true
    for (const file of ['01_schema.sql', '02_views.sql', '03_triggers.sql', '04_procedures.sql', '05_seed_reference.sql', '09_permissions.sql', '10_seed_demo.sql', '12_refresh_sample_labels.sql', '13_seed_port_scenarios.sql']) sql(readFileSync(resolve(root, 'database', file), 'utf8').replaceAll('[PortQueue]', `[${database}]`))
    const assembly = resolve(root, 'src/PortQueue.Api/bin/Release/net8.0/PortQueue.Api.dll')
    for (const [username, name, role] of [['browser.reviewer', '林悦', 'REVIEWER'], ['browser.dispatcher', '周航', 'DISPATCHER']]) {
      execFileSync('dotnet', [assembly, 'account', 'create', username, name, role], { cwd: root, env, input: `${password}\n${password}\n`, stdio: ['pipe', 'pipe', 'pipe'] })
    }
    server = spawn('dotnet', [assembly, '--urls', api], { cwd: root, env, windowsHide: true, stdio: 'ignore' })
    for (let attempt = 0; attempt < 40; attempt++) {
      try { if ((await fetch(api + '/api/session')).ok) return } catch { /* 等待测试 API 绑定端口。 */ }
      if (server.exitCode !== null) throw new Error('Isolated session API exited before startup.')
      await new Promise(resolve => setTimeout(resolve, 200))
    }
    throw new Error('Isolated session API did not become ready.')
  } catch (error) { await cleanup(); throw error }
})
test.afterAll(cleanup)
test.beforeEach(async ({ page }) => {
  await page.route(url => url.pathname.startsWith('/api/'), async route => {
    const response = await route.fetch({ url: api + new URL(route.request().url()).pathname + new URL(route.request().url()).search })
    await route.fulfill({ response })
  })
})

test('真实登录错误恢复、身份持久与退出', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('button', { name: '登录', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(page.getByLabel('用户名', { exact: true })).toBeFocused()
  await page.getByLabel('用户名', { exact: true }).fill('browser.reviewer')
  await page.getByLabel('密码', { exact: true }).fill('wrong')
  await dialog.getByRole('button', { name: '登录', exact: true }).click()
  await expect(dialog.getByRole('alert')).toContainText('用户名或密码错误')
  await expect(page.getByLabel('用户名', { exact: true })).toHaveValue('browser.reviewer')
  await expect(page.getByLabel('密码', { exact: true })).toHaveValue('')
  await page.getByLabel('密码', { exact: true }).fill(password)
  await dialog.getByRole('button', { name: '登录', exact: true }).click()
  await expect(dialog).not.toBeVisible()
  await expect(page.locator('.workspace-account')).toContainText('林悦')
  await expect(page.locator('.workspace-account')).toContainText('审核员')
  await page.reload()
  await expect(page.locator('.workspace-account')).toContainText('林悦')
  await expect(page.locator('.state-distribution button')).toHaveCount(6)
  await page.screenshot({ path: '../../.impeccable/review/session-authenticated.png', fullPage: true })
  await page.getByRole('button', { name: '退出', exact: true }).click()
  await expect(page.locator('.workspace-account')).toContainText('未登录')
})

test('窄屏登录、键盘关闭和停用账号即时失效', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/')
  await page.getByRole('button', { name: '展开业务导航' }).click()
  await page.getByRole('button', { name: '登录', exact: true }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.screenshot({ path: '../../.impeccable/review/mobile-login.png', fullPage: false })
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).not.toBeVisible()
  await page.getByRole('button', { name: '登录', exact: true }).click()
  await page.getByLabel('用户名', { exact: true }).fill('browser.dispatcher')
  await page.getByLabel('密码', { exact: true }).fill(password)
  await page.getByRole('dialog').getByRole('button', { name: '登录', exact: true }).click()
  await expect(page.locator('.workspace-account')).toContainText('调度员')
  sql("UPDATE dbo.user_account SET status_code='DISABLED' WHERE username='browser.dispatcher';")
  await page.reload()
  await page.getByRole('button', { name: '展开业务导航' }).click()
  await expect(page.locator('.workspace-account')).toContainText('未登录')
  await page.setViewportSize({ width: 320, height: 740 })
  await page.getByRole('button', { name: '登录', exact: true }).click()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
})
