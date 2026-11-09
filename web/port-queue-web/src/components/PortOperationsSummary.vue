<script setup lang="ts">
// 图形只表达同口径的组成/数量，未知为空，零值不画虚假进度。
import { computed } from 'vue'
import type { PortOverview } from '../composables/useOverview'
import PortIcon from './PortIcon.vue'
const props = defineProps<{ overview: PortOverview | null }>()
const queueTotal = computed(() => props.overview ? props.overview.waitingQueue + props.overview.assignedQueue : 0)
const plans = computed(() => [{ label: '草拟', value: props.overview?.draftPlans }, { label: '已确认', value: props.overview?.confirmedPlans }, { label: '执行中', value: props.overview?.executingPlans }])
const events = computed(() => [{ label: '进港', value: props.overview?.portEntries }, { label: '靠泊', value: props.overview?.berthings }, { label: '离泊', value: props.overview?.unberthings }])
const eventMax = computed(() => Math.max(1, ...events.value.map(e => e.value ?? 0)))
</script>
<template>
  <section class="glass-panel operations-summary" aria-labelledby="operations-title">
    <div class="panel-heading"><h2 id="operations-title">作业概要</h2></div>
    <div class="operation-visual"><div class="operation-title"><PortIcon name="anchor"/><h3>锚地排队</h3></div><div class="queue-chart">
      <div class="queue-legend"><span>等待分配 <strong>{{ overview?.waitingQueue ?? '—' }}</strong></span><span>已分配 <strong>{{ overview?.assignedQueue ?? '—' }}</strong></span></div>
      <div class="queue-bar" role="img" :aria-label="overview ? `等待分配 ${overview.waitingQueue}，已分配 ${overview.assignedQueue}` : '队列暂未读取'"><span v-if="queueTotal" :style="{ width: `${(overview?.waitingQueue ?? 0) / queueTotal * 100}%` }"></span><i v-if="queueTotal" :style="{ width: `${(overview?.assignedQueue ?? 0) / queueTotal * 100}%` }"></i></div>
    </div></div>
    <div class="operation-visual"><div class="operation-title"><PortIcon name="berth"/><h3>泊位计划</h3></div><ol class="plan-sequence"><li v-for="plan in plans" :key="plan.label" :class="{ populated: (plan.value ?? 0) > 0 }"><span>{{ plan.label }}</span><strong>{{ plan.value ?? '—' }}</strong></li></ol></div>
    <div class="operation-visual execution-visual"><div class="operation-title"><PortIcon name="document"/><h3>累计执行</h3></div><div class="execution-bars"><div v-for="event in events" :key="event.label"><span>{{ event.label }}</span><div class="execution-track"><i :style="{ width: `${(event.value ?? 0) / eventMax * 100}%` }"></i></div><strong>{{ event.value ?? '—' }}</strong></div></div></div>
  </section>
</template>
