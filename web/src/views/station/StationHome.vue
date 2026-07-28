<script setup>
/**
 * 过站台（票 04）：选工位 → 扫/发 SN 过站 → 可选绑关键件 → 查谱系。
 * 大字、扫码优先；业务规则在 API（防跳站等）。
 */
import { onMounted, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { api, clearSession, getUser } from '../../api'

const router = useRouter()
const user = getUser()
const stations = ref([])
const workOrders = ref([])
const stationId = ref('')
const workOrderId = ref('')
const serialInput = ref('')
const lastSerial = ref('')
const componentSerial = ref('')
const pcbMaterialId = ref('')
const message = ref('选择工位后扫描或回车过站；空码则系统发号（须选工单）。')
const error = ref('')
const genealogy = ref(null)

const currentStation = computed(() =>
  stations.value.find((s) => s.id === stationId.value)
)

onMounted(async () => {
  const [st, wo, mats] = await Promise.all([
    api('/api/stations/active'),
    api('/api/work-orders'),
    api('/api/materials'),
  ])
  if (st.ok) {
    stations.value = await st.json()
    const online = stations.value.find((s) => s.stepCode === 'ONLINE')
    if (online) stationId.value = online.id
  }
  if (wo.ok) {
    const list = await wo.json()
    workOrders.value = list.filter(
      (w) => w.status === 'Released' || w.status === 'InProcess'
    )
    if (workOrders.value.length) workOrderId.value = workOrders.value[0].id
  }
  if (mats.ok) {
    const m = await mats.json()
    const pcb = m.find((x) => x.code === 'PCB-MAIN')
    if (pcb) pcbMaterialId.value = pcb.id
  }
})

async function onPass() {
  error.value = ''
  genealogy.value = null
  if (!stationId.value) {
    error.value = '请先选择工位'
    return
  }
  const body = {
    workStationId: stationId.value,
    serialNo: serialInput.value.trim() || null,
    workOrderId: workOrderId.value || null,
  }
  const res = await api('/api/station/pass', {
    method: 'POST',
    body: JSON.stringify(body),
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) {
    error.value = data.error || `过站失败 ${res.status}`
    return
  }
  lastSerial.value = data.serialNo
  serialInput.value = ''
  message.value = `过站成功 ${data.serialNo} → 下一工序 ${data.currentStepCode || '（路线完成）'} [${data.status}]`
  await loadGenealogy(data.serialNo)
}

async function onBind() {
  error.value = ''
  if (!lastSerial.value || !componentSerial.value || !pcbMaterialId.value) {
    error.value = '需要已过站 SN、关键件 SN，且存在 PCB-MAIN'
    return
  }
  const res = await api('/api/station/bind-component', {
    method: 'POST',
    body: JSON.stringify({
      productSerialNo: lastSerial.value,
      componentMaterialId: pcbMaterialId.value,
      componentSerialNo: componentSerial.value.trim(),
      workStationId: stationId.value || null,
    }),
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) {
    error.value = data.error || `绑定失败 ${res.status}`
    return
  }
  message.value = `已绑定关键件 ${componentSerial.value} → ${lastSerial.value}`
  componentSerial.value = ''
  await loadGenealogy(lastSerial.value)
}

async function loadGenealogy(sn) {
  const res = await api(`/api/genealogy/${encodeURIComponent(sn)}`)
  if (res.ok) genealogy.value = await res.json()
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
        <div class="brand">过站台</div>
        <div class="muted">{{ user?.displayName }} · {{ user?.role }}</div>
      </div>
      <div class="nav">
        <router-link v-if="user?.role !== 'Operator'" to="/plan">计划端</router-link>
        <button class="btn ghost" type="button" @click="logout">退出</button>
      </div>
    </div>

    <div class="card station-hero">
      <h1>{{ currentStation ? currentStation.name : '选择工位' }}</h1>
      <p class="muted" v-if="currentStation">
        工序 {{ currentStation.stepCode }} · {{ currentStation.stepName }} · 产线
        {{ currentStation.lineCode }}
      </p>

      <label class="field">
        <span>工位</span>
        <select v-model="stationId" class="station-input" style="font-size: 1.1rem">
          <option disabled value="">请选择</option>
          <option v-for="s in stations" :key="s.id" :value="s.id">
            {{ s.code }} — {{ s.stepName }}
          </option>
        </select>
      </label>

      <label class="field">
        <span>工单（新开 SN / 系统发号时必选）</span>
        <select v-model="workOrderId" class="station-input" style="font-size: 1rem">
          <option value="">（续过站可不选）</option>
          <option v-for="w in workOrders" :key="w.id" :value="w.id">
            {{ w.orderNo }} · {{ w.status }} · 计划 {{ w.plannedQty }}
          </option>
        </select>
      </label>

      <form @submit.prevent="onPass">
        <label class="field">
          <span>成品 SN（空=系统发号）</span>
          <input
            v-model="serialInput"
            class="station-input"
            autofocus
            placeholder="扫码枪输入后回车"
          />
        </label>
        <button class="btn primary" type="submit" style="font-size: 1.1rem; padding: 0.85rem 1.4rem">
          过站合格
        </button>
      </form>

      <p v-if="error" class="error">{{ error }}</p>
      <p>{{ message }}</p>

      <div v-if="lastSerial" style="margin-top: 1.5rem">
        <h3>绑定关键件（PCB-MAIN）→ {{ lastSerial }}</h3>
        <form @submit.prevent="onBind">
          <label class="field">
            <span>关键件 SN</span>
            <input v-model="componentSerial" class="station-input" placeholder="扫描 PCB 序列号" />
          </label>
          <button class="btn" type="submit">绑定</button>
        </form>
      </div>

      <div v-if="genealogy" class="card" style="margin-top: 1rem; background: #0f172a">
        <h3 style="margin-top: 0">谱系 {{ genealogy.serialNo }}</h3>
        <p class="muted">
          工单 {{ genealogy.workOrderNo }} · {{ genealogy.status }} · 当前
          {{ genealogy.currentStepCode || '—' }}
        </p>
        <p><strong>过站</strong></p>
        <ul>
          <li v-for="(p, i) in genealogy.passes" :key="i">
            {{ p.stepCode }} {{ p.stepName }} — {{ p.result }}
            <span class="muted">{{ p.stationCode }} {{ p.operatorUserName }}</span>
          </li>
        </ul>
        <p><strong>关键件</strong></p>
        <ul v-if="genealogy.bindings?.length">
          <li v-for="(b, i) in genealogy.bindings" :key="i">
            {{ b.componentMaterialCode }} · {{ b.componentSerialNo }}
          </li>
        </ul>
        <p v-else class="muted">尚未绑定关键件</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
select.station-input {
  width: 100%;
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.35);
  background: #0f172a;
  color: #f8fafc;
  padding: 0.7rem 0.85rem;
}
</style>
