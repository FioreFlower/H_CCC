<script setup>
import {
  Archive,
  ArrowLeft,
  CalendarDays,
  Download,
  ExternalLink,
  Film,
  Hash,
  Image as ImageIcon,
  Ruler,
  Star,
  Tags
} from "@lucide/vue";

defineProps({
  post: { type: Object, default: null },
  detailLoading: { type: Boolean, required: true },
  detailError: { type: String, required: true },
  originalSrc: { type: String, required: true },
  originalLoading: { type: Boolean, required: true },
  originalError: { type: String, required: true },
  downloadUrl: { type: String, required: true },
  ratingLabel: { type: Function, required: true },
  ratingClass: { type: Function, required: true },
  isVideoPost: { type: Function, required: true },
  isArchivePost: { type: Function, required: true },
  formatDate: { type: Function, required: true },
  detailTagGroups: { type: Function, required: true },
  categoryClass: { type: Function, required: true },
  categoryLabel: { type: Function, required: true },
  tagIcon: { type: Function, required: true }
});

const emit = defineEmits(["close", "open-lightbox", "apply-tag"]);
</script>

<template>
  <main class="detail-page">
    <section v-if="post" class="detail-shell">
      <header class="detail-header">
        <button class="back-button" type="button" @click="emit('close')">
          <ArrowLeft :size="18" aria-hidden="true" />
          <span>목록</span>
        </button>
        <div class="detail-title">
          <strong class="detail-id">
            <Hash :size="16" aria-hidden="true" />
            {{ post.id }}
          </strong>
          <span class="rating rating-badge detail-rating-badge" :class="ratingClass(post.rating)">
            {{ ratingLabel(post.rating) }}
          </span>
          <span class="score-badge detail-score-badge">
            <Star :size="13" aria-hidden="true" />
            {{ post.score }}
          </span>
        </div>
        <div class="detail-actions">
          <a class="detail-link-button detail-download-button" :href="downloadUrl" :download="`danbooru-${post.id}`">
            <Download :size="17" aria-hidden="true" />
            <span>원본 다운로드</span>
          </a>
          <a class="detail-link-button" :href="post.postUrl" target="_blank" rel="noreferrer">
            <ExternalLink :size="17" aria-hidden="true" />
            <span>Danbooru</span>
          </a>
        </div>
      </header>

      <div class="detail-image detail-viewer">
        <span v-if="originalLoading">원본 이미지를 캐시하는 중...</span>
        <span v-else-if="originalError" class="detail-error">{{ originalError }}</span>
        <video
          v-else-if="isVideoPost(post)"
          :src="originalSrc"
          :poster="post.previewUrl"
          controls
          loop
          playsinline
          preload="metadata"
        />
        <div v-else-if="isArchivePost(post)" class="archive-viewer">
          <img v-if="originalSrc" :src="originalSrc" :alt="`Danbooru archive preview ${post.id}`">
          <p>
            <Archive :size="16" aria-hidden="true" />
            ZIP 원본은 다운로드로 열 수 있습니다.
          </p>
        </div>
        <button
          v-else
          class="detail-image-open"
          type="button"
          aria-label="이미지 전체 화면으로 보기"
          @click="emit('open-lightbox')"
        >
          <img :src="originalSrc" :alt="`Danbooru original ${post.id}`">
        </button>
      </div>

      <aside class="detail-meta">
        <div class="detail-stat">
          <span class="detail-stat-icon">
            <ExternalLink :size="17" aria-hidden="true" />
          </span>
          <div>
            <span class="eyebrow">Source</span>
            <a v-if="post.source" :href="post.source" target="_blank" rel="noreferrer">{{ post.source }}</a>
            <span v-else class="muted">No source</span>
          </div>
        </div>
        <div class="detail-stat">
          <span class="detail-stat-icon">
            <Film v-if="isVideoPost(post)" :size="17" aria-hidden="true" />
            <Archive v-else-if="isArchivePost(post)" :size="17" aria-hidden="true" />
            <ImageIcon v-else :size="17" aria-hidden="true" />
          </span>
          <div>
            <span class="eyebrow">Media</span>
            <strong>{{ post.mediaType }}</strong>
          </div>
        </div>
        <div class="detail-stat">
          <span class="detail-stat-icon">
            <Ruler :size="17" aria-hidden="true" />
          </span>
          <div>
            <span class="eyebrow">Size</span>
            <strong>{{ post.width }} × {{ post.height }}</strong>
          </div>
        </div>
        <div v-if="formatDate(post.createdAt)" class="detail-stat">
          <span class="detail-stat-icon">
            <CalendarDays :size="17" aria-hidden="true" />
          </span>
          <div>
            <span class="eyebrow">Created</span>
            <strong>{{ formatDate(post.createdAt) }}</strong>
          </div>
        </div>
        <div class="detail-tag-panel">
          <div class="detail-tag-panel-title">
            <Tags :size="18" aria-hidden="true" />
            <strong>Tags</strong>
          </div>
          <section
            v-for="group in detailTagGroups(post)"
            :key="`group-${group.category}`"
            class="detail-tag-group"
            :class="categoryClass(group.category)"
          >
            <div class="detail-tag-heading">
              <component :is="tagIcon(group.category)" :size="16" aria-hidden="true" />
              <span>{{ categoryLabel(group.category) }}</span>
              <small>{{ group.tags.length }}</small>
            </div>
            <div class="tag-list detail-tags">
              <button
                v-for="tag in group.tags"
                :key="`viewer-${group.category}-${tag}`"
                class="tag-chip detail-tag-chip"
                :class="categoryClass(group.category)"
                type="button"
                @click="emit('apply-tag', tag)"
              >
                <span>{{ tag }}</span>
              </button>
            </div>
          </section>
        </div>
      </aside>
    </section>

    <section v-else class="detail-missing">
      <button class="back-button" type="button" @click="emit('close')">
        <ArrowLeft :size="18" aria-hidden="true" />
        <span>목록</span>
      </button>
      <h2 v-if="detailLoading">포스트를 불러오는 중...</h2>
      <template v-else>
        <h2>포스트를 표시할 수 없습니다.</h2>
        <p>{{ detailError || "목록에서 다시 선택해 주세요." }}</p>
      </template>
    </section>
  </main>
</template>
