<script setup>
import { ArrowLeft } from "@lucide/vue";

defineProps({
  gallery: { type: Object, required: true }
});

const emit = defineEmits(["back"]);
</script>

<template>
  <section class="toolbar" aria-label="갤러리 상태">
    <button class="toolbar-action" type="button" @click="emit('back')">
      <ArrowLeft :size="16" aria-hidden="true" />
      <span>갤러리 목록</span>
    </button>
    <div>
      <span class="eyebrow">Gallery</span>
      <strong>{{ gallery.title }}</strong>
    </div>
    <div>
      <span class="eyebrow">Cached</span>
      <strong>{{ gallery.cachedPostCount }} posts</strong>
    </div>
    <div>
      <span class="eyebrow">Status</span>
      <strong>{{ gallery.isFullyCached ? 'Complete' : `Page ${gallery.nextPageToCache || 0}` }}</strong>
    </div>
  </section>
</template>
