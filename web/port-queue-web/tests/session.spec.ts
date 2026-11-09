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
const env = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ConnectionStrings__PortQueue: connection, DemoAccounts__Enabled: 'true' }
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
    execFileSync('dotnet', [assembly, 'account', 'seed-demo'], { cwd: root, env, stdio: 'pipe' })
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
test.afterEach(async ({ page }) => { await page.unrouteAll({ behavior: 'ignoreErrors' }) })
test.beforeEach(async ({ page }) => {
  // 每个用例重启独立 API 清空来源限速窗口；产品仍保持每分钟五次，不改测试专用配额。
  if (server && server.exitCode === null) { server.kill(); await new Promise<void>(resolve => server!.once('exit', () => resolve())) }
  server = spawn('dotnet', [resolve(root, 'src/PortQueue.Api/bin/Release/net8.0/PortQueue.Api.dll'), '--urls', api], { cwd: root, env, windowsHide: true, stdio: 'ignore' })
  for (let attempt = 0; attempt < 40; attempt++) {
    try { if ((await fetch(api + '/api/session')).ok) break } catch { /* 等待独立测试API。 */ }
    await new Promise(resolve => setTimeout(resolve, 100))
  }
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
  await expect(page.locator('.account-options button')).toHaveCount(4)
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
  await expect(page.locator('.account-options button')).toHaveCount(4)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
})


async function presetLogin(page: import('@playwright/test').Page, role: string) {
  await page.goto('/')
  await page.getByRole('button', { name: '登录', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('button', { name: new RegExp(role) }).click()
  await expect(page.getByLabel('密码', { exact: true })).toHaveValue('123456')
  await dialog.getByRole('button', { name: '登录', exact: true }).click()
  await expect(dialog).not.toBeVisible()
}

test('账号选项可登录且输入错误不误报服务不可用', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('button', { name: '登录', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.locator('.account-options button')).toHaveCount(4)
  await page.getByLabel('用户名', { exact: true }).fill('中文账号')
  await page.getByLabel('密码', { exact: true }).fill('123456')
  await dialog.getByRole('button', { name: '登录', exact: true }).click()
  await expect(dialog.getByRole('alert')).toContainText('用户名只支持')
  await expect(page.locator('.workspace-account')).toContainText('未登录')
  await dialog.getByRole('button', { name: /审核员/ }).click()
  await page.getByLabel('密码', { exact: true }).fill('wrong')
  await dialog.getByRole('button', { name: '登录', exact: true }).click()
  await expect(dialog.getByRole('alert')).toContainText('用户名或密码错误')
  await expect(dialog).not.toContainText('服务不可用')
  await dialog.getByRole('button', { name: /审核员/ }).click()
  await page.screenshot({ path: '../../.impeccable/review/stage3b-login.png' })
  await dialog.getByRole('button', { name: '登录', exact: true }).click()
  await expect(page.locator('.workspace-account')).toContainText('林悦')
})

test('批准真实预约后清单移出而详情保留且总览更新', async ({ page }) => {
  await presetLogin(page, '审核员')
  const before = await (await fetch(api + '/api/overview')).json()
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('.appointments-pane tbody .status-text').first()).toHaveText('待审核')
  const first = page.locator('.appointments-pane tbody .vessel-row').first()
  const no = await first.locator('small').innerText(); await first.click()
  await expect(page.locator('.declaration-section')).toHaveCount(2)
  await page.getByRole('button', { name: '批准预约', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '批准预约' })
  await page.getByLabel('审核意见（选填）').fill('船舶资料、时间及申报吃水核验通过。')
  await page.screenshot({ path: '../../.impeccable/review/stage3b-review-confirm.png' })
  await dialog.getByRole('button', { name: '确认批准' }).click()
  await expect(page.locator('.save-feedback')).toContainText('预约已批准')
  await expect(page.locator('#appointment-detail')).toContainText(no)
  await expect(page.locator('#appointment-detail .status-badge')).toContainText('已批准')
  await expect(page.locator('.appointments-pane tbody')).not.toContainText(no)
  await expect(page.locator('.review-line')).toContainText('林悦')
  await page.screenshot({ path: '../../.impeccable/review/stage3b-approved.png', fullPage: true })
  await page.getByRole('button', { name: '总览', exact: true }).click()
  await expect(page.locator('.metric strong').first()).toHaveText(String(before.pendingAppointments - 1))
  const audit = sql(`SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.audit_log WHERE table_name='arrival_appointment' AND JSON_VALUE(new_data_json,'$.appointment_no')='${no}' AND JSON_VALUE(new_data_json,'$.appointment_status_code')='APPROVED';`)
  expect(audit.trim()).toMatch(/1/)
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await page.getByRole('button', { name: '全部', exact: true }).click()
  await expect(page.locator('.save-feedback')).toHaveCount(0)
})

test('窄屏驳回必填原因，成功但清单失败不会误报保存失败', async ({ page }) => {
  await presetLogin(page, '审核员')
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('.appointments-pane tbody .status-text').first()).toHaveText('待审核')
  await page.locator('.appointments-pane tbody .vessel-row').first().click()
  await expect(page.getByRole('button', { name: '驳回', exact: true })).toBeVisible()
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('button', { name: '驳回', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '驳回预约' })
  await dialog.getByRole('button', { name: '确认驳回' }).click()
  await expect(dialog.getByRole('alert')).toContainText('请填写原因')
  await page.getByLabel('审核意见（必填）').fill('申报资料不完整，请重新申请。')
  await expect(dialog.getByRole('alert')).toHaveCount(0)
  await page.screenshot({ path: '../../.impeccable/review/stage3b-mobile-review.png' })
  await page.route(url => url.pathname === '/api/appointments', route => route.fulfill({ status: 503, body: '{}' }))
  await dialog.getByRole('button', { name: '确认驳回' }).click()
  await expect(page.locator('.save-feedback')).toContainText('预约已驳回')
  await expect(page.locator('.save-feedback')).toContainText('无需再次审核')
  await expect(page.locator('#appointment-detail .status-badge')).toContainText('已驳回')
  await expect(page.locator('#appointment-detail .progress-track')).toContainText('已驳回')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy()
})

test('调度员无审核操作，接口仍拒绝绕过界面的请求', async ({ page }) => {
  await presetLogin(page, '调度员')
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.locator('.declaration-section')).toHaveCount(2)
  await expect(page.getByRole('button', { name: '批准预约', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '驳回', exact: true })).toHaveCount(0)
  const status = await page.evaluate(async () => {
    const list = await (await fetch('/api/appointments?status=PENDING')).json()
    const session = await (await fetch('/api/session')).json()
    const result = await fetch(`/api/appointments/${list.items[0].appointmentId}/review`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken }, body: JSON.stringify({ decision: 'APPROVED' }) })
    return result.status
  })
  expect(status).toBe(403)
})


test('审核令牌失效保留意见，恢复会话后可以继续确认', async ({ page }) => {
  await presetLogin(page, '审核员')
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.getByRole('button', { name: '批准预约', exact: true })).toBeVisible()
  await page.getByRole('button', { name: '批准预约', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '批准预约' })
  await page.getByLabel('审核意见（选填）').fill('保留这份核验意见')
  await page.route(url => url.pathname.endsWith('/review'), async route => {
    const response = await route.fetch({ url: api + new URL(route.request().url()).pathname, headers: { ...route.request().headers(), 'x-csrf-token': 'invalid' } })
    await route.fulfill({ response })
  }, { times: 1 })
  await dialog.getByRole('button', { name: '确认批准' }).click()
  await expect(dialog.getByRole('alert')).toContainText('会话已变化')
  await expect(page.getByLabel('审核意见（选填）')).toHaveValue('保留这份核验意见')
  await expect(dialog.getByRole('button', { name: '确认批准' })).toBeEnabled()
  await dialog.getByRole('button', { name: '确认批准' }).click()
  await expect(page.locator('.save-feedback')).toContainText('预约已批准')
})

test('审核响应丢失先读取结果，不自动重复写入', async ({ page }) => {
  await presetLogin(page, '审核员')
  await page.getByRole('button', { name: '进港预约', exact: true }).click()
  await page.getByRole('button', { name: '待审核', exact: true }).click()
  await expect(page.getByRole('button', { name: '批准预约', exact: true })).toBeVisible()
  const no = await page.locator('#appointment-detail .progress-identity p').innerText()
  await page.getByRole('button', { name: '批准预约', exact: true }).click()
  let writes = 0
  await page.route(url => url.pathname.endsWith('/review'), async route => {
    writes++
    const response = await route.fetch({ url: api + new URL(route.request().url()).pathname })
    expect(response.status()).toBe(200)
    await route.abort('failed')
  })
  const dialog = page.getByRole('dialog', { name: '批准预约' })
  await dialog.getByRole('button', { name: '确认批准' }).click()
  await expect(dialog.getByRole('alert')).toContainText('尚未确认审核结果')
  await expect(dialog.getByRole('button', { name: '确认批准' })).toHaveCount(0)
  await dialog.getByRole('button', { name: '查看最新状态' }).click()
  await expect(page.locator('#appointment-detail .status-badge')).toContainText('已批准')
  await expect(page.locator('#appointment-detail')).toContainText(no)
  await expect(page.locator('.appointments-pane tbody')).not.toContainText(no)
  expect(writes).toBe(1)
})
