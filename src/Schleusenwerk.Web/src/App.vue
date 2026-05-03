<template>
  <div class="blueprint-grid"></div>
  <div style="position: relative; z-index: 1; min-height: 100vh;">
    <SchaltleisteNav />
    <main class="blueprint-content">
      <RouterView />
    </main>
  </div>
</template>

<script setup lang="ts">
import SchaltleisteNav from '@/components/SchaltleisteNav.vue'
</script>
