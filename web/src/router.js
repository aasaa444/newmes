import { createRouter, createWebHistory } from 'vue-router'
import { getToken, getUser } from './api'
import LoginView from './views/LoginView.vue'
import PlanHome from './views/plan/PlanHome.vue'
import StationHome from './views/station/StationHome.vue'
import AuditView from './views/plan/AuditView.vue'

const routes = [
  { path: '/', redirect: '/login' },
  { path: '/login', component: LoginView, meta: { public: true } },
  {
    path: '/plan',
    component: PlanHome,
    meta: { roles: ['Planner', 'Leader'] },
  },
  {
    path: '/plan/audit',
    component: AuditView,
    meta: { roles: ['Planner', 'Leader', 'Operator'] },
  },
  {
    path: '/station',
    component: StationHome,
    meta: { roles: ['Operator', 'Leader', 'Planner'] },
  },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

router.beforeEach((to) => {
  if (to.meta.public) return true
  if (!getToken()) return '/login'
  const user = getUser()
  const roles = to.meta.roles
  if (roles && user && !roles.includes(user.role)) {
    if (user.role === 'Operator') return '/station'
    return '/plan'
  }
  return true
})

export default router
