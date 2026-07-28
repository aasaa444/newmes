<script setup>
/**
 * 计划端 — 业务审计 + ERP 出站报文（票 06）。
 */
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, clearSession } from '../../api'

const router = useRouter()
const audits = ref([])
const outbox = ref([])
const fg = ref([])
const error = ref('')
const selectedPayload = ref('')

onMounted(async () => {
  const [a, o, f] = await Promise.all([
    api('/api/audit'),
    api('/api/erp/outbox'),
    api('/api/inventory/finished-goods'),
  ])
  if (!a.ok || !o.ok) {
    error.value = '加载集成/审计失败'
    return
  }
  audits.value = await a.json()
  outbox.value = await o.json()
  if (f.ok) fg.value = await f.json()
})

function showPayload(row) {
  selectedPayload.value = row.payloadJson
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
        <div class="brand">集成与审计</div>
        <div class="muted">ERP 出站模拟 · 业务审计 · 成品仓</div>
      </div>
      <div class="nav">
        <router-link to="/plan">计划端</router-link>
        <router-link to="/plan/work-orders">工单</router-link>
        <button class="btn ghost" type="button" @click="logout">退出</button>
      </div>
    </div>

    <p v-if="error" class="error">{{ error }}</p>

    <div class="card" style="margin-bottom: 1rem">
      <h3 style="margin-top: 0">成品库存</h3>
      <table class="table">
        <thead>
          <tr>
            <th>物料</th>
            <th>名称</th>
            <th>数量</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in fg" :key="r.materialId">
            <td>{{ r.materialCode }}</td>
            <td>{{ r.materialName }}</td>
            <td>{{ r.quantityOnHand }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="!fg.length" class="muted">暂无成品入库</p>
    </div>

    <div class="card" style="margin-bottom: 1rem">
      <h3 style="margin-top: 0">ERP 出站（模拟）</h3>
      <table class="table">
        <thead>
          <tr>
            <th>类型</th>
            <th>业务键</th>
            <th>状态</th>
            <th>时间</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="m in outbox" :key="m.id">
            <td>{{ m.messageType }}</td>
            <td>{{ m.businessKey }}</td>
            <td><span class="pill">{{ m.status }}</span></td>
            <td class="muted">{{ new Date(m.createdAt).toLocaleString() }}</td>
            <td><button class="btn ghost" type="button" @click="showPayload(m)">报文</button></td>
          </tr>
        </tbody>
      </table>
      <pre
        v-if="selectedPayload"
        style="
          margin-top: 1rem;
          padding: 0.75rem;
          background: #020617;
          border-radius: 8px;
          overflow: auto;
          max-height: 240px;
          font-size: 0.8rem;
        "
      >{{ selectedPayload }}</pre>
    </div>

    <div class="card">
      <h3 style="margin-top: 0">业务审计（最近）</h3>
      <table class="table">
        <thead>
          <tr>
            <th>动作</th>
            <th>操作者</th>
            <th>对象</th>
            <th>时间</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(a, i) in audits.slice(0, 40)" :key="i">
            <td>{{ a.action }}</td>
            <td>{{ a.actorUserName }}</td>
            <td class="muted">{{ a.subjectType }} {{ a.subjectId }}</td>
            <td class="muted">{{ new Date(a.occurredAt).toLocaleString() }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
