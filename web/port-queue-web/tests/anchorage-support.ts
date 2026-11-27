// 同一浏览器验收批次按角色复用真实会话，避免把业务回归变成一分钟内重复登录压测。
import { expect, type BrowserContext, type Page } from '@playwright/test'
const cookies = new Map<string, Awaited<ReturnType<BrowserContext['cookies']>>>()
export async function loginAnchorage(page: Page, username = 'pq.dispatcher') {
  const cached = cookies.get(username)
  if (cached) await page.context().addCookies(cached)
  let session = await (await page.request.get('/api/session')).json()
  if (session.user?.username !== username) {
    expect((await page.request.post('/api/session/login',{data:{username,password:'123456'},headers:{'X-CSRF-TOKEN':session.csrfToken}})).ok()).toBeTruthy()
    session = await (await page.request.get('/api/session')).json()
    expect(session.user?.username).toBe(username)
    cookies.set(username,await page.context().cookies())
  }
  await page.goto('/#queue')
  await expect(page.locator('.aq-tabs button').first()).toBeVisible()
}
