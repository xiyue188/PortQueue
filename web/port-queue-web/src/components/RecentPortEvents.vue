<script setup lang="ts">
// 标题独立于滚动视口；轮播读取真实事件分页，手动阅读和隐藏时不争抢滚动。
import { computed, nextTick, onMounted, onScopeDispose, ref, watch } from 'vue'
import { eventLabels, type PortOverview } from '../composables/useOverview'
import { formatDate } from '../utils/appointmentPresentation'
import PortIcon from './PortIcon.vue'

const props = defineProps<{ overview: PortOverview | null; loading: boolean; error: string; active: boolean }>()
const emit = defineEmits<{ page: [page?: number]; select: [appointmentId: string] }>()
const viewport = ref<HTMLElement>()
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)')
const paused = ref(reducedMotion.matches)
const hovering = ref(false)
const focused = ref(false)
const visible = ref(false)
const hidden = ref(document.hidden)
const totalPages = computed(() => Math.max(1, Math.ceil((props.overview?.totalEvents ?? 0) / 12)))
const currentPage = computed(() => props.overview?.eventsPage ?? 1)
const range = computed(() => {
  const overview = props.overview
  if (!overview?.recentEvents.length) return `共 ${overview?.totalEvents ?? 0} 条`
  const first = (overview.eventsPage - 1) * overview.eventsPageSize + 1
  return `${first}–${first + overview.recentEvents.length - 1} / ${overview.totalEvents}`
})
const running = computed(() => props.active && visible.value && !hidden.value && !paused.value
  && !hovering.value && !focused.value && !props.loading && !props.error && !!props.overview?.recentEvents.length)
let frame = 0
let last = 0
let position = 0
let holdUntil = 0
let observer: IntersectionObserver | undefined

function stop() { cancelAnimationFrame(frame); last = 0 }
function tick(now: number) {
  const element = viewport.value
  if (!element || !running.value) return
  const elapsed = last ? Math.min(now - last, 64) : 0
  last = now
  const end = element.scrollHeight - element.clientHeight
  if (now >= holdUntil) {
    if (end > 0 && element.scrollTop < end - 1) {
      position += elapsed * .018
      element.scrollTop = Math.min(end, position)
      if (element.scrollTop >= end - 1) holdUntil = now + 1800
    } else if (totalPages.value > 1) {
      emit('page', currentPage.value < totalPages.value ? currentPage.value + 1 : 1)
      return // 等待新数据，避免每帧重复请求。
    } else if (end > 0) {
      position = 0; element.scrollTop = 0; holdUntil = now + 1800
    }
  }
  frame = requestAnimationFrame(tick)
}
watch(running, value => {
  stop()
  if (value) {
    position = viewport.value?.scrollTop ?? 0
    holdUntil = performance.now() + 1800
    frame = requestAnimationFrame(tick)
  }
})
watch(() => props.overview?.recentEvents[0]?.eventId, async () => {
  await nextTick()
  if (viewport.value) viewport.value.scrollTop = 0
  position = 0
})
function manualPage(page: number) { paused.value = true; emit('page', page) }
function pauseReading() { paused.value = true }
function focusout(event: FocusEvent) {
  focused.value = event.relatedTarget instanceof Node && (event.currentTarget as HTMLElement).contains(event.relatedTarget)
}
function visibilityChange() { hidden.value = document.hidden }
function preferenceChange() { paused.value = reducedMotion.matches }
onMounted(() => {
  observer = new IntersectionObserver(entries => { visible.value = entries[0]?.isIntersecting ?? false }, { threshold: .15 })
  if (viewport.value) observer.observe(viewport.value)
  document.addEventListener('visibilitychange', visibilityChange)
  reducedMotion.addEventListener('change', preferenceChange)
})
onScopeDispose(() => {
  stop(); observer?.disconnect()
  document.removeEventListener('visibilitychange', visibilityChange)
  reducedMotion.removeEventListener('change', preferenceChange)
})
</script>

<template>
  <section class="glass-panel recent-events" aria-labelledby="events-title" :aria-busy="loading">
    <div class="panel-heading events-heading">
      <h2 id="events-title">港口动态</h2>
      <div class="event-controls">
        <span class="event-range">{{ overview ? range : '—' }}</span>
        <button class="icon-button" :disabled="loading || !overview?.recentEvents.length" :aria-label="paused ? '继续动态滚动' : '暂停动态滚动'" :title="paused ? '继续滚动' : '暂停滚动'" @click="paused = !paused"><PortIcon :name="paused ? 'play' : 'pause'" :size="16" /></button>
        <button class="icon-button" aria-label="较新动态" :disabled="loading || currentPage <= 1" @click="manualPage(currentPage - 1)"><PortIcon name="left" :size="16" /></button>
        <button class="icon-button" aria-label="较早动态" :disabled="loading || currentPage >= totalPages" @click="manualPage(currentPage + 1)"><PortIcon name="right" :size="16" /></button>
      </div>
    </div>
    <div ref="viewport" class="events-viewport" tabindex="0" role="region" aria-label="港口动态记录"
      @pointerenter="hovering = $event.pointerType === 'mouse'" @pointerleave="hovering = false"
      @focusin="focused = true" @focusout="focusout" @wheel.passive="pauseReading" @touchstart.passive="pauseReading">
      <p v-if="error" role="alert">{{ error }} <button class="text-action" @click="emit('page')">重试</button></p>
      <ol v-else-if="overview?.recentEvents.length">
        <li v-for="event in overview.recentEvents" :key="event.eventId">
          <PortIcon :name="event.eventTypeCode.includes('ANCHORAGE') ? 'anchor' : 'ship'" :size="19" />
          <button class="event-ship" :aria-label="`查看${event.shipName}预约 ${event.appointmentNo}`" @click="emit('select', event.appointmentId)">{{ event.shipName }}</button>
          <span>{{ eventLabels[event.eventTypeCode] }}</span><time>{{ formatDate(event.eventTime) }}</time>
        </li>
      </ol>
      <p v-else role="status">{{ loading ? '正在读取动态…' : '暂无进出港记录' }}</p>
    </div>
  </section>
</template>
