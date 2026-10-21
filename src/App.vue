<script setup>
import {useEventListener} from "@vueuse/core";
import {computed, nextTick, onBeforeUnmount, onMounted, ref, watch} from "vue";
import PhotoSwipe from "photoswipe";
import "photoswipe/style.css";
import { useRoute, useRouter } from "vue-router";
import AppHeader from "./components/AppHeader.vue";
import GalleryList from "./components/GalleryList.vue";
import GalleryStatusToolbar from "./components/GalleryStatusToolbar.vue";
import NotFoundView from "./components/NotFoundView.vue";
import PostDetail from "./components/PostDetail.vue";
import PostGrid from "./components/PostGrid.vue";
import SearchStatusToolbar from "./components/SearchStatusToolbar.vue";
import { useTagSearch } from "./composables/useTagSearch.js";
import { routeNames } from "./router.js";
import {
  categoryClass,
  categoryLabel,
  detailTagGroups,
  formatCount,
  formatDate,
  isArchivePost,
  isVideoPost,
  ratingClass,
  ratingLabel,
  relativeTime,
  tagIcon,
  tokenClass
} from "./lib/viewHelpers.js";

const sentinel = ref(null);
const activeTags = ref("all");
const posts = ref([]);
const page = ref(0);
const limit = ref(40);
const loading = ref(false);
const done = ref(false);
const waitingForGalleryCache = ref(false);
const error = ref("");
const currentView = ref("search");
const galleries = ref([]);
const galleryPreviews = ref({});
const galleryLoading = ref(false);
const galleryError = ref("");
const activeGalleryId = ref(null);
const selectedPostId = ref(null);
const directPost = ref(null);
const detailLoading = ref(false);
const detailError = ref("");
const originalSrc = ref("");
const originalLoading = ref(false);
const originalError = ref("");
const themeMode = ref("system");
const settledThumbs = ref(new Set());
const failedThumbs = ref(new Set());
const userScrolledSinceSearch = ref(false);
let controller;
let galleryCacheTimer;
let loadMoreFrame;
let sentinelObserver;
let activePhotoSwipe = null;
let requestSerial = 0;
const route = useRoute();
const router = useRouter();
const isNotFoundRoute = computed(() => route.name === routeNames.notFound);

const selectedPost = computed(() =>
  posts.value.find((post) => String(post.id) === String(selectedPostId.value)) ||
  (String(directPost.value?.id) === String(selectedPostId.value) ? directPost.value : null)
);
const canOpenImageLightbox = computed(() =>
  Boolean(selectedPost.value && originalSrc.value && !originalLoading.value && !originalError.value) &&
  !isVideoPost(selectedPost.value) &&
  !isArchivePost(selectedPost.value)
);

const statusText = computed(() => {
  if (error.value) return error.value;
  if (loading.value) return "이미지를 불러오는 중...";
  if (waitingForGalleryCache.value) return "갤러리 캐시가 갱신되면 더 불러옵니다.";
  if (done.value) return "마지막 페이지입니다.";
  return "아래로 스크롤하면 더 불러옵니다.";
});

const themeTitle = computed(() => ({
  system: "시스템 테마",
  light: "라이트 모드",
  dark: "다크 모드"
}[themeMode.value]));

const skeletonCardCount = computed(() => loading.value ? pageLimit.value : 0);
const tagTokens = computed(() => committedTags.value);
const activeGallery = computed(() =>
    galleries.value.find((gallery) => gallery.id === activeGalleryId.value) || null
);
const activeGalleryCanGrow = computed(() =>
    currentView.value === "gallery" && Boolean(activeGalleryId.value) && !activeGallery.value?.isFullyCached
);
const pageLimit = computed(() => activeGalleryId.value ? 100 : limit.value);
const canSaveActiveGallery = computed(() => activeTags.value !== "all");
const isActiveGallerySaved = computed(() =>
    !canSaveActiveGallery.value ||
    galleries.value.some((gallery) => gallery.tags === activeTags.value)
);
const gallerySaveLabel = computed(() => {
  if (!canSaveActiveGallery.value) return "all은 제외";
  return isActiveGallerySaved.value ? "갤러리에 있음" : "갤러리에 추가";
});

const isDefaultAllFeed = computed(() =>
    currentView.value === "search" && activeTags.value === "all" && !activeGalleryId.value
);
const isPostGridView = computed(() =>
    currentView.value === "search" || Boolean(activeGalleryId.value)
);
const {
  committedTags,
  tagDraft,
  tagSuggestions,
  tagSuggesting,
  tagSuggestOpen,
  highlightedSuggestion,
  composeTagQuery,
  scheduleTagSuggestions,
  commitDraftTags,
  applySuggestion,
  removeTagToken,
  onTagDraftInput,
  closeTagSuggestionsSoon,
  onTagInputKeydown,
  resetTagSearch,
  abortTagSearch
} = useTagSearch();

function applyTheme() {
  document.documentElement.dataset.theme = themeMode.value;
  localStorage.setItem("danbooru-theme", themeMode.value);
}

function cycleTheme() {
  themeMode.value = themeMode.value === "system"
      ? "light"
      : themeMode.value === "light"
          ? "dark"
          : "system";
  applyTheme();
}

function pushRoute(location) {
  void router.push(location);
}

function goBack() {
  router.back();
}

function originalDownloadUrl(post) {
  if (!post?.id) return "";
  return activeGalleryId.value
      ? `/api/media/galleries/${encodeURIComponent(activeGalleryId.value)}/original/${encodeURIComponent(post.id)}/download`
      : `/api/media/original/${encodeURIComponent(post.id)}/download`;
}

function galleryPreviewImages(galleryId) {
  return galleryPreviews.value[galleryId] || [];
}

function galleryPreviewStyle(index) {
  return {
    animationDelay: `${index * 1600}ms`
  };
}

async function goHome() {
  clearTimeout(galleryCacheTimer);
  waitingForGalleryCache.value = false;
  currentView.value = "search";
  resetTagSearch();
  userScrolledSinceSearch.value = false;
  activeGalleryId.value = null;
  closeDetail({pushState: false, loadFallback: false});
  await resetSearch();
}

async function showSearch() {
  clearTimeout(galleryCacheTimer);
  waitingForGalleryCache.value = false;
  currentView.value = "search";
  activeGalleryId.value = null;
  if (route.name !== routeNames.search) {
    pushRoute({ name: routeNames.search });
  }
  if (!posts.value.length || activeTags.value !== composeTagQuery()) {
    await resetSearch();
  }
}

async function showGalleryNav(pushState = true) {
  if (controller) controller.abort();
  clearTimeout(galleryCacheTimer);
  currentView.value = "gallery";
  activeGalleryId.value = null;
  selectedPostId.value = null;
  resetDetailState();
  posts.value = [];
  page.value = 0;
  done.value = false;
  waitingForGalleryCache.value = false;
  loading.value = false;
  error.value = "";
  if (pushState && route.name !== routeNames.galleries) {
    pushRoute({ name: routeNames.galleries });
  }
  await fetchGalleries();
}

async function fetchGalleries() {
  galleryLoading.value = true;
  galleryError.value = "";
  try {
    const response = await fetch("/api/galleries");
    const payload = await response.json();
    if (!response.ok) {
      throw new Error(payload.detail || payload.error || payload.title || "갤러리를 불러오지 못했습니다.");
    }

    galleries.value = payload;
    payload.forEach((gallery) => {
      if (!galleryPreviews.value[gallery.id]) {
        void fetchGalleryPreview(gallery);
      }
    });
  } catch (caught) {
    galleryError.value = caught.message;
  } finally {
    galleryLoading.value = false;
  }
}

async function fetchActiveGallery() {
  if (!activeGalleryId.value) return null;

  try {
    const response = await fetch(`/api/galleries/${encodeURIComponent(activeGalleryId.value)}`);
    const payload = await response.json();
    if (!response.ok) {
      throw new Error(payload.detail || payload.error || payload.title || "갤러리를 불러오지 못했습니다.");
    }

    const index = galleries.value.findIndex((gallery) => gallery.id === payload.id);
    galleries.value = index >= 0
        ? galleries.value.map((gallery) => gallery.id === payload.id ? payload : gallery)
        : [payload, ...galleries.value];
    return payload;
  } catch (caught) {
    galleryError.value = caught.message;
    return null;
  }
}

function scheduleActiveGalleryCacheCheck() {
  clearTimeout(galleryCacheTimer);
  if (!activeGalleryCanGrow.value) return;

  galleryCacheTimer = window.setTimeout(async () => {
    if (!activeGalleryCanGrow.value || loading.value || selectedPostId.value) return;

    const loadedCount = posts.value.length;
    await fetchActiveGallery();

    if (!activeGalleryCanGrow.value || selectedPostId.value) return;

    if ((activeGallery.value?.cachedPostCount || 0) > loadedCount) {
      waitingForGalleryCache.value = false;
      done.value = false;
      await nextTick();
      queueLoadMoreCheck();
      return;
    }

    scheduleActiveGalleryCacheCheck();
  }, 2500);
}

function queueLoadMoreCheck(options = {}) {
  cancelAnimationFrame(loadMoreFrame);
  loadMoreFrame = requestAnimationFrame(() => {
    if (shouldLoadMore(options)) {
      void loadNextPage();
    }
  });
}

function bindSentinelObserver() {
  sentinelObserver?.disconnect();
  sentinelObserver = null;

  if (!sentinel.value) {
    return;
  }

  sentinelObserver = new IntersectionObserver((entries) => {
    if (entries.some((entry) => entry.isIntersecting)) {
      if (isDefaultAllFeed.value) {
        userScrolledSinceSearch.value = true;
        queueLoadMoreCheck({allowDefaultAll: true});
        return;
      }

      queueLoadMoreCheck();
    }
  }, {
    root: null,
    rootMargin: "900px 0px",
    threshold: 0
  });
  sentinelObserver.observe(sentinel.value);
}

async function fetchGalleryPreview(gallery) {
  try {
    const params = new URLSearchParams({
      page: "0",
      limit: "6"
    });
    const response = await fetch(`/api/galleries/${encodeURIComponent(gallery.id)}/posts?${params}`);
    const payload = await response.json();
    if (!response.ok) return;

    galleryPreviews.value = {
      ...galleryPreviews.value,
      [gallery.id]: payload.posts.filter((post) => post.previewUrl).slice(0, 6)
    };
  } catch {
    galleryPreviews.value = {
      ...galleryPreviews.value,
      [gallery.id]: []
    };
  }
}

async function saveCurrentGallery() {
  if (!canSaveActiveGallery.value) {
    galleryError.value = "all 피드는 자동 캐시 갤러리에 추가하지 않습니다.";
    return;
  }

  galleryError.value = "";
  try {
    const response = await fetch("/api/galleries", {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({
        tags: activeTags.value,
        title: activeTags.value
      })
    });
    const payload = await response.json();
    if (!response.ok) {
      throw new Error(payload.detail || payload.error || payload.title || "갤러리에 추가하지 못했습니다.");
    }

    await fetchGalleries();
    activeGalleryId.value = payload.id;
    await openGallery(payload);
  } catch (caught) {
    galleryError.value = caught.message;
  }
}

async function openGallery(gallery, pushState = true) {
  if (controller) controller.abort();
  clearTimeout(galleryCacheTimer);
  userScrolledSinceSearch.value = true;
  currentView.value = "gallery";
  activeGalleryId.value = gallery.id;
  resetTagSearch();
  committedTags.value = gallery.tags === "all" ? [] : gallery.tags.split(/\s+/).filter(Boolean);
  selectedPostId.value = null;
  resetDetailState();
  if (pushState) {
    pushRoute({ name: routeNames.gallery, params: { galleryId: gallery.id } });
  }
  activeTags.value = gallery.tags;
  posts.value = [];
  settledThumbs.value = new Set();
  failedThumbs.value = new Set();
  page.value = 0;
  done.value = false;
  waitingForGalleryCache.value = false;
  loading.value = false;
  error.value = "";
  await nextTick();
  bindSentinelObserver();
  await loadNextPage();
}

async function deleteGallery(gallery) {
  galleryError.value = "";
  try {
    const response = await fetch(`/api/galleries/${encodeURIComponent(gallery.id)}`, {
      method: "DELETE"
    });
    if (!response.ok && response.status !== 204) {
      const payload = await response.json();
      throw new Error(payload.detail || payload.error || payload.title || "갤러리를 삭제하지 못했습니다.");
    }

    if (activeGalleryId.value === gallery.id) {
      activeGalleryId.value = null;
    }
    const nextPreviews = {...galleryPreviews.value};
    delete nextPreviews[gallery.id];
    galleryPreviews.value = nextPreviews;
    await fetchGalleries();
  } catch (caught) {
    galleryError.value = caught.message;
  }
}

function thumbKey(post) {
  return `${post.id}:${post.previewUrl || ""}`;
}

function isThumbSettled(post) {
  return !post.previewUrl || settledThumbs.value.has(thumbKey(post));
}

function isThumbFailed(post) {
  return failedThumbs.value.has(thumbKey(post));
}

function markThumbSettled(post) {
  const next = new Set(settledThumbs.value);
  next.add(thumbKey(post));
  settledThumbs.value = next;
  handleMediaSettled();
}

function markThumbFailed(post) {
  const key = thumbKey(post);
  const nextSettled = new Set(settledThumbs.value);
  const nextFailed = new Set(failedThumbs.value);
  nextSettled.add(key);
  nextFailed.add(key);
  settledThumbs.value = nextSettled;
  failedThumbs.value = nextFailed;
  handleMediaSettled();
}

function applyTag(tag) {
  resetTagSearch();
  committedTags.value = [tag];
  closeDetail({pushState: false, loadFallback: false});
  void resetSearch();
}

function resetDetailState() {
  closeImageLightbox();
  directPost.value = null;
  detailLoading.value = false;
  detailError.value = "";
  originalSrc.value = "";
  originalLoading.value = false;
  originalError.value = "";
}

function openImageLightbox() {
  if (!canOpenImageLightbox.value) return;

  activePhotoSwipe?.destroy();
  activePhotoSwipe = new PhotoSwipe({
    dataSource: [
      {
        src: originalSrc.value,
        width: selectedPost.value.width || 1600,
        height: selectedPost.value.height || 1200,
        alt: `Danbooru original ${selectedPost.value.id}`
      }
    ],
    index: 0,
    bgOpacity: 0.95,
    showHideAnimationType: "fade",
    wheelToZoom: true,
    clickToCloseNonZoomable: false,
    padding: { top: 24, bottom: 24, left: 12, right: 12 }
  });
  activePhotoSwipe.on("destroy", () => {
    activePhotoSwipe = null;
  });
  activePhotoSwipe.init();
}

function closeImageLightbox() {
  activePhotoSwipe?.close();
  activePhotoSwipe = null;
}

async function loadNextPage() {
  if (!isPostGridView.value || loading.value || done.value) return;

  loading.value = true;
  error.value = "";
  const requestId = ++requestSerial;
  controller = new AbortController();

  const params = new URLSearchParams({
    tags: activeTags.value,
    page: String(page.value),
    limit: String(pageLimit.value)
  });
  const endpoint = activeGalleryId.value
      ? `/api/galleries/${encodeURIComponent(activeGalleryId.value)}/posts?${params}`
      : `/api/posts?${params}`;

  try {
    const response = await fetch(endpoint, {
      signal: controller.signal
    });
    const payload = await response.json();

    if (!response.ok) {
      throw new Error(payload.detail || payload.title || "이미지를 불러오지 못했습니다.");
    }

    if (requestId !== requestSerial) {
      return;
    }

    const responsePosts = payload.posts ?? payload.Posts ?? [];
    const responseHasMore = payload.hasMore ?? payload.HasMore ?? false;

    if (activeGalleryCanGrow.value && responsePosts.length === 0) {
      waitingForGalleryCache.value = true;
      scheduleActiveGalleryCacheCheck();
      return;
    }

    posts.value.push(...responsePosts);
    page.value += 1;
    done.value = !responseHasMore && !activeGalleryCanGrow.value;
    waitingForGalleryCache.value = !responseHasMore && activeGalleryCanGrow.value;
    if (waitingForGalleryCache.value) {
      scheduleActiveGalleryCacheCheck();
    }
  } catch (caught) {
    if (caught.name !== "AbortError") {
      error.value = caught.message;
    }
  } finally {
    if (requestId === requestSerial) {
      loading.value = false;
      await nextTick();
      queueLoadMoreCheck();
    }
  }
}

function shouldLoadMore({allowDefaultAll = false} = {}) {
  if (!isPostGridView.value || selectedPostId.value || loading.value || done.value || waitingForGalleryCache.value) {
    return false;
  }

  if (isDefaultAllFeed.value && (!allowDefaultAll || !userScrolledSinceSearch.value)) {
    return false;
  }

  const scroller = document.scrollingElement || document.documentElement;
  const distanceToBottom = scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight;
  if (distanceToBottom < 1200) {
    return true;
  }

  if (!sentinel.value) {
    return false;
  }

  return sentinel.value.getBoundingClientRect().top < window.innerHeight + 900;
}

function handleMediaSettled() {
  if (isDefaultAllFeed.value) {
    return;
  }
  queueLoadMoreCheck();
}

async function resetSearch() {
  clearTimeout(galleryCacheTimer);
  commitDraftTags();
  if (controller) controller.abort();
  abortTagSearch();
  requestSerial += 1;
  currentView.value = "search";
  userScrolledSinceSearch.value = activeTags.value !== "all";
  selectedPostId.value = null;
  activeGalleryId.value = null;
  resetDetailState();
  if (route.name !== routeNames.search) {
    pushRoute({ name: routeNames.search });
  }
  activeTags.value = composeTagQuery();
  userScrolledSinceSearch.value = activeTags.value !== "all";
  posts.value = [];
  settledThumbs.value = new Set();
  failedThumbs.value = new Set();
  page.value = 0;
  done.value = false;
  waitingForGalleryCache.value = false;
  loading.value = false;
  error.value = "";
  await nextTick();
  bindSentinelObserver();
  await loadNextPage();
}

function openDetail(post, pushState = true) {
  selectedPostId.value = post.id;
  directPost.value = posts.value.some((item) => String(item.id) === String(post.id)) ? null : post;
  detailLoading.value = false;
  detailError.value = "";
  originalSrc.value = "";
  originalLoading.value = true;
  originalError.value = "";
  if (pushState) {
    pushRoute(activeGalleryId.value
      ? { name: routeNames.galleryPost, params: { galleryId: activeGalleryId.value, postId: post.id } }
      : { name: routeNames.post, params: { postId: post.id } });
  }

  if (isVideoPost(post)) {
    originalSrc.value = post.originalUrl;
    originalLoading.value = false;
    return;
  }

  if (isArchivePost(post)) {
    originalSrc.value = post.previewUrl;
    originalLoading.value = false;
    return;
  }

  const image = new Image();
  image.onload = () => {
    originalSrc.value = post.originalUrl;
    originalLoading.value = false;
  };
  image.onerror = () => {
    originalError.value = "원본 이미지를 불러오지 못했습니다.";
    originalLoading.value = false;
  };
  image.src = post.originalUrl;
}

function closeDetail(options = true) {
  const pushState = typeof options === "boolean" ? options : options.pushState !== false;
  const loadFallback = typeof options === "boolean" ? options : options.loadFallback !== false;
  selectedPostId.value = null;
  resetDetailState();
  if (pushState) {
    pushRoute(activeGalleryId.value
      ? { name: routeNames.gallery, params: { galleryId: activeGalleryId.value } }
      : { name: routeNames.search });
  }
  if (loadFallback && !activeGalleryId.value && posts.value.length === 0) {
    void nextTick(() => {
      if (currentView.value === "search" && posts.value.length === 0) {
        void loadNextPage();
      }
    });
  }
}

async function loadPostDetail(id) {
  selectedPostId.value = id;
  resetDetailState();
  detailLoading.value = true;

  try {
    const response = await fetch(`/api/posts/${encodeURIComponent(id)}`);
    const payload = await response.json();

    if (!response.ok) {
      throw new Error(payload.detail || payload.error || payload.title || "포스트를 불러오지 못했습니다.");
    }

    directPost.value = payload;
    openDetail(payload, false);
  } catch (caught) {
    detailError.value = caught.message;
  } finally {
    detailLoading.value = false;
  }
}

async function openGalleryPostRoute(galleryId, postId) {
  closeDetail({pushState: false, loadFallback: false});
  currentView.value = "gallery";
  activeGalleryId.value = galleryId;

  await fetchGalleries();
  const gallery = galleries.value.find((item) => item.id === galleryId);
  if (!gallery) {
    currentView.value = "gallery";
    activeGalleryId.value = null;
    selectedPostId.value = null;
    return;
  }

  await openGallery(gallery, false);
  const post = posts.value.find((item) => String(item.id) === String(postId));
  if (post) {
    openDetail(post, false);
  } else {
    await loadPostDetail(postId);
  }
}

function handleRouteChange(nextRoute = route) {
  if (nextRoute.name === routeNames.galleryPost) {
    void openGalleryPostRoute(String(nextRoute.params.galleryId), String(nextRoute.params.postId));
    return;
  }

  if (nextRoute.name === routeNames.post) {
    const postId = String(nextRoute.params.postId);
    const post = posts.value.find((item) => String(item.id) === postId);
    if (post) {
      openDetail(post, false);
    } else {
      void loadPostDetail(postId);
    }
    return;
  }

  if (nextRoute.name === routeNames.galleries) {
    closeDetail({pushState: false, loadFallback: false});
    void showGalleryNav(false);
    return;
  }

  if (nextRoute.name === routeNames.gallery) {
    closeDetail({pushState: false, loadFallback: false});
    void (async () => {
      await fetchGalleries();
      const gallery = galleries.value.find((item) => item.id === String(nextRoute.params.galleryId));
      if (gallery) {
        await openGallery(gallery, false);
      } else {
        currentView.value = "gallery";
        activeGalleryId.value = null;
      }
    })();
    return;
  }

  if (nextRoute.name === routeNames.notFound) {
    if (controller) controller.abort();
    clearTimeout(galleryCacheTimer);
    requestSerial += 1;
    closeDetail({pushState: false, loadFallback: false});
    currentView.value = "not-found";
    activeGalleryId.value = null;
    loading.value = false;
    waitingForGalleryCache.value = false;
    error.value = "";
    return;
  }

  closeDetail({pushState: false, loadFallback: false});
  currentView.value = "search";
  activeGalleryId.value = null;
  if (posts.value.length === 0) {
    void resetSearch();
  }
}

useEventListener(window, "scroll", () => {
  if (isDefaultAllFeed.value) {
    userScrolledSinceSearch.value = true;
    queueLoadMoreCheck({allowDefaultAll: true});
    return;
  }

  queueLoadMoreCheck();
}, {passive: true});

useEventListener(window, "resize", () => {
  queueLoadMoreCheck();
});

watch(sentinel, () => {
  void nextTick(bindSentinelObserver);
});

watch(
  () => route.fullPath,
  () => handleRouteChange(route)
);

onMounted(() => {
  const savedTheme = localStorage.getItem("danbooru-theme");
  if (savedTheme === "light" || savedTheme === "dark" || savedTheme === "system") {
    themeMode.value = savedTheme;
  }
  applyTheme();

  handleRouteChange(route);
  void nextTick(bindSentinelObserver);
  if (!selectedPostId.value && route.name === routeNames.search) {
    void loadNextPage();
  }
  if (route.name !== routeNames.notFound) {
    void fetchGalleries();
  }
});

onBeforeUnmount(() => {
  if (controller) controller.abort();
  abortTagSearch();
  clearTimeout(galleryCacheTimer);
  cancelAnimationFrame(loadMoreFrame);
  sentinelObserver?.disconnect();
  activePhotoSwipe?.destroy();
});
</script>

<template>
  <AppHeader
    v-model:tag-draft="tagDraft"
    :current-view="currentView"
    :theme-mode="themeMode"
    :theme-title="themeTitle"
    :tag-tokens="tagTokens"
    :tag-suggest-open="tagSuggestOpen"
    :tag-suggesting="tagSuggesting"
    :tag-suggestions="tagSuggestions"
    :highlighted-suggestion="highlightedSuggestion"
    :token-class="tokenClass"
    :category-class="categoryClass"
    :category-label="categoryLabel"
    :format-count="formatCount"
    @go-home="goHome"
    @cycle-theme="cycleTheme"
    @show-search="showSearch"
    @show-gallery="showGalleryNav"
    @reset-search="resetSearch"
    @remove-token="removeTagToken"
    @tag-input="onTagDraftInput"
    @tag-focus="scheduleTagSuggestions"
    @tag-keydown="onTagInputKeydown"
    @tag-blur="closeTagSuggestionsSoon"
    @apply-suggestion="applySuggestion"
    @highlight-suggestion="highlightedSuggestion = $event"
  />

  <NotFoundView
    v-if="isNotFoundRoute"
    :path="route.fullPath"
    @home="goHome"
    @back="goBack"
  />

  <main v-else-if="!selectedPostId">
    <SearchStatusToolbar
      v-if="currentView === 'search'"
      :active-tags="activeTags"
      :loaded-count="posts.length"
      :can-save="canSaveActiveGallery"
      :is-saved="isActiveGallerySaved"
      :save-label="gallerySaveLabel"
      @save="saveCurrentGallery"
    />

    <GalleryList
      v-if="currentView === 'gallery' && !activeGalleryId"
      :galleries="galleries"
      :active-gallery-id="activeGalleryId"
      :loading="galleryLoading"
      :error="galleryError"
      :preview-images="galleryPreviewImages"
      :preview-style="galleryPreviewStyle"
      :relative-time="relativeTime"
      @open="openGallery"
      @delete="deleteGallery"
    />

    <GalleryStatusToolbar
      v-if="currentView === 'gallery' && activeGallery"
      :gallery="activeGallery"
      @back="showGalleryNav"
    />

    <PostGrid
      v-if="currentView === 'search' || activeGalleryId"
      :posts="posts"
      :skeleton-card-count="skeletonCardCount"
      :page="page"
      :is-thumb-settled="isThumbSettled"
      :is-thumb-failed="isThumbFailed"
      :is-video-post="isVideoPost"
      :is-archive-post="isArchivePost"
      :rating-label="ratingLabel"
      :rating-class="ratingClass"
      @open-detail="openDetail"
      @thumb-load="markThumbSettled"
      @thumb-error="markThumbFailed"
    />

    <div v-if="currentView === 'search' || activeGalleryId" ref="sentinel" class="sentinel" :class="{ error: error }">
      {{ statusText }}
    </div>
  </main>

  <PostDetail
    v-else
    :post="selectedPost"
    :detail-loading="detailLoading"
    :detail-error="detailError"
    :original-src="originalSrc"
    :original-loading="originalLoading"
    :original-error="originalError"
    :download-url="selectedPost ? originalDownloadUrl(selectedPost) : ''"
    :rating-label="ratingLabel"
    :rating-class="ratingClass"
    :is-video-post="isVideoPost"
    :is-archive-post="isArchivePost"
    :format-date="formatDate"
    :detail-tag-groups="detailTagGroups"
    :category-class="categoryClass"
    :category-label="categoryLabel"
    :tag-icon="tagIcon"
    @close="closeDetail"
    @open-lightbox="openImageLightbox"
    @apply-tag="applyTag"
  />
</template>
