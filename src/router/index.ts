import { createRouter, createWebHistory } from 'vue-router'

const routes = [
  { path: '/', redirect: '/run' },
  { path: '/config', name: 'config', component: () => import('@/views/ConfigPage.vue') },
  { path: '/run', name: 'run', component: () => import('@/views/RunPage.vue') },
  { path: '/plans', name: 'plans', component: () => import('@/views/PlansPage.vue') },
  { path: '/errors', name: 'errors', component: () => import('@/views/ErrorsPage.vue') },
  { path: '/about', name: 'about', component: () => import('@/views/AboutPage.vue') }
]

export default createRouter({
  history: createWebHistory(),
  routes
})
