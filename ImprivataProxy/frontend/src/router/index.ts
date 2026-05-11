import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'Login',
      component: () => import('../views/LoginView.vue'),
    },
    {
      path: '/',
      redirect: '/users',
    },
    {
      path: '/users',
      name: 'Users',
      component: () => import('../views/UsersView.vue'),
    },
    {
      path: '/cards',
      name: 'Cards',
      component: () => import('../views/CardsView.vue'),
    },
    {
      path: '/sync',
      name: 'Sync',
      component: () => import('../views/SyncView.vue'),
    },
  ],
})

router.beforeEach((to) => {
  const creds = sessionStorage.getItem('admin_creds')
  if (!creds && to.name !== 'Login') {
    return { name: 'Login' }
  }
})

export default router
