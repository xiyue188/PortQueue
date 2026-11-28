// 船体和把手共用指针手势；点击选择，移动超过阈值才预览，事务仍由确认窗口提交。
import { onMounted, onScopeDispose, ref, type Ref } from 'vue'
import type { AnchorageStay } from '../types/anchorage'
export function moveQueue(ids: string[], id: string, index: number) {
  const next = ids.filter(value => value !== id)
  next.splice(Math.max(0,Math.min(index,next.length)),0,id)
  return next
}
export function useQueueDrag(lane: Ref<HTMLElement | undefined>, items: () => AnchorageStay[], allowed: () => boolean,
  preview: Ref<string[] | null>, select: (id: string) => void, confirm: (id: string, position: number) => void) {
  const draggingId = ref(''), interacting = ref(false)
  let suppressClick = false
  let drag: { id: string; pointer: number; handle: HTMLElement; element: HTMLElement; original: string[];
    x: number; y: number; startX: number; startY: number; offsetX: number; scroll: number; moved: boolean; frame: number; last: number } | undefined

  function begin(event: PointerEvent, id: string) {
    if (drag || !event.isPrimary) return
    suppressClick = false
    if (!allowed() || items().length < 2 || !lane.value || (event.pointerType === 'mouse' && event.button !== 0)) return
    const handle = event.currentTarget as HTMLElement, element = handle.closest<HTMLElement>('.aq-vessel')!
    const original = items().map(item=>item.queueEntryId)
    const bounds = lane.value.querySelector<HTMLElement>('.aq-lane')!.getBoundingClientRect()
    // 取消后的让位动画可能尚未结束。从眼前位置抓起，不能跳回逻辑格再开始。
    const offsetX = element.getBoundingClientRect().left-bounds.left-bounds.width/original.length*original.indexOf(id)
    drag = { id, pointer:event.pointerId, handle, element, original, offsetX,
      x:event.clientX, y:event.clientY, startX:event.clientX, startY:event.clientY, scroll:lane.value.scrollLeft, moved:false, frame:0, last:0 }
    interacting.value = true; draggingId.value = id
    element.style.transform = `translate(${offsetX}px,0px)`
    // 不在按下时请求详情，避免异步选择触发定位或改变布局；也保留原生按钮焦点。
    handle.setPointerCapture(event.pointerId)
  }
  function position() {
    if (!drag || !lane.value) return
    const content = lane.value.querySelector<HTMLElement>('.aq-lane')!
    const box = content.getBoundingClientRect(), width = box.width / drag.original.length
    drag.element.style.transform = `translate(${drag.offsetX+drag.x-drag.startX+lane.value.scrollLeft-drag.scroll}px,${Math.max(-18,Math.min(20,drag.y-drag.startY))}px)`
    const index = Math.max(0,Math.min(drag.original.length-1,Math.floor((drag.x-box.left)/width)))
    preview.value = moveQueue(drag.original,drag.id,index)
  }
  function scrollEdge(time: number) {
    if (!drag || !lane.value) return
    drag.frame = 0
    const box = lane.value.getBoundingClientRect(), edge = 48
    const velocity = drag.x < box.left+edge ? -Math.min(1,(box.left+edge-drag.x)/edge)
      : drag.x > box.right-edge ? Math.min(1,(drag.x-box.right+edge)/edge) : 0
    const elapsed = drag.last ? Math.min(32,time-drag.last) : 16; drag.last = time
    const before = lane.value.scrollLeft
    lane.value.scrollLeft += velocity*420*elapsed/1000
    position()
    if (velocity && lane.value.scrollLeft !== before) drag.frame = requestAnimationFrame(scrollEdge)
  }
  function move(event: PointerEvent) {
    if (!drag || event.pointerId !== drag.pointer) return
    if (!drag.moved && Math.hypot(event.clientX-drag.startX,event.clientY-drag.startY) < 6) return
    if (!drag.moved) { preview.value = [...drag.original]; select(drag.id) }
    drag.moved = true; drag.x = event.clientX; drag.y = event.clientY; draggingId.value = drag.id
    position(); if (!drag.frame) drag.frame = requestAnimationFrame(scrollEdge)
  }
  function end(event?: PointerEvent, cancelled = false) {
    if (!drag || (event && event.pointerId !== drag.pointer)) return
    const finished = drag, destination = preview.value?.indexOf(drag.id) ?? -1
    drag = undefined; interacting.value = false; suppressClick = finished.moved || cancelled
    cancelAnimationFrame(finished.frame); draggingId.value = ''; finished.element.style.transform = ''
    if (finished.handle.hasPointerCapture(finished.pointer)) finished.handle.releasePointerCapture(finished.pointer)
    if (cancelled || !finished.moved || destination === finished.original.indexOf(finished.id)) { preview.value = null; return }
    confirm(finished.id,destination+1)
  }
  function click(event: MouseEvent, action: () => void) {
    // 拖动结束后浏览器还会派发click，不能将它误当成新的选择或再次打开窗口。
    if (suppressClick && event.detail !== 0) { suppressClick = false; return }
    suppressClick = false; action()
  }
  function cancel() { end(undefined,true) }
  function escape(event: KeyboardEvent) { if (event.key === 'Escape' && drag) { event.preventDefault(); cancel() } }
  onMounted(()=>window.addEventListener('keydown',escape))
  onScopeDispose(()=>{ cancel(); window.removeEventListener('keydown',escape) })
  return { draggingId, interacting, begin, move, end, cancel, click }
}
