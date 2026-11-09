<script setup lang="ts">
// 统一列表容器：筛选与分页始终保留，加载只覆盖记录区，不卸载已有表格。
import AppointmentTable from './AppointmentTable.vue'
import PortIcon from './PortIcon.vue'
import { appointmentStatuses, type AppointmentItem, type AppointmentQuery, type AppointmentStatus } from '../types/appointment'

const props = defineProps<{
  rows: AppointmentItem[]; query: AppointmentQuery; selectedId?: string
  loading: boolean; error: string; totalCount: number; totalPages: number; compact?: boolean
}>()
const emit = defineEmits<{
  select: [item: AppointmentItem]; filter: [status: AppointmentStatus | '']
  load: [query?: AppointmentQuery]; reset: []; all: []
  'clear-keyword': []
}>()
const tabs = [{ label: '全部', code: '' }, { label: '待审核', code: 'PENDING' },
  { label: '已批准', code: 'APPROVED' }, { label: '已完成', code: 'COMPLETED' }] as const
function changeStatus(event: Event) { emit('filter', (event.target as HTMLSelectElement).value as AppointmentStatus | '') }
function changePageSize(event: Event) { emit('load', { ...props.query, page: 1, pageSize: Number((event.target as HTMLSelectElement).value) }) }
</script>

<template>
  <section class="glass-panel records-panel" :aria-label="compact ? '预约动态' : '预约清单'" :aria-busy="loading">
    <div class="panel-heading records-heading">
      <h2>{{ compact ? '预约动态' : '预约清单' }}</h2>
      <button v-if="!compact" class="icon-button" aria-label="重置全部筛选" title="重置全部筛选" @click="emit('reset')"><PortIcon name="reset" :size="17" /></button>
      <button v-else class="text-action" @click="emit('all')">查看全部预约<PortIcon name="arrow" :size="16" /></button>
    </div>
    <div v-if="!compact" class="filters-bar">
      <div class="filter-tabs" role="group" aria-label="常用预约状态">
        <button v-for="tab in tabs" :key="tab.code" :aria-pressed="query.status === tab.code" @click="emit('filter', tab.code)">{{ tab.label }}</button>
      </div>
      <div class="all-statuses"><label class="sr-only" for="appointment-status">预约状态</label><select id="appointment-status" :value="query.status" @change="changeStatus"><option value="">全部状态</option><option v-for="status in appointmentStatuses" :key="status.code" :value="status.code">{{ status.label }}</option></select></div>
    </div>
    <div v-if="!compact && query.keyword" class="applied-keyword"><span>关键词：{{ query.keyword }}</span><button class="text-action" aria-label="清除关键词筛选" @click="emit('clear-keyword')"><PortIcon name="reset" :size="14" />清除</button></div>
    <p v-if="rows.length" class="scroll-hint">左右滑动查看完整记录</p>
    <div class="records-body" :class="{ 'is-loading': loading }">
      <div v-if="loading" class="query-progress" role="status"><span>正在更新…</span></div>
      <div v-if="rows.length" class="table-host" :inert="loading || undefined">
        <AppointmentTable :rows="rows" :selected-id="selectedId" :compact="compact" @select="emit('select', $event)" />
      </div>
      <div v-else-if="error" class="inline-state" role="alert"><PortIcon name="info" :size="28" /><h3>暂时无法显示预约</h3><p>{{ error }}</p><button class="button button-primary" @click="emit('load')">重新查询</button></div>
      <div v-else-if="loading" class="table-skeleton" aria-hidden="true"><div v-for="row in 5" :key="row"><span></span><span></span><span></span></div></div>
      <div v-else class="inline-state" role="status"><PortIcon name="search" :size="28" /><h3>{{ totalCount ? '这一页没有预约记录' : '没有符合条件的预约' }}</h3><p>调整关键词或状态后重新查询。</p><button class="button" @click="totalCount ? emit('load', { ...query, page: 1 }) : emit('reset')">{{ totalCount ? '返回第一页' : '清除筛选' }}</button></div>
    </div>
    <footer v-if="!compact" class="pagination">
      <span role="status" aria-live="polite">{{ error ? '查询未完成' : `共 ${totalCount} 条预约，第 ${query.page} 页，共 ${totalPages} 页` }}</span>
      <div class="pagination-controls">
        <label class="sr-only" for="page-size">每页条数</label><select id="page-size" :value="query.pageSize" :disabled="loading" @change="changePageSize"><option v-for="size in [2, 10, 20, 50, 100]" :key="size" :value="size">{{ size }} 条 / 页</option></select>
        <button class="icon-button" aria-label="上一页" :disabled="loading || !!error || query.page <= 1" @click="emit('load', { ...query, page: query.page - 1 })"><PortIcon name="left" :size="17" /></button>
        <span class="page-number">{{ error ? '—' : query.page }}</span>
        <button class="icon-button" aria-label="下一页" :disabled="loading || !!error || query.page >= totalPages" @click="emit('load', { ...query, page: query.page + 1 })"><PortIcon name="right" :size="17" /></button>
      </div>
    </footer>
  </section>
</template>
