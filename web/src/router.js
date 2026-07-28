/**
 * 路由：/login 公开；/plan 管理端壳（侧栏五菜单）；/station 过站端全屏。
 */
import { createRouter, createWebHistory } from 'vue-router'
import { getToken, getUser, resolveHomePath } from './api'
import LoginView from './views/LoginView.vue'
import ManagementLayout from './layouts/ManagementLayout.vue'
import OpsOverviewView from './views/plan/OpsOverviewView.vue'
import StationHome from './views/station/StationHome.vue'
import AuditView from './views/plan/AuditView.vue'
import MasterDataView from './views/plan/MasterDataView.vue'
import WorkOrdersView from './views/plan/WorkOrdersView.vue'
import IntegrationView from './views/plan/IntegrationView.vue'
import WipWorkbenchView from './views/plan/WipWorkbenchView.vue'
import QualityWorkbenchView from './views/plan/QualityWorkbenchView.vue'
import InventoryWorkbenchView from './views/plan/InventoryWorkbenchView.vue'
import UsersPlaceholderView from './views/plan/UsersPlaceholderView.vue'

const managementRoles = ['Planner', 'Leader', 'Owner']

const routes = [
  { path: '/', redirect: '/login' },
  { path: '/login', component: LoginView, meta: { public: true } },
  {
    path: '/plan',
    component: ManagementLayout,
    meta: { roles: managementRoles },
    children: [
      {
        path: '',
        name: 'ops-overview',
        component: OpsOverviewView,
        meta: {
          roles: managementRoles,
          title: '经营总览',
          subtitle: '今日口径 · 执行事实（票 03 接完整指标）',
          requireOverview: true,
        },
      },
      {
        path: 'work-orders',
        component: WorkOrdersView,
        meta: {
          roles: managementRoles,
          title: '工单工作台',
          subtitle: '生产执行 · 下达 / 领料 / 关闭',
        },
      },
      {
        path: 'wip',
        component: WipWorkbenchView,
        meta: {
          roles: managementRoles,
          title: '在制工作台',
          subtitle: '生产执行 · 按工序分布',
        },
      },
      {
        path: 'quality',
        component: QualityWorkbenchView,
        meta: {
          roles: managementRoles,
          title: '质量异常',
          subtitle: '隔离待办 · 放行 / 报废',
        },
      },
      {
        path: 'inventory',
        component: InventoryWorkbenchView,
        meta: {
          roles: managementRoles,
          title: '物料库存',
          subtitle: '线边 · 成品（非完整 WMS）',
        },
      },
      {
        path: 'master-data',
        component: MasterDataView,
        meta: {
          roles: managementRoles,
          title: '主数据',
          subtitle: '系统 · 物料 / BOM / 路线 / 工位',
        },
      },
      {
        path: 'users',
        component: UsersPlaceholderView,
        meta: {
          roles: ['Planner', 'Owner'],
          title: '用户与角色',
          subtitle: '系统 · 配置台（票 08）',
        },
      },
      {
        path: 'audit',
        component: AuditView,
        meta: {
          roles: ['Planner', 'Leader', 'Owner', 'Operator'],
          title: '业务审计',
          subtitle: '系统 · 操作者行为',
        },
      },
      {
        path: 'integration',
        component: IntegrationView,
        meta: {
          roles: managementRoles,
          title: 'ERP 出站',
          subtitle: '系统 · 回写模拟报文',
        },
      },
    ],
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
  // 合并父级 meta.roles
  const roles = to.matched.map((r) => r.meta.roles).filter(Boolean).flat()
  const needRoles = to.meta.roles || (roles.length ? roles : null)
  if (needRoles && user && !needRoles.includes(user.role)) {
    return resolveHomePath(user)
  }
  if (to.meta.requireOverview && user && user.canViewOpsOverview === false && user.role === 'Operator') {
    return resolveHomePath(user)
  }
  return true
})

export default router
