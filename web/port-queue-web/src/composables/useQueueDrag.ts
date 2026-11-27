// 指针只改变预览；事务提交由确认窗口负责。把手独占拖动，船体仍可选择与滚动。
import { onMounted, onScopeDispose, ref, type Ref } from 'vue'
import type { AnchorageStay } from '../types/anchorage'
export function moveQueue(ids: string[], id: string, index: number) {
  const next = ids.filter(value => value !== id)
  next.splice(Math.max(0,Math.min(index,next.length)),0,id)
  return next
}
export function useQueueDrag(lane: Ref<HTMLElement | undefined>, items: () => AnchorageStay[], allowed: () => boolean,
  preview: Ref<string[] | null>, select: (id: string) => void, confirm: (id: string, position: number) => void) {
  const draggingId = ref('')
  let drag: { id: string; pointer: number; handle: HTMLElement; element: HTMLElement; original: string[];
    x: number; y: number; startX: number; startY: number; scroll: number; moved: boolean; frame: number; last: number } | undefined

  function begin(event: PointerEvent, id: string) {
    if (!allowed() || items().length < 2 || !lane.value || (event.pointerType === 'mouse' && event.button !== 0)) return
    const handle = event.currentTarget as HTMLElement, element = handle.closest<HTMLElement>('.aq-vessel')!
    drag = { id, pointer:event.pointerId, handle, element, original:items().map(item=>item.queueEntryId),
      x:event.clientX, y:event.clientY, startX:event.clientX, startY:event.clientY, scroll:lane.value.scrollLeft, moved:false, frame:0, last:0 }
    preview.value = [...drag.original]; select(id)
    handle.setPointerCapture(event.pointerId); event.preventDefault()
  }
  function position() {
    if (!drag || !lane.value) return
    const content = lane.value.querySelector<HTMLElement>('.aq-lane')!
    const box = content.getBoundingClientRect(), width = box.width / Math.max(5,drag.original.length)
    drag.element.style.transform = `translate(${drag.x-drag.startX+lane.value.scrollLeft-drag.scroll}px,${Math.max(-18,Math.min(20,drag.y-drag.startY))}px)`
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
    drag.moved = true; drag.x = event.clientX; drag.y = event.clientY; draggingId.value = drag.id
    position(); if (!drag.frame) drag.frame = requestAnimationFrame(scrollEdge)
  }
  function end(event?: PointerEvent, cancelled = false) {
    if (!drag || (event && event.pointerId !== drag.pointer)) return
    const finished = drag, destination = preview.value?.indexOf(drag.id) ?? -1
    drag = undefined; cancelAnimationFrame(finished.frame); draggingId.value = ''; finished.element.style.transform = ''
    if (finished.handle.hasPointerCapture(finished.pointer)) finished.handle.releasePointerCapture(finished.pointer)
    if (cancelled || (finished.moved && destination === finished.original.indexOf(finished.id))) { preview.value = null; return }
    confirm(finished.id,destination+1)
  }
  function cancel() { end(undefined,true) }
  function escape(event: KeyboardEvent) { if (event.key === 'Escape' && drag) { event.preventDefault(); cancel() } }
  onMounted(()=>window.addEventListener('keydown',escape))
  onScopeDispose(()=>{ cancel(); window.removeEventListener('keydown',escape) })
  return { draggingId, begin, move, end, cancel }
}
