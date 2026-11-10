<script setup lang="ts">
// 路径动画只解释业务先后，不绑定船舶、位置或进度；离屏/隐藏时暂停原生 SVG 时间轴。
import { computed, onMounted, onScopeDispose, ref, watch } from 'vue'
import PortIcon from './PortIcon.vue'

const props = defineProps<{ active: boolean }>()
const diagram = ref<HTMLElement>()
const desktopTrack = ref<SVGSVGElement>()
const mobileTrack = ref<SVGSVGElement>()
const width = ref(640)
const visible = ref(false)
const hidden = ref(document.hidden)
const reduced = ref(window.matchMedia('(prefers-reduced-motion: reduce)').matches)
const mobile = ref(window.matchMedia('(max-width: 700px)').matches)
const paused = ref(false)
const running = computed(() => props.active && visible.value && !hidden.value && !reduced.value && !paused.value)
const mainPath = computed(() => `M${width.value * .125} 80 H${width.value * .875}`)
const bypass = computed(() => {
  const start = width.value * .125, end = width.value * .625
  return `M${start} 80 V38 Q${start} 26 ${start + 12} 26 H${end - 12} Q${end} 26 ${end} 38 V80`
})
const steps = [
  { icon: 'document', name: '预约审核', detail: '登记 · 提交 · 审核' },
  { icon: 'anchor', name: '锚地等泊', detail: '按需进入队列' },
  { icon: 'berth', name: '泊位安排', detail: '选择泊位与时间' },
  { icon: 'ship', name: '靠离泊作业', detail: '记录实际时间' },
]
// 以用户给出的帆船轮廓为方向，使用项目自绘图标；不引入图片水印或额外依赖。
const boat = 'M-16-8H17L12 0H-10Z M0-34V-11 M-3-32L-14-13H-3Z M4-29V-13H14Q12-23 4-29Z'
let observer: IntersectionObserver | undefined
let resizeObserver: ResizeObserver | undefined
const motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)')
const mobilePreference = window.matchMedia('(max-width: 700px)')
function syncAnimation() {
  for (const [track, enabled] of [[desktopTrack.value, !mobile.value], [mobileTrack.value, mobile.value]] as const) {
    if (running.value && enabled) track?.unpauseAnimations()
    else track?.pauseAnimations()
  }
}
function visibilityChange() { hidden.value = document.hidden }
function preferenceChange() { reduced.value = motionPreference.matches; mobile.value = mobilePreference.matches }
watch([running, mobile], syncAnimation, { flush: 'post' })
onMounted(() => {
  syncAnimation()
  observer = new IntersectionObserver(entries => { visible.value = entries[0]?.isIntersecting ?? false }, { threshold: .25 })
  resizeObserver = new ResizeObserver(entries => { width.value = entries[0]?.contentRect.width ?? 640 })
  if (diagram.value) { observer.observe(diagram.value); resizeObserver.observe(diagram.value) }
  document.addEventListener('visibilitychange', visibilityChange)
  motionPreference.addEventListener('change', preferenceChange)
  mobilePreference.addEventListener('change', preferenceChange)
})
onScopeDispose(() => {
  observer?.disconnect(); resizeObserver?.disconnect()
  document.removeEventListener('visibilitychange', visibilityChange)
  motionPreference.removeEventListener('change', preferenceChange)
  mobilePreference.removeEventListener('change', preferenceChange)
})
</script>

<template>
  <section class="glass-panel port-process" aria-labelledby="port-process-title">
    <div class="panel-heading">
      <h2 id="port-process-title">进港流程</h2>
      <button class="icon-button" :disabled="reduced" :aria-label="paused ? '继续流程动画' : '暂停流程动画'"
        :title="reduced ? '已按系统偏好减少动画' : '流程动画，不表示实时船位'" @click="paused = !paused">
        <PortIcon :name="paused || reduced ? 'play' : 'pause'" :size="16" />
      </button>
    </div>
    <div class="process-body">
      <div ref="diagram" class="process-diagram" :class="{ 'is-reduced': reduced }"
        aria-label="预约审核通过后，可在锚地等待或直接安排泊位；确认泊位后进行进港、靠泊及离泊作业。小船动画仅表达流程方向。">
        <svg ref="desktopTrack" class="process-track desktop-track" :viewBox="`0 0 ${width} 100`" aria-hidden="true">
          <path class="route-bypass" :d="bypass" />
          <path class="route-main" :d="mainPath" />
          <text :x="width * .375" y="17" text-anchor="middle">无需等泊，直接安排</text>
          <circle v-for="fraction in [.125, .375, .625, .875]" :key="fraction" :cx="width * fraction" cy="80" r="4" />
          <g class="route-boat">
            <path :d="boat" />
            <animateMotion :path="mainPath" dur="16s" repeatCount="indefinite" />
            <animate attributeName="opacity" values="0;1;1;0" keyTimes="0;0.06;0.92;1" dur="16s" repeatCount="indefinite" />
          </g>
        </svg>
        <svg ref="mobileTrack" class="process-track mobile-track" viewBox="0 0 80 360" aria-hidden="true">
          <path class="route-main" d="M55 30V330" />
          <path class="route-bypass" d="M55 30H21Q9 30 9 42V218Q9 230 21 230H55" />
          <circle v-for="y in [30,130,230,330]" :key="y" cx="55" :cy="y" r="4" />
          <g class="route-boat"><path :d="boat" transform="translate(0 10) scale(.7)" />
            <animateMotion path="M55 30V330" dur="16s" repeatCount="indefinite" />
            <animate attributeName="opacity" values="0;1;1;0" keyTimes="0;0.06;0.92;1" dur="16s" repeatCount="indefinite" />
          </g>
        </svg>
        <span class="mobile-bypass-label" aria-hidden="true">无需等泊，直接安排</span>
        <ol class="process-steps">
          <li v-for="(step, index) in steps" :key="step.name" :class="{ 'process-entry': index === 0 }">
            <span class="process-symbol"><PortIcon :name="step.icon" :size="25" /></span>
            <h3>{{ step.name }}</h3><p>{{ step.detail }}</p><small v-if="index">待接入</small>
          </li>
        </ol>
      </div>
    </div>
  </section>
</template>
