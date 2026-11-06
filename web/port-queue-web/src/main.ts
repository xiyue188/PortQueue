// 应用入口：挂载真实预约页面。组件库在实际使用时按需导入，避免全量注册。
import { createApp } from 'vue'
import './styles.css'
import App from './App.vue'

createApp(App).mount('#app')
