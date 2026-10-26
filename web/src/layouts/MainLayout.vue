<template>
  <div
    class="app-shell"
    :class="{ 'is-collapsed': collapsed, 'is-mobile-open': mobileOpen }"
  >
    <div class="app-ambient" aria-hidden="true"></div>

    <AppSidebar :collapsed="collapsed" @toggle="toggleCollapse" @navigate="mobileOpen = false" />

    <div class="app-body">
      <AppTopbar @open-menu="openMenu" />
      <main class="app-content">
        <div class="app-content-inner">
          <router-view v-slot="{ Component }">
            <transition name="page-fade" mode="out-in">
              <component :is="Component" />
            </transition>
          </router-view>
        </div>
      </main>
    </div>

    <div v-if="mobileOpen" class="sidebar-overlay" @click="mobileOpen = false"></div>
  </div>
</template>

<script setup>
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import AppSidebar from '@/components/AppSidebar.vue'
import AppTopbar from '@/components/AppTopbar.vue'

const route = useRoute()
const COLLAPSE_KEY = 'qts_sidebar_collapsed'

const collapsed = ref(localStorage.getItem(COLLAPSE_KEY) === '1')
const mobileOpen = ref(false)

function isMobile() {
  return window.matchMedia('(max-width: 1024px)').matches
}

function toggleCollapse() {
  if (isMobile()) {
    mobileOpen.value = false
    return
  }
  collapsed.value = !collapsed.value
  localStorage.setItem(COLLAPSE_KEY, collapsed.value ? '1' : '0')
}

function openMenu() {
  if (isMobile()) {
    mobileOpen.value = true
  } else {
    toggleCollapse()
  }
}

function handleResize() {
  if (!isMobile()) mobileOpen.value = false
}

watch(
  () => route.fullPath,
  () => {
    mobileOpen.value = false
  }
)

onMounted(() => window.addEventListener('resize', handleResize))
onBeforeUnmount(() => window.removeEventListener('resize', handleResize))
</script>
