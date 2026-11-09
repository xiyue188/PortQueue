<script setup lang="ts">
// 每次比较同类事实；当前队列/当前计划与累计事件分开呈现，不混用百分比。
import { computed, ref } from 'vue'
import type { PortOverview } from '../composables/useOverview'
import PortIcon from './PortIcon.vue'
const props = defineProps<{ overview: PortOverview | null }>()
const emit = defineEmits<{ navigate: [view: 'queue' | 'plans' | 'events'] }>()
const active = ref<'queue' | 'plans' | 'events'>('queue')
const tabs = [{ key: 'queue', label: '锚地' }, { key: 'plans', label: '泊位' }, { key: 'events', label: '事件' }] as const
const series = computed(() => active.value === 'queue'
  ? [{ label: '等待分配', value: props.overview?.waitingQueue }, { label: '已分配', value: props.overview?.assignedQueue }]
  : active.value === 'plans'
    ? [{ label: '草拟', value: props.overview?.draftPlans }, { label: '已确认', value: props.overview?.confirmedPlans }, { label: '执行中', value: props.overview?.executingPlans }]
    : [{ label: '进港', value: props.overview?.portEntries }, { label: '靠泊', value: props.overview?.berthings }, { label: '离泊', value: props.overview?.unberthings }])
const max = computed(() => Math.max(1, ...series.value.map(s => s.value ?? 0)))
const heading = computed(() => ({ queue: '当前有效队列', plans: '当前未完成计划', events: '累计执行事件' })[active.value])
const colors = ['#188ce5', '#46b2f4', '#85c9f7']
</script>
<template>
  <section class="glass-panel operations-summary" aria-labelledby="operations-title">
    <div class="panel-heading operations-heading"><h2 id="operations-title">作业分布</h2>
      <div class="operation-tabs" role="group" aria-label="作业类别"><button v-for="tab in tabs" :key="tab.key" :aria-pressed="active === tab.key" @click="active = tab.key">{{ tab.label }}</button></div>
    </div>
    <div class="operation-chart-heading"><h3>{{ heading }}</h3><span>{{ active === 'queue' ? '艘' : '条' }}</span></div>
    <div class="operation-bars" :aria-label="heading">
      <div v-for="(item, index) in series" :key="item.label" class="operation-bar-row"><span>{{ item.label }}</span>
        <div class="operation-bar-track"><i :style="{ width: `${(item.value ?? 0) / max * 100}%`, background: colors[index] }"></i></div>
        <strong>{{ item.value ?? '—' }}</strong>
      </div>
    </div>
    <footer class="operation-footer"><button class="text-action" @click="emit('navigate', active)">查看{{ active === 'queue' ? '队列' : active === 'plans' ? '计划' : '记录' }}<PortIcon name="arrow" :size="16" /></button></footer>
  </section>
</template>
