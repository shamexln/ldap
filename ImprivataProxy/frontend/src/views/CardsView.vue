<script setup lang="ts">
import { ref, onMounted } from 'vue'
import api from '../api'

interface User {
  id: string
  username: string
  domain: string
  displayName: string | null
  enabled: boolean
  hasPin: boolean
  cardCount: number
}

interface UserDetail {
  id: string
  username: string
  cards: Card[]
}

interface Card {
  id: string
  userId: string
  label: string | null
  last4: string | null
  issuedAt: string
  expiresAt: string | null
  revoked: boolean
}

const users = ref<User[]>([])
const selectedUser = ref<UserDetail | null>(null)
const loading = ref(false)
const issueDialog = ref(false)
const newCard = ref({ cardUid: '', label: '' })

async function fetchUsers() {
  loading.value = true
  try {
    const res = await api.get('/admin/users', { params: { take: 100 } })
    users.value = res.data
  } catch (e) {
    console.error(e)
  } finally {
    loading.value = false
  }
}

async function selectUser(user: User) {
  try {
    const res = await api.get(`/admin/users/${user.id}`)
    selectedUser.value = res.data
  } catch (e) {
    console.error(e)
  }
}

async function issueCard() {
  if (!selectedUser.value || !newCard.value.cardUid) return
  try {
    await api.post('/admin/cards', {
      userId: selectedUser.value.id,
      cardUid: newCard.value.cardUid,
      label: newCard.value.label || null,
    })
    issueDialog.value = false
    newCard.value = { cardUid: '', label: '' }
    await selectUser({ id: selectedUser.value.id } as User)
    await fetchUsers()
  } catch (e: any) {
    if (e.response?.status === 409) {
      alert('Card already enrolled')
    }
    console.error(e)
  }
}

async function revokeCard(card: Card) {
  if (!confirm(`Revoke card ****${card.last4}?`)) return
  try {
    await api.delete(`/admin/cards/${card.id}`)
    if (selectedUser.value) {
      await selectUser({ id: selectedUser.value.id } as User)
    }
    await fetchUsers()
  } catch (e) {
    console.error(e)
  }
}

onMounted(fetchUsers)
</script>

<template>
  <h1>Cards</h1>

  <div class="cards-layout">
    <div class="user-list card">
      <h3>Select User</h3>
      <div
        v-for="user in users"
        :key="user.id"
        class="user-item"
        :class="{ active: selectedUser?.id === user.id }"
        @click="selectUser(user)"
      >
        <span>{{ user.username }}</span>
        <span class="badge badge-success" v-if="user.cardCount > 0">{{ user.cardCount }}</span>
      </div>
    </div>

    <div class="card-detail card" v-if="selectedUser">
      <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:16px;">
        <h3>Cards for {{ selectedUser.username }}</h3>
        <button class="btn btn-primary" @click="issueDialog = true">+ Issue Card</button>
      </div>

      <table v-if="selectedUser.cards.length > 0">
        <thead>
          <tr>
            <th>Last 4</th>
            <th>Label</th>
            <th>Issued</th>
            <th>Expires</th>
            <th>Status</th>
            <th>Action</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="card in selectedUser.cards" :key="card.id">
            <td>****{{ card.last4 }}</td>
            <td>{{ card.label || '-' }}</td>
            <td>{{ new Date(card.issuedAt).toLocaleDateString() }}</td>
            <td>{{ card.expiresAt ? new Date(card.expiresAt).toLocaleDateString() : 'Never' }}</td>
            <td>
              <span class="badge" :class="card.revoked ? 'badge-danger' : 'badge-success'">
                {{ card.revoked ? 'Revoked' : 'Active' }}
              </span>
            </td>
            <td>
              <button v-if="!card.revoked" class="btn btn-danger" @click="revokeCard(card)">Revoke</button>
            </td>
          </tr>
        </tbody>
      </table>
      <p v-else>No cards issued.</p>
    </div>

    <div v-else class="card-detail card">
      <p>Select a user to manage their cards.</p>
    </div>
  </div>

  <!-- Issue Card Dialog -->
  <div v-if="issueDialog" class="modal-overlay" @click.self="issueDialog = false">
    <div class="modal">
      <h3>Issue Card for {{ selectedUser?.username }}</h3>
      <div class="field">
        <label>Card UID</label>
        <input type="text" v-model="newCard.cardUid" placeholder="Scan or enter card UID" />
      </div>
      <div class="field">
        <label>Label (optional)</label>
        <input type="text" v-model="newCard.label" placeholder="e.g. Badge #1234" />
      </div>
      <div class="modal-actions">
        <button class="btn btn-primary" @click="issueCard" :disabled="!newCard.cardUid">Issue</button>
        <button class="btn" @click="issueDialog = false">Cancel</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.cards-layout {
  display: flex;
  gap: 20px;
}

.user-list {
  width: 250px;
  max-height: 70vh;
  overflow-y: auto;
}

.user-list h3 {
  margin-bottom: 12px;
}

.user-item {
  padding: 10px 12px;
  cursor: pointer;
  border-radius: 4px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.user-item:hover {
  background: #f0f0f0;
}

.user-item.active {
  background: #e3f2fd;
}

.card-detail {
  flex: 1;
}

.field {
  margin-bottom: 12px;
}

.field label {
  display: block;
  margin-bottom: 4px;
  font-size: 14px;
  font-weight: 500;
}

.field input {
  width: 100%;
}

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
  min-width: 360px;
}

.modal h3 {
  margin-bottom: 16px;
}

.modal-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
  margin-top: 16px;
}
</style>
