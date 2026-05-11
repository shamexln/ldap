<script setup lang="ts">
import { ref } from 'vue'
import api from '../api'

interface SyncResult {
  added: number
  updated: number
  unchanged: number
  disabled: number
  durationMs: number
}

const result = ref<SyncResult | null>(null)
const loading = ref(false)
const error = ref('')

async function triggerSync() {
  loading.value = true
  error.value = ''
  result.value = null
  try {
    const res = await api.post('/admin/sync')
    result.value = res.data
  } catch (e: any) {
    if (e.response?.status === 409) {
      error.value = 'Sync is already running. Please wait.'
    } else {
      error.value = e.response?.data?.error || 'Sync failed. Check server logs.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <h1>Active Directory Sync</h1>

  <div class="card">
    <p>Trigger a manual sync to pull user data from the Active Directory domain controller.</p>
    <p style="margin-top:8px; color:#666; font-size:14px;">
      Automatic sync runs every 30 minutes. Use this button to sync immediately.
    </p>

    <button
      class="btn btn-primary"
      style="margin-top:20px; padding:12px 24px; font-size:15px;"
      @click="triggerSync"
      :disabled="loading"
    >
      {{ loading ? 'Syncing...' : 'Trigger Sync Now' }}
    </button>

    <p v-if="error" style="color:#cc3333; margin-top:16px;">{{ error }}</p>
  </div>

  <div v-if="result" class="card">
    <h3>Sync Result</h3>
    <div class="stats">
      <div class="stat-item">
        <div class="stat-value">{{ result.added }}</div>
        <div class="stat-label">Added</div>
      </div>
      <div class="stat-item">
        <div class="stat-value">{{ result.updated }}</div>
        <div class="stat-label">Updated</div>
      </div>
      <div class="stat-item">
        <div class="stat-value">{{ result.unchanged }}</div>
        <div class="stat-label">Unchanged</div>
      </div>
      <div class="stat-item">
        <div class="stat-value">{{ result.disabled }}</div>
        <div class="stat-label">Disabled</div>
      </div>
      <div class="stat-item">
        <div class="stat-value">{{ (result.durationMs / 1000).toFixed(1) }}s</div>
        <div class="stat-label">Duration</div>
      </div>
    </div>
  </div>
</template>
