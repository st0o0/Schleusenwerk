import { createRouter, createWebHistory } from 'vue-router'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: () => import('@/pages/Leitstand.vue') },
    { path: '/tore', component: () => import('@/pages/Schleusentore.vue') },
    { path: '/tore/neu', component: () => import('@/pages/TorEinsetzen.vue') },
    { path: '/tore/:domain', component: () => import('@/pages/TorDetail.vue'), props: true },
    { path: '/siegel', component: () => import('@/pages/Siegel.vue') },
    { path: '/flussprotokoll', component: () => import('@/pages/Flussprotokoll.vue') },
    { path: '/hafenbecken', component: () => import('@/pages/Hafenbecken.vue') },
    { path: '/stellwerk', component: () => import('@/pages/Stellwerk.vue') },
  ],
})
