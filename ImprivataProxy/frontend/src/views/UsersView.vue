<script setup lang="ts">
import { ref, onMounted } from 'vue'
import api from '../api'

interface User {
  id: string
  username: string
  domain: string
  displayName: string | null
  givenName: string | null
  sn: string | null
  enabled: boolean
  hasPin: boolean
  cardCount: number
  pwdLockedUntil: string | null
  pinLockedUntil: string | null
  lastSyncedAt: string | null
  groups: string[] | null
}

const users = ref<User[]>([])
const search = ref('')
const loading = ref(false)
const pinDialog = ref<{ userId: string; username: string } | null>(null)
const pinValue = ref('')

async function fetchUsers() {
  loading.value = true
  try {
    const params: any = { take: 100 }
    if (search.value) params.search = search.value
    const res = await api.get('/admin/users', { params })
    users.value = res.data
  } catch (e) {
    console.error(e)
  } finally {
    loading.value = false
  }
}

async function toggleEnabled(user: User) {
  try {
    await api.patch(`/admin/users/${user.id}`, { enabled: !user.enabled })
    user.enabled = !user.enabled
  } catch (e) {
    console.error(e)
  }
}

async function unlockUser(user: User) {
  try {
    await api.post(`/admin/users/${user.id}/unlock`)
    user.pwdLockedUntil = null
    user.pinLockedUntil = null
  } catch (e) {
    console.error(e)
  }
}

function openPinDialog(user: User) {
  pinDialog.value = { userId: user.id, username: user.username }
  pinValue.value = ''
}

async function setPin() {
  if (!pinDialog.value || pinValue.value.length < 4) return
  try {
    await api.put(`/admin/users/${pinDialog.value.userId}/pin`, { pin: pinValue.value })
    pinDialog.value = null
    await fetchUsers()
  } catch (e) {
    console.error(e)
  }
}

async function clearPin(user: User) {
  if (!confirm(`Clear PIN for ${user.username}?`)) return
  try {
    await api.delete(`/admin/users/${user.id}/pin`)
    user.hasPin = false
  } catch (e) {
    console.error(e)
  }
}

function isLocked(user: User): boolean {
  const now = new Date().toISOString()
  return (user.pwdLockedUntil != null && user.pwdLockedUntil > now) ||
         (user.pinLockedUntil != null && user.pinLockedUntil > now)
}

onMounted(fetchUsers)
</script>

<template>
  <h1>Users</h1>

  <div class="search-bar">
    <input type="text" v-model="search" placeholder="Search users..." @keyup.enter="fetchUsers" />
    <button class="btn btn-primary" @click="fetchUsers">Search</button>
  </div>

  <table v-if="!loading">
    <thead>
      <tr>
        <th>Username</th>
        <th>Domain</th>
        <th>Display Name</th>
        <th>Given Name</th>
        <th>Surname</th>
        <th>Groups</th>
        <th>Status</th>
        <th>PIN</th>
        <th>Cards</th>
        <th>Actions</th>
      </tr>
    </thead>
    <tbody>
      <tr v-for="user in users" :key="user.id">
        <td>{{ user.username }}</td>
        <td>{{ user.domain }}</td>
        <td>{{ user.displayName || '-' }}</td>
        <td>{{ user.givenName || '-' }}</td>
        <td>{{ user.sn || '-' }}</td>
        <td>{{ user.groups ? user.groups.join(', ') : '-' }}</td>
        <td>
          <span class="badge" :class="user.enabled ? 'badge-success' : 'badge-danger'">
            {{ user.enabled ? 'Active' : 'Disabled' }}
          </span>
          <span v-if="isLocked(user)" class="badge badge-danger" style="margin-left:4px">Locked</span>
        </td>
        <td>{{ user.hasPin ? 'Yes' : 'No' }}</td>
        <td>{{ user.cardCount }}</td>
        <td>
          <button class="btn" :class="user.enabled ? 'btn-warning' : 'btn-success'" @click="toggleEnabled(user)">
            {{ user.enabled ? 'Disable' : 'Enable' }}
          </button>
          <button v-if="isLocked(user)" class="btn btn-success" @click="unlockUser(user)">Unlock</button>
          <button class="btn btn-primary" @click="openPinDialog(user)">Set PIN</button>
          <button v-if="user.hasPin" class="btn btn-danger" @click="clearPin(user)">Clear PIN</button>
        </td>
      </tr>
    </tbody>
  </table>

  <p v-if="loading">Loading...</p>
  <p v-if="!loading && users.length === 0">No users found.</p>

  <!-- PIN Dialog -->
  <div v-if="pinDialog" class="modal-overlay" @click.self="pinDialog = null">
    <div class="modal">
      <h3>Set PIN for {{ pinDialog.username }}</h3>
      <input type="password" v-model="pinValue" placeholder="Enter PIN (min 4 chars)" @keyup.enter="setPin" />
      <div class="modal-actions">
        <button class="btn btn-primary" @click="setPin" :disabled="pinValue.length < 4">Save</button>
        <button class="btn" @click="pinDialog = null">Cancel</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0,0,0,0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: #fff;
  padding: 24px;
  border-radius: 8px;
  min-width: 320px;
}

.modal h3 {
  margin-bottom: 16px;
}

.modal input {
  width: 100%;
  margin-bottom: 16px;
}

.modal-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}
</style>
