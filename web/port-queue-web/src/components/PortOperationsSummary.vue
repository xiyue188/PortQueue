<script setup lang="ts">
// 跨业务事实口径来自同一总览查询；累计事件不冒充今日量，排队数量不冒充实际在场。
import type { PortOverview } from '../composables/useOverview'
import PortIcon from './PortIcon.vue'
defineProps<{ overview: PortOverview | null }>()
</script>

<template>
  <section class="glass-panel operations-summary" aria-labelledby="operations-title">
    <div class="panel-heading"><h2 id="operations-title">作业概要</h2></div>
    <div class="operation-row"><PortIcon name="anchor" /><h3>锚地排队</h3><dl><div><dt>等待分配</dt><dd>{{ overview?.waitingQueue ?? '—' }}</dd></div><div><dt>已分配</dt><dd>{{ overview?.assignedQueue ?? '—' }}</dd></div></dl></div>
    <div class="operation-row"><PortIcon name="berth" /><h3>泊位计划</h3><dl><div><dt>草拟</dt><dd>{{ overview?.draftPlans ?? '—' }}</dd></div><div><dt>执行中</dt><dd>{{ overview?.executingPlans ?? '—' }}</dd></div></dl></div>
    <div class="operation-row"><PortIcon name="document" /><h3>累计执行</h3><dl><div><dt>进港</dt><dd>{{ overview?.portEntries ?? '—' }}</dd></div><div><dt>靠泊</dt><dd>{{ overview?.berthings ?? '—' }}</dd></div><div><dt>离泊</dt><dd>{{ overview?.unberthings ?? '—' }}</dd></div></dl></div>
  </section>
</template>
