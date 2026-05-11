import axios from 'axios'

const api = axios.create({
  baseURL: '/',
})

api.interceptors.request.use((config) => {
  const creds = sessionStorage.getItem('admin_creds')
  if (creds) {
    config.headers.Authorization = `Basic ${creds}`
  }
  return config
})

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      sessionStorage.removeItem('admin_creds')
      window.location.href = '/login'
    }
    return Promise.reject(error)
  }
)

export default api
