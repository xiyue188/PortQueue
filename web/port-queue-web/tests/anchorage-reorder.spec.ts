// 调序验收使用完整真实队列；所有预览均核对未提前写入，响应丢失不重复发请求。
import { test, expect, type Page } from '@playwright/test'
import { mkdirSync } from 'node:fs'
import { loginAnchorage } from './anchorage-support'
async function openQueue(page: Page) {
  await loginAnchorage(page)
  const areas = await (await page.request.get('/api/anchorages')).json()
  const area = areas.items.find((a:{code:string})=>a.code==='ANCH-OUTER-A')
  await page.getByRole('button',{name:new RegExp(area.name)}).click()
  await expect(page.locator('.aq-vessel')).toHaveCount(area.queueCount)
  return area.anchorageId as string
}
async function read(page: Page,id:string) { return (await page.request.get(`/api/anchorages/${id}/queue`)).json() }
async function dragFifthToSecond(page: Page) {
  await page.locator('.aq-sea').evaluate(el=>el.scrollLeft=0)
  // 取消后的让位动画结束，才开始下一次独立拖动。
  await expect.poll(()=>page.locator('.aq-vessel').nth(4).evaluate(el=>{
    const lane = el.parentElement!, slot = lane.getBoundingClientRect().width / Math.max(5,lane.querySelectorAll('.aq-vessel').length)
    return Math.abs(el.getBoundingClientRect().left-lane.getBoundingClientRect().left-slot*4)
  })).toBeLessThan(1)
  const from = (await page.locator('.aq-grab').nth(4).boundingBox())!, to = (await page.locator('.aq-grab').nth(1).boundingBox())!
  await page.mouse.move(from.x+22,from.y+22); await page.mouse.down()
  await page.mouse.move(to.x+22,to.y+22,{steps:14}); await page.mouse.up()
  await expect(page.locator('.aq-reorder[open]')).toBeVisible()
  await expect(page.locator('#aq-new-position')).toHaveValue('2')
}
test('拖动预览取消不写、确认调序、并发冲突保留原因、响应丢失核对',async({page})=>{
  const id = await openQueue(page), initial = await read(page,id)
  const moving = initial.items[4], dialog = page.locator('.aq-reorder[open]')
  let writes = 0
  page.on('request',req=>{if(req.method()==='POST'&&req.url().endsWith('/reorder'))writes++})
  await dragFifthToSecond(page)
  await expect(dialog.locator('.aq-affected li')).toHaveCount(3)
  await dialog.getByRole('button',{name:'确认调序'}).click()
  expect(writes).toBe(0)
  await dialog.getByRole('button',{name:'取消',exact:true}).click()
  expect((await read(page,id)).snapshot).toBe(initial.snapshot)
  await dragFifthToSecond(page)
  await dialog.getByLabel('调整原因',{exact:true}).fill('引航衔接调整：第五位前移至第二位')
  await dialog.getByRole('button',{name:'确认调序'}).click()
  await expect(dialog).toHaveCount(0)
  await expect(page.locator('.aq-feedback')).toContainText('第5位调整至第2位')
  let queue = await read(page,id)
  expect(queue.items[1].queueEntryId).toBe(moving.queueEntryId)
  expect(queue.occupiedCount).toBe(initial.occupiedCount)
  await page.getByRole('button',{name:'关闭操作结果'}).click()
  await page.getByRole('button',{name:'移至第几位',exact:true}).click()
  await dialog.getByLabel('移至第',{exact:false}).fill(String(queue.items.length))
  await dialog.getByLabel('调整原因',{exact:true}).fill('并发后保留此调整原因')
  const session = await (await page.request.get('/api/session')).json()
  const concurrent = await page.request.post(`/api/anchorage-stays/${queue.items[0].queueEntryId}/reorder`,{
    data:{newPosition:3,reason:'另一操作窗口已更新队列',expectedSnapshot:queue.snapshot},headers:{'X-CSRF-TOKEN':session.csrfToken}})
  expect(concurrent.ok()).toBeTruthy()
  await dialog.getByRole('button',{name:'确认调序'}).click()
  await expect(dialog.getByRole('button',{name:'读取最新队列'})).toBeVisible()
  await dialog.getByRole('button',{name:'读取最新队列'}).click()
  await expect(dialog.getByLabel('调整原因',{exact:true})).toHaveValue('并发后保留此调整原因')
  await expect(dialog).toContainText('请核对新的顺位变化后确认')
  await dialog.getByRole('button',{name:'取消',exact:true}).click()
  queue = await read(page,id)
  await page.getByRole('button',{name:'总览',exact:true}).click()
  await page.getByRole('navigation',{name:'业务导航'}).getByRole('button',{name:'锚地排队',exact:true}).click()
  await page.getByRole('button',{name:'移至第几位',exact:true}).click()
  await dialog.getByLabel('移至第',{exact:false}).fill(String(queue.items.length))
  await dialog.getByLabel('调整原因',{exact:true}).fill('调序响应丢失核对验收')
  const affectedBefore = await dialog.locator('.aq-affected li').count()
  let droppedWrites = 0
  let committed!:()=>void, release!:()=>void
  const transactionCommitted = new Promise<void>(resolve=>committed=resolve), responseRelease = new Promise<void>(resolve=>release=resolve)
  await page.route(`**/api/anchorage-stays/${moving.queueEntryId}/reorder`,async route=>{
    droppedWrites++;expect((await route.fetch()).ok()).toBeTruthy();committed();await responseRelease;await route.abort('failed')
  })
  await dialog.getByRole('button',{name:'确认调序'}).click()
  await transactionCommitted; await page.goBack(); release()
  await page.getByRole('navigation',{name:'业务导航'}).getByRole('button',{name:'锚地排队',exact:true}).click()
  await expect(page.getByRole('button',{name:'核对原调序'})).toBeVisible()
  // 主队列读取失败后恢复，未确认请求仍应存在，不能因结果区卸载而丢失。
  await page.route('**/api/anchorages',route=>route.abort('failed'))
  await page.getByRole('button',{name:'刷新',exact:true}).click()
  await expect(page.getByRole('button',{name:'重新读取',exact:true})).toBeVisible()
  await page.unroute('**/api/anchorages')
  await page.getByRole('button',{name:'重新读取',exact:true}).click()
  await page.getByRole('button',{name:'核对原调序'}).click()
  await expect(dialog.locator('.aq-affected li')).toHaveCount(affectedBefore)
  await dialog.getByRole('button',{name:'读取最新队列'}).click()
  await expect(dialog).toContainText('最新队列与原调序预览一致')
  expect(droppedWrites).toBe(1)
  await dialog.getByRole('button',{name:'结束核对'}).click()
  expect((await read(page,id)).occupiedCount).toBe(initial.occupiedCount)
})

test('长队列触摸跨屏、Esc取消、键盘等价与减少动态效果',async({page})=>{
  const id = await openQueue(page), initial = await read(page,id)
  await page.setViewportSize({width:390,height:844})
  await page.locator('.aq-grab').first().scrollIntoViewIfNeeded()
  const from = (await page.locator('.aq-grab').first().boundingBox())!, box = (await page.locator('.aq-sea').boundingBox())!
  const cdp = await page.context().newCDPSession(page)
  await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:from.x+22,y:from.y+22}]})
  await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:box.x+box.width-10,y:from.y+22}]})
  await expect.poll(()=>page.locator('.aq-sea').evaluate(el=>el.scrollLeft)).toBeGreaterThan(300)
  await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]})
  const dialog = page.locator('.aq-reorder[open]')
  await expect(dialog).toBeVisible()
  expect(Number(await dialog.locator('#aq-new-position').inputValue())).toBeGreaterThan(2)
  expect(await dialog.evaluate(el=>el.getBoundingClientRect().right<=innerWidth && el.getBoundingClientRect().left>=0)).toBeTruthy()
  await dialog.getByRole('button',{name:'取消',exact:true}).scrollIntoViewIfNeeded()
  await page.screenshot({path:'../../artifacts/stage4/4d-mobile-confirm.png'})
  await dialog.getByRole('button',{name:'取消',exact:true}).click()
  expect((await read(page,id)).snapshot).toBe(initial.snapshot)
  await page.setViewportSize({width:1440,height:900}); await page.locator('.aq-sea').evaluate(el=>el.scrollLeft=0)
  const handle = page.locator('.aq-grab').nth(2), point = (await handle.boundingBox())!
  await page.mouse.move(point.x+22,point.y+22); await page.mouse.down(); await page.mouse.move(point.x+150,point.y+22)
  await page.keyboard.press('Escape'); await page.mouse.up()
  await expect(dialog).toHaveCount(0)
  await handle.focus(); await page.keyboard.press('Enter'); await expect(dialog).toBeVisible()
  await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0)
  await page.emulateMedia({reducedMotion:'reduce'})
  expect(await page.locator('.aq-vessel').first().evaluate(el=>getComputedStyle(el).transitionDuration)).toBe('0s')
  expect((await read(page,id)).snapshot).toBe(initial.snapshot)
})

test('第四阶段桌面、短屏、宽屏与窄屏截图',async({page})=>{
  await openQueue(page); mkdirSync('../../artifacts/stage4',{recursive:true})
  for (const [name,width,height] of [['desktop',1440,900],['short',1366,768],['wide',1920,1080],['mobile',390,844],['narrow',320,844]] as const) {
    await page.setViewportSize({width,height}); await expect(page.locator('.aq-journey')).toBeVisible()
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy()
    if(width>1000) expect(await page.locator('.aq-journey').evaluate(el=>el.getBoundingClientRect().bottom)).toBeLessThanOrEqual(height-20)
    await page.screenshot({path:`../../artifacts/stage4/4d-${name}.png`,fullPage:true})
  }
  await page.setViewportSize({width:1440,height:900}); await dragFifthToSecond(page)
  await page.screenshot({path:'../../artifacts/stage4/4d-confirm.png'})
  await page.getByRole('button',{name:'取消',exact:true}).click()
})
