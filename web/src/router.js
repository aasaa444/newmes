/**
 * 路由：/login 公开；/plan* 计划端；/station 过站台（大字布局，后续票接业务）。
 * beforeEach：无 token → 登录；meta.roles 不匹配则按角色兜底跳转。
 */
import { createRouter, createWebHistory } from 'vue-router'
import { getToken, getUser } from './api'
import LoginView from './views/LoginView.vue'
import PlanHome from './views/plan/PlanHome.vue'
import StationHome from './views/station/StationHome.vue'
import AuditView from './views/plan/AuditView.vue'
import MasterDataView from './views/plan/MasterDataView.vue'
import WorkOrdersView from './views/plan/WorkOrdersView.vue'

const routes = [
  { path: '/', redirect: '/login' },
  { path: '/login', component: LoginView, meta: { public: true } },
  {
    path: '/plan',
    component: PlanHome,
    meta: { roles: ['Planner', 'Leader'] },
  },
  {
    path: '/plan/master-data',
    component: MasterDataView,
    meta: { roles: ['Planner', 'Leader'] },
  },
  {
    path: '/plan/work-orders',
    component: WorkOrdersView,
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
