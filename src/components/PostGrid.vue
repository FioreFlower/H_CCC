<script setup>
defineProps({
  posts: { type: Array, required: true },
  skeletonCardCount: { type: Number, required: true },
  page: { type: Number, required: true },
  isThumbSettled: { type: Function, required: true },
  isThumbFailed: { type: Function, required: true },
  isVideoPost: { type: Function, required: true },
  isArchivePost: { type: Function, required: true },
  ratingLabel: { type: Function, required: true },
  ratingClass: { type: Function, required: true }
});

const emit = defineEmits(["open-detail", "thumb-load", "thumb-error"]);
</script>

<template>
  <section class="grid" aria-live="polite">
    <article v-for="post in posts" :key="post.id" class="post-card">
      <button class="thumb-button" type="button" @click="emit('open-detail', post)">
        <span v-if="post.previewUrl && !isThumbSettled(post)" class="thumb-skeleton" aria-hidden="true"></span>
        <img
          v-if="post.previewUrl"
          :class="{ 'is-loaded': isThumbSettled(post) && !isThumbFailed(post), 'is-hidden': isThumbFailed(post) }"
          :src="post.previewUrl"
          :alt="`Danbooru post ${post.id}`"
          loading="lazy"
          decoding="async"
          @load="emit('thumb-load', post)"
          @error="emit('thumb-error', post)"
        >
        <span v-if="(isVideoPost(post) || isArchivePost(post)) && (!post.previewUrl || isThumbFailed(post))" class="thumb-placeholder">
          <span>{{ isArchivePost(post) ? 'ZIP' : 'VIDEO' }}</span>
        </span>
        <span class="rating rating-badge" :class="ratingClass(post.rating)">
          {{ ratingLabel(post.rating) }}
        </span>
        <span class="score-badge">
          <span aria-hidden="true">★</span>
          {{ post.score }}
        </span>
        <span v-if="isVideoPost(post) || isArchivePost(post)" class="media-badge">
          {{ isArchivePost(post) ? 'ZIP' : 'VIDEO' }}
        </span>
      </button>
    </article>

    <article v-for="index in skeletonCardCount" :key="`skeleton-${page}-${index}`" class="post-card skeleton-card" aria-hidden="true">
      <div class="thumb-button">
        <span class="thumb-skeleton"></span>
      </div>
    </article>
  </section>
</template>
