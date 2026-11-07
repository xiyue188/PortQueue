<script setup lang="ts">
// 统一 24px、1.8px 描边的图标；只表达导航和操作，不承担业务状态文字。
withDefaults(defineProps<{ name: string; size?: number }>(), { size: 22 })
const paths: Record<string, string> = {
  home: 'M3 10 12 3l9 7M5 9v12h5v-7h4v7h5V9',
  ship: 'M3 16 12 12l9 4-3 5H6zM6 14V9h12v5M9 9V5h6v4M12 5V2M2 22c2-2 3 2 5 0s3 2 5 0 3 2 5 0 3 2 5 0',
  anchor: 'M12 8v13M8 11h8M3 14v3c0 2 4 4 9 4s9-2 9-4v-3M1 16l2-2 2 2m14 0 2-2 2 2M12 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6',
  berth: 'M3 21V9l9-6 9 6v12M3 9h18M8 21V11m8 10V11M2 21h20M12 3V1',
  document: 'M6 3h8l5 5v13H6zM14 3v6h5M9 13h7m-7 4h7',
  chart: 'M4 21v-7h3v7zm6 0V8h3v13zm6 0V3h3v18z',
  search: 'M16 16l5 5M18 10a8 8 0 1 0-16 0 8 8 0 0 0 16 0',
  refresh: 'M20 7v5h-5M4 17v-5h5M5 7a8 8 0 0 1 13-2l2 3M4 16l2 3a8 8 0 0 0 13-2',
  arrow: 'M4 12h16m-6-6 6 6-6 6',
  left: 'm14 6-6 6 6 6',
  right: 'm10 6 6 6-6 6',
  down: 'm6 9 6 6 6-6',
  clock: 'M12 7v6l4 2M22 12a10 10 0 1 0-20 0 10 10 0 0 0 20 0',
  calendar: 'M5 5h14v16H5zM8 2v6m8-6v6M5 10h14',
  check: 'm5 12 4 4L19 6',
  user: 'M4 22v-3a8 8 0 0 1 16 0v3M17 7A5 5 0 1 0 7 7a5 5 0 0 0 10 0',
  info: 'M12 11v6m0-10v.1M22 12a10 10 0 1 0-20 0 10 10 0 0 0 20 0',
  reset: 'M7 7 17 17M17 7 7 17',
  menu: 'M4 6h16M4 12h16M4 18h16',
}
</script>

<template>
  <svg :width="size" :height="size" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <path :d="paths[name] ?? paths.info" />
  </svg>
</template>
