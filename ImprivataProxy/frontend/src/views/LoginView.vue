<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import api from '../api'

const router = useRouter()
const username = ref('admin')
const password = ref('')
const error = ref('')
const loading = ref(false)

async function login() {
  error.value = ''
  loading.value = true
  const creds = btoa(`${username.value}:${password.value}`)
  sessionStorage.setItem('admin_creds', creds)

  try {
    await api.get('/admin/users?take=1')
    router.push('/users')
  } catch (e: any) {
    sessionStorage.removeItem('admin_creds')
    error.value = 'Invalid credentials'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <div class="login-card">
      <h1>ImprivataProxy Admin</h1>
      <form @submit.prevent="login">
        <div class="field">
          <label>Username</label>
          <input type="text" v-model="username" autofocus />
        </div>
        <div class="field">
          <label>Password</label>
          <input type="password" v-model="password" />
        </div>
        <p v-if="error" class="error">{{ error }}</p>
        <button type="submit" class="btn btn-primary login-btn" :disabled="loading">
          {{ loading ? 'Logging in...' : 'Login' }}
        </button>
      </form>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #1a1a2e;
}

.login-card {
  background: #fff;
  padding: 40px;
  border-radius: 12px;
  width: 380px;
  box-shadow: 0 4px 20px rgba(0,0,0,0.3);
}

.login-card h1 {
  text-align: center;
  margin-bottom: 30px;
  font-size: 20px;
}

.field {
  margin-bottom: 16px;
}

.field label {
  display: block;
  margin-bottom: 6px;
  font-size: 14px;
  font-weight: 500;
}

.field input {
  width: 100%;
  padding: 10px 12px;
}

.login-btn {
  width: 100%;
  padding: 12px;
  font-size: 15px;
  margin-top: 10px;
}

.error {
  color: #cc3333;
  font-size: 14px;
  margin-bottom: 10px;
}
</style>
