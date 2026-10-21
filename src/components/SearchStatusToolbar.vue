<script setup>
import { Plus } from "@lucide/vue";

defineProps({
  activeTags: { type: String, required: true },
  loadedCount: { type: Number, required: true },
  canSave: { type: Boolean, required: true },
  isSaved: { type: Boolean, required: true },
  saveLabel: { type: String, required: true }
});

const emit = defineEmits(["save"]);
</script>

<template>
  <section class="toolbar" aria-label="검색 상태">
    <div>
      <span class="eyebrow">Active Tags</span>
      <strong>{{ activeTags }}</strong>
    </div>
    <div>
      <span class="eyebrow">Loaded</span>
      <strong>{{ loadedCount }}</strong>
    </div>
    <button
      v-if="canSave"
      class="toolbar-action"
      type="button"
      :disabled="isSaved"
      @click="emit('save')"
    >
      <Plus :size="16" aria-hidden="true" />
      <span>{{ saveLabel }}</span>
    </button>
  </section>
</template>
