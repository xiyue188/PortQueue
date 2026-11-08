<script setup lang="ts">
// 候选复用真实查询 API；防抖、取消与请求序号共同隔离旧响应，不改变主清单筛选。
import { nextTick, onMounted, onScopeDispose, ref, watch } from 'vue'
import { fetchAppointments } from '../api/appointments'
import type { AppointmentItem } from '../types/appointment'
import { formatDate, statusClass, statusLabel } from '../utils/appointmentPresentation'
import PortIcon from './PortIcon.vue'

const props = defineProps<{ modelValue: string }>()
const emit = defineEmits<{
  'update:modelValue': [value: string]
  search: []
  select: [item: AppointmentItem]
}>()
const root = ref<HTMLElement>()
const input = ref<HTMLInputElement>()
const open = ref(false)
const composing = ref(false)
const loading = ref(false)
const error = ref('')
const candidates = ref<AppointmentItem[]>([])
const active = ref(-1)
let debounce: ReturnType<typeof setTimeout> | undefined
let controller: AbortController | undefined
let sequence = 0

function close() {
  open.value = false
  active.value = -1
  clearTimeout(debounce)
  controller?.abort()
  sequence++
  loading.value = false
}
function schedule(delay = 220) {
  clearTimeout(debounce)
  controller?.abort()
  const current = ++sequence
  candidates.value = []
  active.value = -1
  error.value = ''
  if (!open.value || composing.value) return
  loading.value = true
  debounce = setTimeout(async () => {
    const request = new AbortController()
    controller = request
    const timeout = setTimeout(() => request.abort(), 8_000)
    try {
      const result = await fetchAppointments({ page: 1, pageSize: 6, status: '', keyword: props.modelValue.trim() }, request.signal)
      if (current === sequence) candidates.value = result.items
    } catch {
      if (current === sequence) error.value = '暂时无法读取候选，请重试。'
    } finally {
      clearTimeout(timeout)
      if (current === sequence) loading.value = false
    }
  }, delay)
}
function show() { if (!open.value) { open.value = true; schedule(0) } }
function choose(item: AppointmentItem) {
  close()
  emit('update:modelValue', item.appointmentNo)
  emit('select', item)
}
function submit() {
  if (composing.value) return
  close()
  emit('search')
}
async function keydown(event: KeyboardEvent) {
  if (event.isComposing || composing.value || event.keyCode === 229) return
  if (event.key === 'Escape') { event.preventDefault(); close(); return }
  if (event.key === 'Enter' && open.value && active.value >= 0 && candidates.value[active.value]) {
    event.preventDefault(); choose(candidates.value[active.value]); return
  }
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    if (!open.value) { show(); return }
    const count = candidates.value.length
    if (!count) return
    active.value = event.key === 'ArrowDown' ? (active.value + 1) % count : (active.value <= 0 ? count - 1 : active.value - 1)
    await nextTick()
    document.getElementById(`appointment-option-${active.value}`)?.scrollIntoView({ block: 'nearest' })
  }
}
function outside(event: PointerEvent) { if (event.target instanceof Node && !root.value?.contains(event.target)) close() }
function focusout(event: FocusEvent) { if (!(event.relatedTarget instanceof Node) || !root.value?.contains(event.relatedTarget)) close() }
function clear() { emit('update:modelValue', ''); input.value?.focus(); show() }
// 重试会卸载错误区按钮；先把焦点还给输入框，避免 focusout 把浮层关闭并取消请求。
function retry() { input.value?.focus(); schedule(0) }
watch(() => props.modelValue, () => { if (open.value) schedule() })
onMounted(() => document.addEventListener('pointerdown', outside))
onScopeDispose(() => { close(); document.removeEventListener('pointerdown', outside) })
</script>

<template>
  <form ref="root" class="header-search search-combobox" role="search" aria-label="预约查询" @submit.prevent="submit" @focusout="focusout">
    <PortIcon name="search" :size="18" />
    <label class="sr-only" for="appointment-keyword">预约关键词</label>
    <input ref="input" id="appointment-keyword" :value="modelValue" role="combobox" type="text" maxlength="100"
      placeholder="选择或搜索船舶、预约编号…" autocomplete="off" aria-autocomplete="list" aria-controls="appointment-suggestions"
      :aria-expanded="open" :aria-activedescendant="open && active >= 0 ? `appointment-option-${active}` : undefined"
      @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)" @focus="show" @click="show" @keydown="keydown"
      @compositionstart="composing = true; schedule()" @compositionend="composing = false; schedule()" />
    <button v-if="modelValue" type="button" class="search-clear" aria-label="清空关键词" @click="clear"><PortIcon name="reset" :size="15" /></button>
    <button type="submit" class="search-submit">查询</button>
    <div v-show="open" class="search-popover">
      <div id="appointment-suggestions" role="listbox" aria-label="预约候选" :aria-busy="loading">
        <div v-for="(item, index) in candidates" :id="`appointment-option-${index}`" :key="item.appointmentId"
          role="option" :aria-selected="active === index" class="search-option"
          @pointerdown.prevent @click="choose(item)" @pointermove="active = index">
          <div><strong>{{ item.shipName }}</strong><span>{{ item.appointmentNo }}</span><time>{{ formatDate(item.requestedArrivalAt) }}</time></div>
          <span class="status-text" :class="statusClass(item.appointmentStatusCode)">{{ statusLabel(item.appointmentStatusCode) }}</span>
        </div>
      </div>
      <p v-if="loading" class="search-message" role="status">正在查找…</p>
      <div v-else-if="error" class="search-message" role="alert">{{ error }} <button type="button" class="text-action" @click="retry">重试</button></div>
      <p v-else-if="!candidates.length" class="search-message" role="status">没有匹配的预约</p>
    </div>
  </form>
</template>
