<script setup>
/**
 * 计划端 — 生产工单列表与下达/领料（票 03）。
 * 班组长/计划员可看列表；写操作需计划员 token（403 时提示）。
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
  message.value = `已创建并下达 ${wo.orderNo}`
  await load()
}

async function issue(id) {
  message.value = ''
  error.value = ''
  const res = await api(`/api/work-orders/${id}/issue`, { method: 'POST', body: '{}' })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    error.value = body.error || `领料失败 ${res.status}`
    return
  }
  message.value = '领料完成（非关键件已耗，关键件待绑 SN）'
  await load()
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
    <p v-if="message" class="muted">{{ message }}</p>

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
            <th>冻结路线/BOM</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="w in orders" :key="w.id">
            <td>{{ w.orderNo }}</td>
            <td>{{ w.finishedMaterialCode }}</td>
            <td>{{ w.plannedQty }}</td>
            <td><span class="pill">{{ w.status }}</span></td>
            <td class="muted">{{ w.frozenRouteVersion }} / {{ w.frozenBomVersion }}</td>
            <td>
              <button
                v-if="user?.role === 'Planner' && w.status === 'Released'"
                class="btn"
                type="button"
                style="margin-right: 0.35rem"
                @click="issue(w.id)"
              >
                齐套领料
              </button>
              <button
                v-if="user?.role === 'Planner' && (w.status === 'Draft' || w.status === 'Released')"
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
    </div>

    <div class="card">
      <h3 style="margin-top: 0">线边库存</h3>
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
