<script setup lang="ts">
import { useRouter, useRoute } from 'vue-router'

const router = useRouter()
const route = useRoute()

function logout() {
  sessionStorage.removeItem('admin_creds')
  router.push('/login')
}
</script>

<template>
  <div v-if="route.name === 'Login'">
    <router-view />
  </div>
  <div v-else class="layout">
    <aside class="sidebar">
      <h2 class="logo">ImprivataProxy</h2>
      <nav>
        <router-link to="/users" class="nav-item">Users</router-link>
        <router-link to="/cards" class="nav-item">Cards</router-link>
        <router-link to="/sync" class="nav-item">AD Sync</router-link>
      </nav>
      <button class="logout-btn" @click="logout">Logout</button>
    </aside>
    <main class="content">
      <router-view />
    </main>
  </div>
</template>

<style>
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  background: #f5f7fa;
  color: #333;
}

.layout {
  display: flex;
  min-height: 100vh;
}

.sidebar {
  width: 220px;
  background: #1a1a2e;
  color: #fff;
  padding: 20px 0;
  display: flex;
  flex-direction: column;
}

.logo {
  font-size: 16px;
  padding: 0 20px 20px;
  border-bottom: 1px solid #2a2a4a;
  margin-bottom: 10px;
}

.nav-item {
  display: block;
  padding: 12px 20px;
  color: #aaa;
  text-decoration: none;
  transition: background 0.2s;
}

.nav-item:hover {
  background: #2a2a4a;
  color: #fff;
}

.nav-item.router-link-active {
  background: #0066cc;
  color: #fff;
}

.logout-btn {
  margin-top: auto;
  margin-left: 20px;
  margin-right: 20px;
  padding: 10px;
  background: #cc3333;
  color: #fff;
  border: none;
  border-radius: 4px;
  cursor: pointer;
}

.logout-btn:hover {
  background: #aa2222;
}

.content {
  flex: 1;
  padding: 30px;
  overflow-y: auto;
}

h1 {
  margin-bottom: 20px;
  font-size: 24px;
}

table {
  width: 100%;
  border-collapse: collapse;
  background: #fff;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0,0,0,0.1);
}

th, td {
  padding: 12px 16px;
  text-align: left;
  border-bottom: 1px solid #eee;
}

th {
  background: #f8f9fa;
  font-weight: 600;
  font-size: 13px;
  color: #666;
  text-transform: uppercase;
}

tr:hover {
  background: #f8f9fa;
}

.btn {
  padding: 6px 12px;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 13px;
  margin-right: 4px;
}

.btn-primary {
  background: #0066cc;
  color: #fff;
}

.btn-primary:hover {
  background: #0052a3;
}

.btn-danger {
  background: #cc3333;
  color: #fff;
}

.btn-danger:hover {
  background: #aa2222;
}

.btn-success {
  background: #28a745;
  color: #fff;
}

.btn-success:hover {
  background: #218838;
}

.btn-warning {
  background: #ffc107;
  color: #333;
}

.btn-warning:hover {
  background: #e0a800;
}

input[type="text"],
input[type="password"] {
  padding: 8px 12px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 14px;
}

input:focus {
  outline: none;
  border-color: #0066cc;
}

.badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 10px;
  font-size: 12px;
  font-weight: 600;
}

.badge-success {
  background: #d4edda;
  color: #155724;
}

.badge-danger {
  background: #f8d7da;
  color: #721c24;
}

.search-bar {
  margin-bottom: 20px;
  display: flex;
  gap: 10px;
  align-items: center;
}

.card {
  background: #fff;
  border-radius: 8px;
  padding: 24px;
  box-shadow: 0 1px 3px rgba(0,0,0,0.1);
  margin-bottom: 20px;
}

.stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 16px;
  margin-top: 16px;
}

.stat-item {
  text-align: center;
  padding: 16px;
  background: #f8f9fa;
  border-radius: 8px;
}

.stat-value {
  font-size: 28px;
  font-weight: 700;
  color: #0066cc;
}

.stat-label {
  font-size: 13px;
  color: #666;
  margin-top: 4px;
}
</style>
