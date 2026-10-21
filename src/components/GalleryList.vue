<script setup>
import { CircleAlert, Clock, Images, RefreshCw, Trash2 } from "@lucide/vue";

defineProps({
  galleries: { type: Array, required: true },
  activeGalleryId: { type: String, default: null },
  loading: { type: Boolean, required: true },
  error: { type: String, required: true },
  previewImages: { type: Function, required: true },
  previewStyle: { type: Function, required: true },
  relativeTime: { type: Function, required: true }
});

const emit = defineEmits(["open", "delete"]);
</script>

<template>
  <section class="gallery-panel gallery-list-page" aria-label="저장된 갤러리">
    <div class="gallery-panel-title">
      <Images :size="18" aria-hidden="true" />
      <strong>Gallery</strong>
      <span v-if="loading" class="gallery-status">
        <RefreshCw :size="13" aria-hidden="true" />
        불러오는 중
      </span>
    </div>
    <p v-if="error" class="gallery-error">
      <CircleAlert :size="14" aria-hidden="true" />
      {{ error }}
    </p>
    <div v-if="galleries.length" class="gallery-list">
      <article
        v-for="gallery in galleries"
        :key="gallery.id"
        class="gallery-item"
        :class="{ 'is-active': gallery.id === activeGalleryId }"
      >
        <button class="gallery-open" type="button" @click="emit('open', gallery)">
          <div class="gallery-slideshow" aria-hidden="true">
            <img
              v-for="(preview, index) in previewImages(gallery.id)"
              :key="`${gallery.id}-${preview.id}`"
              :src="preview.previewUrl"
              :style="previewStyle(index)"
              alt=""
            >
            <span v-if="previewImages(gallery.id).length === 0" class="gallery-preview-empty">
              <Images :size="24" aria-hidden="true" />
            </span>
          </div>
          <div class="gallery-info">
            <strong>{{ gallery.title }}</strong>
            <span>{{ gallery.tags }}</span>
            <small>
              <Clock :size="12" aria-hidden="true" />
              {{ relativeTime(gallery.lastCheckedAt) }}
              <template v-if="gallery.cachedPostCount">
                · {{ gallery.cachedPostCount }} cached
              </template>
              <template v-if="gallery.isFullyCached">
                · complete
              </template>
              <template v-else-if="gallery.nextPageToCache">
                · page {{ gallery.nextPageToCache }}
              </template>
            </small>
          </div>
        </button>
        <button class="gallery-delete" type="button" aria-label="갤러리 삭제" @click="emit('delete', gallery)">
          <Trash2 :size="15" aria-hidden="true" />
        </button>
      </article>
    </div>
    <p v-else class="gallery-empty">검색 결과를 갤러리에 추가하면 여기서 다시 열 수 있습니다.</p>
  </section>
</template>
