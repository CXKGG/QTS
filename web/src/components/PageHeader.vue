<template>
  <div class="page-header">
    <div class="page-header-main">
      <div class="page-header-title-row">
        <h2 v-if="title">{{ title }}</h2>
        <slot name="tag" />
      </div>
      <p v-if="subtitle" class="page-header-sub">{{ subtitle }}</p>
    </div>
    <div v-if="$slots.default" class="page-header-actions">
      <slot />
    </div>
  </div>
</template>

<script setup>
defineProps({
  title: { type: String, default: '' },
  subtitle: { type: String, default: '' }
})
</script>
