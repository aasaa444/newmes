/**
 * 路由：/login 公开；/plan* 管理端（二期壳前暂用）；/station 过站端。
 * 按角色 defaultPath 落地；meta.roles 含 Owner 可读管理页。
 */
import { createRouter, createWebHistory } from 'vue-router'
import { getToken, getUser, resolveHomePath } from './api'
import LoginView from './views/LoginView.vue'
import PlanHome from './views/plan/PlanHome.vue'
import StationHome from './views/station/StationHome.vue'
import AuditView from './views/plan/AuditView.vue'
import MasterDataView from './views/plan/MasterDataView.vue'
import WorkOrdersView from './views/plan/WorkOrdersView.vue'
import IntegrationView from './views/plan/IntegrationView.vue'

/** 可进管理端页面的角色（经营者只读浏览） */
const managementRoles = ['Planner', 'Leader', 'Owner']

const routes = [
  { path: '/', redirect: '/login' },
  { path: '/login', component: LoginView, meta: { public: true } },
  {
    path: '/plan',
    component: PlanHome,
    meta: { roles: managementRoles },
  },
  {
    path: '/plan/master-data',
    component: MasterDataView,
    meta: { roles: ['Planner', 'Leader', 'Owner'] },
  },
  {
    path: '/plan/work-orders',
    component: WorkOrdersView,
    meta: { roles: managementRoles },
  },
  {
    path: '/plan/integration',
    component: IntegrationView,
    meta: { roles: managementRoles },
  },
  {
    path: '/plan/audit',
    component: AuditView,
    meta: { roles: ['Planner', 'Leader', 'Operator', 'Owner'] },
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
    return resolveHomePath(user)
  }
  return true
})

export default router
