<script setup lang="ts">
// 既有业务视图的只读工作区；各实例保留自己的筛选和分页，离开页面不重新挂载。
import { onScopeDispose, ref, watch } from 'vue'
import PortIcon from './PortIcon.vue'
import { formatDate } from '../utils/appointmentPresentation'
import { eventLabels } from '../composables/useOverview'
const props = defineProps<{ kind: 'queue' | 'plans' | 'events'; active: boolean; refreshKey: number }>()
const emit = defineEmits<{ select: [appointmentId: string] }>()
interface Row { id: string; appointmentId: string; appointmentNo: string; shipName: string; location: string; status: string; startedAt: string | null; endedAt: string | null; position: number | null; version: number | null }
const rows = ref<Row[]>([]), total = ref(0), page = ref(1), keyword = ref(''), committed = ref(''), loading = ref(false), error = ref('')
const titles = { queue: '锚地排队', plans: '泊位计划', events: '进出港记录' }
const states: Record<string, string> = { WAITING: '等待分配', ASSIGNED: '已分配', LEFT: '已离开', CANCELLED: '已撤销', DRAFT: '草拟', CONFIRMED: '已确认', EXECUTING: '执行中', COMPLETED: '已完成', ...eventLabels }
let lastRequest = { page: 1, keyword: '' }
function retry() { void load(lastRequest.page, lastRequest.keyword) }
let visited = false, sequence = 0, controller: AbortController | undefined
async function load(next = page.value, query = committed.value) {
  lastRequest = { page: next, keyword: query }
  controller?.abort(); const request = new AbortController(); controller = request; const current = ++sequence
  loading.value = true; error.value = ''; const timer = setTimeout(() => request.abort(), 15_000)
  try {
    const response = await fetch(`/api/business/${props.kind}?${new URLSearchParams({ keyword: query, page: String(next) })}`, { signal: request.signal })
    if (!response.ok) throw new Error('读取失败')
    const data = await response.json()
    if (data.page !== next || data.pageSize !== 20 || !Number.isSafeInteger(data.totalCount) || data.totalCount < 0 || !Array.isArray(data.items)
      || !data.items.every((r: Row) => r && /^\d+$/.test(r.id) && /^\d+$/.test(r.appointmentId) && ['appointmentNo', 'shipName', 'location', 'status'].every(k => typeof r[k as keyof Row] === 'string'))) throw new Error('响应无效')
    if (current === sequence) { rows.value = data.items; total.value = data.totalCount; page.value = next; committed.value = query }
  } catch { if (current === sequence) { rows.value = []; total.value = 0; error.value = '记录暂时无法读取，请重试。' } }
  finally { clearTimeout(timer); if (current === sequence) loading.value = false }
}
watch(() => props.active, active => { if (active && !visited) { visited = true; void load() } }, { immediate: true })
watch(() => props.refreshKey, () => { if (props.active) void load() })
onScopeDispose(() => { sequence++; controller?.abort() })
</script>
<template>
  <section class="glass-panel business-records" :aria-label="titles[kind]" :aria-busy="loading">
    <div class="panel-heading"><h2>{{ titles[kind] }}</h2><span class="record-count">{{ error ? '—' : total }} 条</span></div>
    <form class="business-search" @submit.prevent="load(1, keyword.trim())"><label :for="`business-search-${kind}`" class="sr-only">{{ titles[kind] }}关键词</label><PortIcon name="search" :size="18" /><input :id="`business-search-${kind}`" v-model="keyword" maxlength="100" :placeholder="kind === 'events' ? '船名、预约编号、记录人' : '船名、预约编号、位置'" /><button type="submit" class="button button-primary" :disabled="loading">查询</button><button class="button" type="button" :disabled="loading" @click="keyword = ''; load(1, '')">重置</button></form>
    <p v-if="error" role="alert" class="business-error">{{ error }}<button class="text-action" @click="retry">重新读取</button></p>
    <div class="business-table-scroll" :class="{ 'is-loading': loading }">
      <table class="business-table"><thead><tr><th>船舶 / 预约</th><th>{{ kind === 'events' ? '记录人' : kind === 'queue' ? '锚地' : '泊位' }}</th><th>{{ kind === 'events' ? '事件' : '状态' }}</th><th v-if="kind !== 'events'">{{ kind === 'queue' ? '队列位置' : '当前版本' }}</th><th>{{ kind === 'queue' ? '实际进锚' : kind === 'plans' ? '计划靠泊' : '发生时间' }}</th><th v-if="kind !== 'events'">{{ kind === 'queue' ? '实际离锚' : '计划离泊' }}</th><th><span class="sr-only">操作</span></th></tr></thead>
        <tbody :inert="loading || undefined"><tr v-for="row in rows" :key="row.id"><td><strong>{{ row.shipName }}</strong><small>{{ row.appointmentNo }}</small></td><td>{{ row.location }}</td><td>{{ states[row.status] ?? row.status }}</td><td v-if="kind !== 'events'">{{ (kind === 'queue' ? row.position : row.version) ?? '—' }}</td><td>{{ row.startedAt ? formatDate(row.startedAt) : '—' }}</td><td v-if="kind !== 'events'">{{ row.endedAt ? formatDate(row.endedAt) : '—' }}</td><td><button class="text-action" :aria-label="`查看${row.shipName}的预约`" @click="emit('select', row.appointmentId)">查看预约<PortIcon name="arrow" :size="14" /></button></td></tr></tbody></table>
      <p v-if="!rows.length && !error" class="business-empty" role="status">{{ loading ? '正在读取…' : '没有符合条件的记录' }}</p>
    </div>
    <footer class="pagination"><span role="status">第 {{ page }} 页，共 {{ Math.max(1, Math.ceil(total / 20)) }} 页</span><div class="pagination-controls"><button class="button" :disabled="loading || !!error || page <= 1" @click="load(page - 1)">上一页</button><button class="button" :disabled="loading || !!error || page * 20 >= total" @click="load(page + 1)">下一页</button></div></footer>
  </section>
</template>
