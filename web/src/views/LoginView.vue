<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { login, resolveHomePath } from '../api'

const router = useRouter()
const userName = ref('planner')
const password = ref('Planner@123')
const error = ref('')
const loading = ref(false)

async function onSubmit() {
  error.value = ''
  loading.value = true
  try {
    const data = await login(userName.value.trim(), password.value)
    await router.push(resolveHomePath(data))
  } catch (e) {
    error.value = e.message || '登录失败'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="shell" style="max-width: 420px; padding-top: 10vh">
    <div class="card">
      <div class="brand">无名 MES · 试点</div>
      <p class="muted">离散电子组装 · 本地账号（计划员 / 操作工 / 班组长 / 经营者）</p>
      <form @submit.prevent="onSubmit">
        <label class="field">
          <span>用户名</span>
          <input v-model="userName" autocomplete="username" />
        </label>
        <label class="field">
          <span>密码</span>
          <input v-model="password" type="password" autocomplete="current-password" />
        </label>
        <p v-if="error" class="error">{{ error }}</p>
        <button class="btn primary" style="width: 100%" :disabled="loading" type="submit">
          {{ loading ? '登录中…' : '登录' }}
        </button>
      </form>
      <p class="muted" style="margin-top: 1rem; font-size: 0.8rem">
        planner / operator / leader / owner<br />
        密码：Planner@123 · Operator@123 · Leader@123 · Owner@123
      </p>
    </div>
  </div>
</template>
