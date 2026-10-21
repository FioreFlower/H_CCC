<script setup>
import { ArrowLeft, SearchX } from "@lucide/vue";

defineProps({
  path: { type: String, required: true }
});

const emit = defineEmits(["home", "back"]);
</script>

<template>
  <main class="not-found-page">
    <section class="not-found-panel">
      <div class="not-found-icon">
        <SearchX :size="30" aria-hidden="true" />
      </div>
      <div>
        <span class="eyebrow">404</span>
        <h1>페이지를 찾을 수 없습니다.</h1>
        <p>{{ path }}</p>
      </div>
      <div class="not-found-actions">
        <button class="back-button" type="button" @click="emit('back')">
          <ArrowLeft :size="18" aria-hidden="true" />
          <span>이전</span>
        </button>
        <button class="toolbar-action" type="button" @click="emit('home')">
          <span>홈으로</span>
        </button>
      </div>
    </section>
  </main>
</template>
