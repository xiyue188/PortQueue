// 前端应用入口：注册 Element Plus、全局样式并挂载总览组件。
import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import './styles.css'
import App from './App.vue'

createApp(App).use(ElementPlus).mount('#app')
