<script setup lang="ts">
// 可搜索真实基础资料并翻页；选择值与输入文本分离，取消旧请求，支持中文输入法和键盘。
import { nextTick, onScopeDispose, ref, watch } from 'vue'
export interface ReferenceOption { id: string; name: string; detail: string; maxDraftM: number | null }
const props = defineProps<{ modelValue: ReferenceOption | null; kind: 'ships' | 'ports'; label: string; id: string; required?: boolean; disabled?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: ReferenceOption | null] }>()
const input = ref<HTMLInputElement>(), host = ref<HTMLElement>(), text = ref(props.modelValue?.name ?? ''), open = ref(false), items = ref<ReferenceOption[]>([]), total = ref(0), page = ref(1), active = ref(-1), loading = ref(false), error = ref('')
let controller: AbortController | undefined, timer: ReturnType<typeof setTimeout> | undefined, sequence = 0, query = ''
const composing = ref(false)
watch(() => props.modelValue, value => { if (value) text.value = value.name; else if (!open.value) text.value = '' })
watch([() => props.modelValue, text], () => input.value?.setCustomValidity(text.value.trim() && !props.modelValue ? '请从搜索结果中选择有效资料。' : ''), { flush: 'post' })
async function load(keyword = query, next = 1) {
  controller?.abort(); const request = new AbortController(); controller = request; const current = ++sequence
  query = keyword; loading.value = true; error.value = ''; active.value = -1
  const timeout = setTimeout(() => request.abort(), 12_000)
  try {
    const response = await fetch(`/api/${props.kind}?${new URLSearchParams({ keyword, page: String(next) })}`, { signal: request.signal })
    const data = await response.json()
    if (!response.ok || !Array.isArray(data.items) || !Number.isSafeInteger(data.totalCount) || data.page !== next || data.pageSize !== 20
      || !data.items.every((i: ReferenceOption) => i && /^\d+$/.test(i.id) && typeof i.name === 'string' && typeof i.detail === 'string' && (i.maxDraftM === null || Number.isFinite(i.maxDraftM)))) throw new Error('候选无效')
    if (current === sequence) { items.value = data.items; total.value = data.totalCount; page.value = next }
  } catch { if (current === sequence) { items.value = []; total.value = 0; error.value = '无法读取候选，请重试。' } }
  finally { clearTimeout(timeout); if (current === sequence) loading.value = false }
}
function focus() { open.value = true; void load(props.modelValue ? '' : text.value.trim()) }
function changed() {
  emit('update:modelValue', null); clearTimeout(timer); controller?.abort(); sequence++; items.value = []; open.value = true
  if (!composing.value) timer = setTimeout(() => void load(text.value.trim()), 180)
}
function choose(option: ReferenceOption) { emit('update:modelValue', option); text.value = option.name; open.value = false; input.value?.focus() }
function keydown(event: KeyboardEvent) {
  if (event.isComposing || composing.value) return
  if (event.key === 'Escape' && open.value) { event.preventDefault(); event.stopPropagation(); open.value = false; return }
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault(); if (!open.value) { focus(); return }
    active.value = Math.max(0, Math.min(items.value.length - 1, active.value + (event.key === 'ArrowDown' ? 1 : -1)))
    void nextTick(() => document.getElementById(`${props.id}-option-${active.value}`)?.scrollIntoView({ block: 'nearest' }))
  }
  if (event.key === 'Enter' && open.value) { event.preventDefault(); if (items.value[active.value]) choose(items.value[active.value]!) }
}
function leave(event: FocusEvent) { if (!host.value?.contains(event.relatedTarget as Node)) open.value = false }
onScopeDispose(() => { sequence++; controller?.abort(); clearTimeout(timer) })
</script>
<template>
  <div ref="host" class="reference-select" @focusout="leave">
    <label :for="id">{{ label }}<span v-if="required" aria-hidden="true"> *</span></label>
    <input :id="id" ref="input" v-model="text" :disabled="disabled" :required="required" :placeholder="kind === 'ships' ? '搜索船名、IMO 或 MMSI' : '搜索港口名称或代码'" autocomplete="off" maxlength="100" role="combobox" :aria-expanded="open" :aria-controls="`${id}-options`" :aria-activedescendant="active >= 0 ? `${id}-option-${active}` : undefined" @focus="focus" @input="changed" @keydown="keydown" @compositionstart="composing = true" @compositionend="composing = false; changed()" />
    <div v-if="open" class="reference-popup"><p v-if="loading" role="status">正在读取…</p><p v-else-if="error" role="alert">{{ error }}<button type="button" @click="load()">重试</button></p>
      <ul :id="`${id}-options`" role="listbox" :aria-label="label"><li v-for="(item, index) in items" :id="`${id}-option-${index}`" :key="item.id" role="option" :aria-selected="modelValue?.id === item.id" :class="{ 'is-active': active === index }" @mousedown.prevent @click="choose(item)"><strong>{{ item.name }}</strong><span>{{ item.detail }}</span></li></ul>
      <p v-if="!loading && !error && !items.length">没有符合条件的{{ kind === 'ships' ? '船舶' : '港口' }}</p>
      <footer v-if="!error"><span>共 {{ total }} 条</span><button type="button" :disabled="loading || page <= 1" @click="load(query, page - 1)">上一页</button><button type="button" :disabled="loading || page * 20 >= total" @click="load(query, page + 1)">下一页</button></footer>
    </div>
  </div>
</template>
