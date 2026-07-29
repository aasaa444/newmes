<script setup>
/**
 * 经营总览（票 03）：五块真数 + 下钻。
 */
import { onMounted, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../../api'

const router = useRouter()
const overview = ref(null)
const wipList = ref([])
const wipFilter = ref('all') // 'all' 或工序 code
const ordersBucket = ref('inProcess') // 'inProcess' | 'releasedNotStarted'
const orders = ref([])
const isolated = ref([])
const error = ref('')
const loading = ref(true)
const drillOpen = ref(null) // 'wip' | 'orders' | 'isolated'
const stepFilter = ref('')

const byProcess = computed(() => overview.value?.wip?.byProcess ?? [])

async function loadOverview() {
  const res = await api('/api/ops/overview')
  if (!res.ok) {
    error.value = `加载总览失败 (${res.status})`
    return
  }
  overview.value = await res.json()
}

async function loadIsolated() {
  const res = await api('/api/quality/isolated')
  if (res.ok) isolated.value = await res.json()
}

async function loadWip() {
  const url = stepFilter.value
    ? `/api/ops/wip?processStepId=${encodeURIComponent(stepFilter.value)}`
    : '/api/ops/wip'
  const res = await api(url)
  if (res.ok) wipList.value = await res.json()
}

async function loadOrders() {
  const res = await api(`/api/ops/orders?bucket=${ordersBucket.value}`)
  if (res.ok) orders.value = await res.json()
}

onMounted(async () => {
  try {
    await Promise.all([loadOverview(), loadIsolated()])
  } finally {
    loading.value = false
  }
})

function openDrill(kind) {
  drillOpen.value = drillOpen.value === kind ? null : kind
  if (kind === 'wip') {
    stepFilter.value = ''
    loadWip()
  } else if (kind === 'orders') {
    loadOrders()
  }
}

function filterByStep(stepId) {
  stepFilter.value = stepFilter.value === stepId ? '' : stepId
  loadWip()
}
</script>

<template>
  <div>
    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="muted">加载中…</p>

    <div v-if="overview" class="overview-grid">
      <button type="button" class="overview-tile" @click="openDrill('wip')">
        <div class="tile-label">在制</div>
        <div class="tile-num">{{ overview.wip.total }}</div>
        <div class="tile-sub muted">
          <span v-for="b in byProcess" :key="b.stepId" class="chip" :class="{ active: stepFilter === b.stepId }" @click.stop="filterByStep(b.stepId)">
            {{ b.stepCode }} {{ b.count }}
          </span>
        </div>
      </button>

      <div class="overview-tile">
        <div class="tile-label">今日合格入库</div>
        <div class="tile-num">{{ overview.todayQualifiedReceipts }}</div>
        <div class="tile-sub muted">
          {{ new Date(overview.windowStart).toLocaleDateString() }} 自然日
        </div>
      </div>

      <div class="overview-tile">
        <div class="tile-label">今日报废</div>
        <div class="tile-num">{{ overview.todayScraps }}</div>
        <div class="tile-sub muted">执行事实</div>
      </div>

      <button type="button" class="overview-tile" @click="openDrill('isolated')">
        <div class="tile-label">隔离待处理</div>
        <div class="tile-num">{{ overview.isolatedPendingCount }}</div>
        <div class="tile-sub muted">需放行/报废</div>
      </button>

      <button type="button" class="overview-tile" @click="openDrill('orders')">
        <div class="tile-label">工单</div>
        <div class="tile-num">
          {{ overview.orders.inProcess }}<span class="muted"> / </span>{{ overview.orders.releasedNotStarted }}
        </div>
        <div class="tile-sub muted">进行中 / 已下达未开工</div>
      </button>
    </div>

    <p v-if="overview" class="muted overview-foot">
      五块均来自执行事实（工序/过站/隔离/工单状态），今日口径为自然日（<strong>{{ overview.windowStart }}</strong>）。
    </p>

    <!-- 钻取：在制 -->
    <div v-if="drillOpen === 'wip'" class="card" style="margin-top: 1rem">
      <h3 style="margin-top: 0">在制 SN 列表
        <span v-if="stepFilter" class="pill" style="margin-left: 0.5rem">
          工序过滤：{{ byProcess.find(b => b.stepId === stepFilter)?.stepCode }}
        </span>
      </h3>
      <table class="table">
        <thead>
          <tr>
            <th>序列号</th>
            <th>工单</th>
            <th>当前工序</th>
            <th>创建</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in wipList" :key="r.id">
            <td>{{ r.serialNo }}</td>
            <td>{{ r.workOrderNo }}</td>
            <td>{{ r.currentStepCode || '—' }}</td>
            <td class="muted">{{ new Date(r.createdAt).toLocaleString() }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="!wipList.length" class="muted">无在制</p>
    </div>

    <!-- 钻取：隔离 -->
    <div v-if="drillOpen === 'isolated'" class="card" style="margin-top: 1rem">
      <h3 style="margin-top: 0">隔离中</h3>
      <table class="table">
        <thead>
          <tr>
            <th>序列号</th>
            <th>工单</th>
            <th>当前工序</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in isolated" :key="r.id">
            <td>{{ r.serialNo }}</td>
            <td>{{ r.workOrderNo }}</td>
            <td>{{ r.currentStepCode || '—' }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="!isolated.length" class="muted">当前无隔离</p>
    </div>

    <!-- 钻取：工单 -->
    <div v-if="drillOpen === 'orders'" class="card" style="margin-top: 1rem">
      <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
        <button
          class="btn"
          :class="{ primary: ordersBucket === 'inProcess' }"
          type="button"
          @click="ordersBucket = 'inProcess'; loadOrders()"
        >进行中</button>
        <button
          class="btn"
          :class="{ primary: ordersBucket === 'releasedNotStarted' }"
          type="button"
          @click="ordersBucket = 'releasedNotStarted'; loadOrders()"
        >已下达未开工</button>
      </div>
      <table class="table">
        <thead>
          <tr>
            <th>工单号</th>
            <th>成品</th>
            <th>计划</th>
            <th>完工</th>
            <th>报废</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="o in orders" :key="o.id">
            <td>{{ o.orderNo }}</td>
            <td>{{ o.finishedMaterialCode }}</td>
            <td>{{ o.plannedQty }}</td>
            <td>{{ o.completedQty }}</td>
            <td>{{ o.scrappedQty }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="!orders.length" class="muted">无工单</p>
    </div>
  </div>
</template>

<style scoped>
.overview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 0.75rem;
}
.overview-tile {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  padding: 1.1rem 1rem;
  border: 1px solid rgba(148, 163, 184, 0.2);
  border-radius: 12px;
  background: rgba(15, 23, 42, 0.7);
  text-align: left;
  color: inherit;
  font: inherit;
  cursor: pointer;
  transition: border-color 0.15s, transform 0.05s;
}
.overview-tile:hover {
  border-color: rgba(45, 212, 191, 0.5);
}
.overview-tile:active { transform: scale(0.99); }
.tile-label {
  font-size: 0.85rem;
  color: #94a3b8;
}
.tile-num {
  font-size: 1.9rem;
  font-weight: 700;
  color: #5eead4;
}
.tile-sub {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
  font-size: 0.78rem;
}
.chip {
  background: rgba(45, 212, 191, 0.1);
  border-radius: 999px;
  padding: 0.05rem 0.5rem;
  color: #99f6e4;
  font-size: 0.72rem;
  cursor: pointer;
}
.chip.active {
  background: rgba(45, 212, 191, 0.4);
  color: #fff;
}
.overview-foot {
  margin-top: 0.75rem;
  font-size: 0.85rem;
}
</style>
