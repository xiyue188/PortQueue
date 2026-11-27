<script setup lang="ts">
// 顺位控件中的俯视船形，不表达实船外观或地理坐标。
withDefaults(defineProps<{ hue?: string }>(), { hue: '#2978a9' })
</script>
<template>
  <svg class="queue-ship" viewBox="0 0 76 174" fill="none" aria-hidden="true">
    <path d="M14 143C8 118 9 80 13 52M62 143c6-25 5-63 1-91" stroke="#edfaff" stroke-width="2"/>
    <path d="M38 4C24 15 17 26 17 40v110l5 16h32l5-16V40C59 26 52 15 38 4Z" fill="#153d59" stroke="#e8f5f9" stroke-width="2"/>
    <path d="M38 11C28 20 22 29 22 41v106l4 13h24l4-13V41C54 29 48 20 38 11Z" fill="#79a4b8"/>
    <path d="m38 14-7 14h14z" fill="#d0e5eb"/>
    <g v-for="(y,row) in [36,52,68,84,100]" :key="y"><g v-for="(x,col) in [24,39]" :key="x">
      <rect :x="x" :y="y" width="13" height="14" rx="1" :fill="(row+col)%3===0 ? '#a6ccd9' : hue" stroke="#d5e9ef" stroke-width=".6"/>
      <path :d="`M${x+3} ${y+2}v10m4-10v10m3-10v10`" stroke="#d2e5ec" opacity=".35" stroke-width=".6"/>
    </g></g>
    <rect x="23" y="120" width="30" height="8" rx="2" fill="#e7f4f7"/><rect x="25" y="130" width="26" height="17" rx="2" fill="#bcd7e3"/>
    <path d="M28 133h20M28 138h20" stroke="#315a71" stroke-width="2"/><rect x="33" y="149" width="10" height="7" rx="1" fill="#e7f4f7"/>
  </svg>
</template>
