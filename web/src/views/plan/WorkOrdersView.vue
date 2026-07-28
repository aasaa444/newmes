<script setup>
/**
 * 计划端 — 生产工单列表与下达/领料。
 * 列表展示「领料」状态，避免点过齐套领料后看不出来。
 */
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, clearSession, getUser } from '../../api'

const router = useRouter()
const user = getUser()
const orders = ref([])
const inventory = ref([])
const error = ref('')
const message = ref('')
const loading = ref(true)
const plannedQty = ref(10)
const selectedId = ref('')
const issueDetail = ref(null)

const statusLabel = {
  Draft: '草稿',
  Released: '已下达',
  InProcess: '生产中',
  Completed: '已完工',
  Closed: '已关闭',
  Cancelled: '已取消',
}

function statusText(s) {
  return statusLabel[s] || s
}

async function load() {
  error.value = ''
  const [o, inv] = await Promise.all([api('/api/work-orders'), api('/api/inventory/line-side')])
  if (!o.ok) {
    error.value = `加载工单失败 (${o.status})`
    return
  }
  orders.value = await o.json()
  if (inv.ok) inventory.value = await inv.json()
}

onMounted(async () => {
  try {
    await load()
  } finally {
    loading.value = false
  }
})

async function createAndRelease() {
  message.value = ''
  error.value = ''
  const mats = await api('/api/materials')
  if (!mats.ok) {
    error.value = '无法读取物料'
    return
  }
  const list = await mats.json()
  const fg = list.find((m) => m.code === 'FG-ROUTER')
  if (!fg) {
    error.value = '缺少种子成品 FG-ROUTER'
    return
  }
  const create = await api('/api/work-orders', {
    method: 'POST',
    body: JSON.stringify({
      finishedMaterialId: fg.id,
      plannedQty: Number(plannedQty.value) || 1,
    }),
  })
  if (!create.ok) {
    const body = await create.json().catch(() => ({}))
    error.value = body.error || `创建失败 ${create.status}`
    return
  }
  const wo = await create.json()
  const rel = await api(`/api/work-orders/${wo.id}/release`, { method: 'POST', body: '{}' })
  if (!rel.ok) {
    const body = await rel.json().catch(() => ({}))
    error.value = body.error || `下达失败 ${rel.status}`
    await load()
    return
  }
  message.value = `已创建并下达 ${wo.orderNo}（尚未领料，请点「齐套领料」）`
  await load()
}

async function issue(id) {
  message.value = ''
  error.value = ''
  issueDetail.value = null
  const res = await api(`/api/work-orders/${id}/issue`, { method: 'POST', body: '{}' })
  const body = await res.json().catch(() => ({}))
  if (!res.ok) {
    error.value = body.error || `领料失败 ${res.status}`
    return
  }
  // 接口返回详情：header + issueLines
  const lines = body.issueLines || body.IssueLines || []
  const header = body.header || body.Header || {}
  const orderNo = header.orderNo || header.OrderNo || id
  if (!lines.length) {
    message.value = `工单 ${orderNo}：无需再领（可能已全部领过或 BOM 无差额）`
  } else {
    const parts = lines.map(
      (l) =>
        `${l.materialCode || l.MaterialCode}×${l.issuedQty ?? l.IssuedQty}` +
        ((l.isKeyComponent ?? l.IsKeyComponent) ? '(待绑SN)' : '(已耗)')
    )
    message.value = `工单 ${orderNo} 领料完成：${parts.join('，')}`
  }
  issueDetail.value = { orderNo, lines }
  selectedId.value = id
  await load()
}

async function showIssue(id) {
  error.value = ''
  const res = await api(`/api/work-orders/${id}`)
  if (!res.ok) {
    error.value = `无法加载工单用料 ${res.status}`
    return
  }
  const body = await res.json()
  const lines = body.issueLines || body.IssueLines || []
  const header = body.header || body.Header || {}
  issueDetail.value = {
    orderNo: header.orderNo || header.OrderNo,
    lines,
  }
  selectedId.value = id
  if (!lines.length) {
    message.value = '该工单尚无领料记录'
  } else {
    message.value = `已查看 ${issueDetail.value.orderNo} 的用料台账（${lines.length} 行）`
  }
}

async function cancel(id) {
  error.value = ''
  const res = await api(`/api/work-orders/${id}/cancel`, { method: 'POST', body: '{}' })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    error.value = body.error || `取消失败 ${res.status}`
    return
  }
  message.value = '工单已取消'
  issueDetail.value = null
  await load()
}

async function closeOrder(id) {
  error.value = ''
  const res = await api(`/api/work-orders/${id}/close`, { method: 'POST', body: '{}' })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    error.value = body.error || `关闭失败 ${res.status}`
    return
  }
  message.value = '工单已关闭'
  await load()
}

function logout() {
  clearSession()
  router.push('/login')
}
</script>

<template>
  <div class="shell">
    <div class="topbar">
      <div>
        <div class="brand">生产工单</div>
        <div class="muted">草稿 → 下达 → 领料 · {{ user?.displayName }}</div>
      </div>
      <div class="nav">
        <router-link to="/plan">计划端</router-link>
        <router-link to="/plan/master-data">主数据</router-link>
        <button class="btn ghost" type="button" @click="logout">退出</button>
      </div>
    </div>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="message" class="ok-msg">{{ message }}</p>

    <div v-if="user?.role === 'Planner'" class="card" style="margin-bottom: 1rem">
      <h3 style="margin-top: 0">新建演示工单（FG-ROUTER）</h3>
      <label class="field" style="max-width: 200px">
        <span>计划数量</span>
        <input v-model.number="plannedQty" type="number" min="1" />
      </label>
      <button class="btn primary" type="button" @click="createAndRelease">创建并下达</button>
    </div>

    <div class="card" style="margin-bottom: 1rem">
      <h3 style="margin-top: 0">工单列表</h3>
      <p v-if="loading" class="muted">加载中…</p>
      <table v-else class="table">
        <thead>
          <tr>
            <th>工单号</th>
            <th>成品</th>
            <th>计划</th>
            <th>状态</th>
            <th>领料</th>
            <th>冻结路线/BOM</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="w in orders"
            :key="w.id"
            :class="{ 'row-selected': selectedId === w.id }"
          >
            <td>{{ w.orderNo }}</td>
            <td>{{ w.finishedMaterialCode }}</td>
            <td>{{ w.plannedQty }}</td>
            <td>
              <span class="pill">{{ statusText(w.status) }}</span>
            </td>
            <td>
              <span v-if="w.materialIssued" class="pill pill-ok">
                已领料
                <span class="muted" v-if="w.issuedLineCount">·{{ w.issuedLineCount }}种</span>
              </span>
              <span v-else class="pill pill-warn">未领料</span>
            </td>
            <td class="muted">{{ w.frozenRouteVersion }} / {{ w.frozenBomVersion }}</td>
            <td class="actions">
              <button
                v-if="user?.role === 'Planner' && (w.status === 'Released' || w.status === 'InProcess') && !w.materialIssued"
                class="btn"
                type="button"
                @click="issue(w.id)"
              >
                齐套领料
              </button>
              <button
                v-if="user?.role === 'Planner' && (w.status === 'Released' || w.status === 'InProcess') && w.materialIssued"
                class="btn ghost"
                type="button"
                @click="issue(w.id)"
              >
                再领/补领
              </button>
              <button
                v-if="w.materialIssued"
                class="btn ghost"
                type="button"
                @click="showIssue(w.id)"
              >
                看用料
              </button>
              <button
                v-if="user?.role === 'Planner' && (w.status === 'Draft' || w.status === 'Released') && !w.materialIssued"
                class="btn ghost"
                type="button"
                @click="cancel(w.id)"
              >
                取消
              </button>
              <button
                v-if="user?.role === 'Planner' && w.status === 'Completed'"
                class="btn"
                type="button"
                @click="closeOrder(w.id)"
              >
                关闭
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <p v-if="!loading && orders.length === 0" class="muted">暂无工单</p>
      <p class="muted" style="font-size: 0.85rem; margin-top: 0.75rem">
        说明：「已领料」表示该工单用料台账里已有发料记录；线边库存数字会在领料后减少。点「看用料」可查看明细。
      </p>
    </div>

    <div v-if="issueDetail" class="card" style="margin-bottom: 1rem">
      <h3 style="margin-top: 0">用料台账 · {{ issueDetail.orderNo }}</h3>
      <table v-if="issueDetail.lines?.length" class="table">
        <thead>
          <tr>
            <th>物料</th>
            <th>已领</th>
            <th>待绑 SN</th>
            <th>已耗</th>
            <th>类型</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in issueDetail.lines" :key="i">
            <td>{{ l.materialCode || l.MaterialCode }}</td>
            <td>{{ l.issuedQty ?? l.IssuedQty }}</td>
            <td>{{ l.pendingQty ?? l.PendingQty }}</td>
            <td>{{ l.consumedQty ?? l.ConsumedQty }}</td>
            <td>
              {{ (l.isKeyComponent ?? l.IsKeyComponent) ? '关键件' : '非关键件' }}
            </td>
          </tr>
        </tbody>
      </table>
      <p v-else class="muted">无用料行</p>
    </div>

    <div class="card">
      <h3 style="margin-top: 0">线边库存</h3>
      <p class="muted" style="font-size: 0.85rem">领料成功后这里的「可用」会减少，可与上方「已领料」对照。</p>
      <table class="table">
        <thead>
          <tr>
            <th>物料</th>
            <th>名称</th>
            <th>可用</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="i in inventory" :key="i.materialId">
            <td>{{ i.materialCode }}</td>
            <td>{{ i.materialName }}</td>
            <td>{{ i.quantityOnHand }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
.ok-msg {
  color: #5eead4;
  margin: 0.5rem 0 1rem;
}
.pill-ok {
  background: rgba(16, 185, 129, 0.2);
  color: #6ee7b7;
}
.pill-warn {
  background: rgba(245, 158, 11, 0.15);
  color: #fcd34d;
}
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  align-items: center;
}
.row-selected td {
  background: rgba(45, 212, 191, 0.06);
}
</style>
