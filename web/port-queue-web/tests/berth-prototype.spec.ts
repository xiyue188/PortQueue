// 第五阶段原型的用户任务验收：直接打开HTML，不依赖业务API或数据库写入。
import { test, expect, type Page } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { mkdirSync } from 'node:fs'

const prototype = pathToFileURL(resolve(process.cwd(), '../../docs/design/berth-prototype/index.html')).href
const evidence = resolve(process.cwd(), '../../.impeccable/review/berth')
mkdirSync(evidence, { recursive: true })
test.beforeEach(async ({ page }) => { await page.goto(prototype) })

test('现有Vite预览入口能加载相同原型、素材和操作',async({page})=>{
  const errors:string[]=[];page.on('pageerror',error=>errors.push(error.message))
  await page.goto(`/@fs${new URL(prototype).pathname}`)
  await expect(page).toHaveTitle('PortQueue · 泊位排期交互原型')
  await expect(page.locator('#detail-title')).toHaveText('星洲明珠')
  const loaded=await page.locator('.detail-scene').evaluate(el=>new Promise<boolean>(resolve=>{
    const image=new Image();image.onload=()=>resolve(image.naturalWidth>0);image.onerror=()=>resolve(false)
    image.src=getComputedStyle(el).backgroundImage.slice(5,-2)
  }))
  expect(loaded).toBe(true)
  await page.locator('#edit-arrangement').click()
  await expect(page.locator('#edit-dialog')).toBeVisible()
  expect(errors).toEqual([])
})

async function choose(page: Page, name: string) {
  await page.locator('#search').fill(name)
  await page.locator('#search-results button').click()
  await expect(page.locator('#detail-title')).toHaveText(name)
}
async function edit(page: Page, berth: string, start: string, end: string) {
  await page.locator('#edit-arrangement').click()
  await page.locator('#edit-berth').selectOption(berth)
  await page.locator('#edit-start').fill(start)
  await page.locator('#edit-end').fill(end)
  await page.locator('#edit-reason').fill('配合本次船舶作业安排')
}
async function dragBar(page: Page, target: 'body'|'handle', deltaX: number, lane?: string) {
  const bar=page.locator('.plan-bar[data-visit="linGang"][data-kind="current"]')
  await bar.scrollIntoViewIfNeeded()
  const box=(await bar.boundingBox())!
  const x=box.x+(target==='handle'?20:100), y=box.y+25
  const laneBox=lane?await page.locator(`[data-lane="${lane}"]`).boundingBox():null
  await page.mouse.move(x,y)
  await page.mouse.down()
  await page.mouse.move(x+deltaX,laneBox?laneBox.y+45:y,{steps:12})
  await expect(page.locator('#drag-feedback')).toBeVisible()
}

test('人工预计、确认计划和候选各自表达来源，候选不覆盖旧确认',async({page})=>{
  await expect(page.locator('#detail')).toContainText('人工预计 11/28 16:00')
  await page.locator('#empty-window').click()
  await expect(page.locator('#edit-start')).toHaveValue('2026-11-28T16:00')
  await page.locator('#edit-reason').fill('等待二号泊位作业结束')
  await page.locator('#apply').click()
  await expect(page.locator('.candidate[data-visit="xingzhou"]')).toHaveCount(1)
  await expect(page.locator('#detail')).toContainText('已有候选安排')
  await choose(page,'江澜远航')
  await expect(page.locator('#detail .arrangement')).toContainText('14:00')
  await expect(page.locator('#detail .reference')).not.toHaveAttribute('open')
  await edit(page,'b2','2026-11-29T00:07','2026-11-29T10:07')
  await page.locator('#apply').click()
  await expect(page.locator('#detail .arrangement')).toContainText('14:00')
  await expect(page.locator('#detail .candidate-note')).toContainText('00:07')
  await expect(page.locator('#detail .candidate-note')).toContainText('原确认安排仍有效')
  await page.reload()
  await expect(page.locator('.plan-bar.candidate')).toHaveCount(0)
})

test('时段冲突、实际未离泊、设施不符分别给出具体原因，相接可用',async({page})=>{
  await edit(page,'b2','2026-11-28T15:00','2026-11-28T23:00')
  await expect(page.locator('#validation')).toContainText('临港晓风')
  await expect(page.locator('#apply')).toBeDisabled()
  await page.locator('#edit-start').fill('2026-11-28T16:00')
  await expect(page.locator('#apply')).toBeEnabled()
  await page.locator('#edit-berth').selectOption('b1')
  await expect(page.locator('#validation')).toContainText('海晏远行尚未离泊')
  await expect(page.locator('#apply')).toBeDisabled()
  await page.locator('#edit-berth').selectOption('b3')
  await expect(page.locator('#validation')).toContainText('不支持集装箱船')
  await expect(page.locator('#validation')).toContainText('超过B-03可用长度')
  await expect(page.locator('#validation')).toContainText('超过B-03上限')
})

test('条身与把手均能拖动，15分钟吸附；取消不改变原安排',async({page})=>{
  await choose(page,'临港晓风')
  for(const target of ['body','handle'] as const){
    await dragBar(page,target,42)
    await expect(page.locator('.drag-ghost')).toBeVisible()
    await page.mouse.up()
    await expect(page.locator('#edit-dialog')).toBeVisible()
    const value=await page.locator('#edit-start').inputValue()
    expect(Number(value.slice(-2))%15).toBe(0)
    expect(value).not.toBe('2026-11-28T11:00')
    await page.getByRole('button',{name:'取消',exact:true}).click()
    await expect(page.locator('.plan-bar.candidate')).toHaveCount(0)
    await expect(page.locator('#detail .arrangement')).toContainText('11:00')
  }
})

test('纵向换泊位有具体冲突；拖出资源区和Esc都不生成候选',async({page})=>{
  await choose(page,'临港晓风')
  await dragBar(page,'body',25,'b3')
  await page.mouse.up()
  await expect(page.locator('#edit-berth')).toHaveValue('b3')
  await expect(page.locator('#validation')).toContainText('不支持集装箱船')
  await page.keyboard.press('Escape')
  await dragBar(page,'body',30)
  await page.mouse.move(800,100,{steps:5})
  await expect(page.locator('#drag-feedback')).toContainText('当前落点不生成安排')
  await page.mouse.up()
  await expect(page.locator('#edit-dialog')).not.toBeVisible()
  await dragBar(page,'handle',30)
  await page.keyboard.press('Escape')
  await page.mouse.up()
  await expect(page.locator('.drag-ghost')).toHaveCount(0)
  await expect(page.locator('.plan-bar.candidate')).toHaveCount(0)
})

test('单击、搜索和历史定位只选择到访，不过滤其他资源；实际记录只读',async({page})=>{
  await choose(page,'临港晓风')
  await page.locator('.plan-bar[data-visit="linGang"]').click()
  await expect(page.locator('#edit-dialog')).not.toBeVisible()
  await expect(page.locator('.resource-row')).toHaveCount(3)
  await expect(page.locator('.plan-bar')).toHaveCount(5)
  await choose(page,'海宁嘉禾')
  await expect(page.locator('#detail')).toContainText('已完成')
  await expect(page.locator('#edit-arrangement')).toHaveCount(0)
  await page.locator('#visit').click()
  await expect(page.locator('#visit-content')).toContainText('实际离泊')
  await expect(page.locator('#visit-content')).toContainText('05:25')
})

test('跨日时间按比例，24/48切换不改数据，无计划日期与日期清空可恢复',async({page})=>{
  const width=async(id:string)=>(await page.locator(`.plan-bar[data-visit="${id}"]`).boundingBox())!.width
  expect((await width('jianglan'))/(await width('linGang'))).toBeCloseTo(2,1)
  await page.locator('[data-hours="48"]').click()
  // 远川跨日8小时，江澜10小时；24小时下远川只显示当日6小时。
  expect((await width('yuanchuan'))/(await width('jianglan'))).toBeCloseTo(.8,1)
  await page.locator('#date').fill('2026-12-02')
  await page.locator('#date').blur()
  await expect(page.locator('.plan-bar')).toHaveCount(0)
  await page.locator('[data-view="list"]').click()
  await expect(page.locator('#schedule-list')).toContainText('该时段没有安排')
  await page.locator('#date').fill('')
  await page.locator('#date').blur()
  await expect(page.locator('#date')).toHaveValue('2026-11-28')
  await expect(page.locator('.plan-bar')).toHaveCount(5)
})

test('键盘可独立安排，精确到分钟，不调用业务API',async({page})=>{
  const calls:string[]=[], errors:string[]=[]
  page.on('request',req=>{if(/^https?:/.test(req.url()))calls.push(req.url())})
  page.on('pageerror',err=>errors.push(err.message))
  await page.reload()
  await page.locator('#search').fill('浦江')
  await page.locator('#search').press('ArrowDown')
  await page.keyboard.press('Enter')
  await expect(page.locator('#detail-title')).toHaveText('浦江清和')
  await edit(page,'b3','2026-11-28T11:07','2026-11-28T15:07')
  await page.locator('#apply').focus()
  await page.keyboard.press('Enter')
  await expect(page.locator('#detail .candidate-note')).toContainText('11:07')
  expect(calls).toEqual([])
  expect(errors).toEqual([])
})

test('窄屏触摸条身可拖动，明细与表单提供独立办理入口',async({page})=>{
  await page.setViewportSize({width:390,height:844})
  await choose(page,'临港晓风')
  const bar=page.locator('.plan-bar[data-visit="linGang"]')
  await bar.scrollIntoViewIfNeeded()
  const b=(await bar.boundingBox())!
  const axis=(await page.locator('#axis-scroll').boundingBox())!
  const x=Math.max(axis.x+125,b.x+20),y=b.y+25
  const cdp=await page.context().newCDPSession(page)
  await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y}]})
  await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:x+30,y}]})
  await expect(page.locator('#drag-feedback')).toBeVisible()
  await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]})
  await expect(page.locator('#edit-dialog')).toBeVisible()
  await page.getByRole('button',{name:'取消',exact:true}).click()
  await page.locator('[data-view="list"]').click()
  await expect(page.locator('#schedule-list')).toContainText('临港晓风')
  await expect(page.locator('#schedule-list')).toContainText('11/28 11:00')
  await cdp.detach()
})

test('多个候选独立成轨，不遮挡旧安排；同内容不生成无意义版本',async({page})=>{
  await page.setViewportSize({width:1440,height:900})
  await choose(page,'江澜远航')
  await page.locator('#edit-arrangement').click()
  await expect(page.locator('#validation')).toContainText('安排未变化')
  await expect(page.locator('#apply')).toBeDisabled()
  await page.keyboard.press('Escape')
  await edit(page,'b2','2026-11-28T16:00','2026-11-29T02:00')
  await page.locator('#apply').click()
  await page.locator('[data-hours="48"]').click()
  await expect(page.locator('#edit-arrangement')).toBeInViewport()
  await expect(page.locator('#discard-candidate')).toBeInViewport()
  await expect(page.locator('#visit')).toBeInViewport()
  await page.screenshot({path:resolve(evidence,'candidate.png'),fullPage:true})
  await choose(page,'星洲明珠')
  await edit(page,'b2','2026-11-28T16:00','2026-11-29T00:00')
  await page.locator('#apply').click()
  const bars=await page.locator('.plan-bar.candidate').all()
  const first=(await bars[0].boundingBox())!,second=(await bars[1].boundingBox())!
  expect(Math.abs(first.y-second.y)).toBeGreaterThan(first.height)
  await choose(page,'星洲明珠')
  await edit(page,'b2','2026-11-28T15:00','2026-11-28T23:00')
  await page.screenshot({path:resolve(evidence,'conflict.png'),fullPage:true})
})

test('时间轴空闲段可直接选取，直接安排无需人工预计',async({page})=>{
  await choose(page,'浦江清和')
  const slot=page.locator('[data-free-berth="b3"]').first()
  await slot.scrollIntoViewIfNeeded()
  await slot.click({position:{x:45,y:30}})
  await expect(page.locator('#edit-ship')).toHaveText('浦江清和')
  await expect(page.locator('#edit-berth')).toHaveValue('b3')
  await expect(page.locator('#apply')).toBeEnabled()
})

test('实际未释放跨日期持续可见，明细与图形一致，人工预计不限制空闲查询',async({page})=>{
  await page.locator('#axis-scroll').evaluate(el=>el.scrollLeft=700)
  await expect(page.locator('[data-berth="b1"] .resource-label')).toContainText('尚未离泊')
  await expect(page.locator('.occupancy-pending button')).toBeInViewport()
  await page.locator('#date').fill('2026-11-29')
  await page.locator('#date').blur()
  await expect(page.locator('.occupancy-pending')).toBeVisible()
  await expect(page.locator('[data-free-berth="b1"]')).toHaveCount(0)
  await page.screenshot({path:resolve(evidence,'open-occupancy.png'),fullPage:true})
  await page.locator('[data-view="list"]').click()
  await expect(page.locator('#schedule-list')).toContainText('海晏远行')
  await expect(page.locator('#schedule-list')).toContainText('尚未离泊，结束待定')
  await page.screenshot({path:resolve(evidence,'occupancy-list.png'),fullPage:true})
  await page.locator('#date').fill('2026-11-30')
  await page.locator('#date').blur()
  // 人工预计仍是11/28 16:00；查找按所选日期/实际可用资源，不增加人工限制。
  await page.locator('#empty-window').click()
  await expect(page.locator('#edit-start')).toHaveValue('2026-11-30T00:00')
})

for(const [name,width,height] of [['desktop',1440,900],['short',1366,768],['wide',1920,1080],['mobile',390,844],['narrow',320,740]] as const){
  test(`布局证据 ${name} ${width}×${height}`,async({page})=>{
    const errors:string[]=[];page.on('pageerror',err=>errors.push(err.message))
    await page.setViewportSize({width,height})
    await page.reload()
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
    await expect(page.locator('#detail-title')).toBeVisible()
    await expect(page.locator('#edit-arrangement')).toBeVisible()
    if(width>=1366){
      const waiting=(await page.locator('.waiting-panel').boundingBox())!
      expect(waiting.y+waiting.height).toBeLessThanOrEqual(height-20)
      expect((await page.locator('#detail').boundingBox())!.width).toBeGreaterThanOrEqual(295)
      await expect(page.locator('#visit')).toBeInViewport()
    }else{
      await expect(page.locator('#quick-edit')).toBeInViewport()
      await expect(page.locator('#compact-task')).toContainText('星洲明珠')
    }
    const scene=await page.locator('.detail-scene').evaluate(el=>getComputedStyle(el).backgroundImage)
    expect(scene).toContain('port-scene.png')
    await page.screenshot({path:resolve(evidence,`${name}.png`),fullPage:true})
    expect(errors).toEqual([])
  })
}
