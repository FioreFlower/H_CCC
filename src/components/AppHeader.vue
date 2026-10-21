<script setup>
import { Images, Menu, Monitor, Moon, Sun, Tags } from "@lucide/vue";
import { ref } from "vue";
import megadereLogo from "../assets/megadere-logo.png";

defineProps({
  currentView: { type: String, required: true },
  themeMode: { type: String, required: true },
  themeTitle: { type: String, required: true },
  tagTokens: { type: Array, required: true },
  tagSuggestOpen: { type: Boolean, required: true },
  tagSuggesting: { type: Boolean, required: true },
  tagSuggestions: { type: Array, required: true },
  highlightedSuggestion: { type: Number, required: true },
  tokenClass: { type: Function, required: true },
  categoryClass: { type: Function, required: true },
  categoryLabel: { type: Function, required: true },
  formatCount: { type: Function, required: true }
});

const tagDraft = defineModel("tagDraft", { type: String, required: true });
const emit = defineEmits([
  "go-home",
  "cycle-theme",
  "show-search",
  "show-gallery",
  "reset-search",
  "remove-token",
  "tag-input",
  "tag-focus",
  "tag-keydown",
  "tag-blur",
  "apply-suggestion",
  "highlight-suggestion"
]);

const tagInput = ref(null);

function focusTagInput() {
  tagInput.value?.focus();
}
</script>

<template>
  <header class="topbar">
    <div class="topbar-primary">
      <button class="menu-toggle" type="button" aria-label="메뉴 열기" title="메뉴">
        <Menu :size="21" aria-hidden="true" />
      </button>

      <div class="brand">
        <button class="brand-home" type="button" @click="emit('go-home')">
          <img :src="megadereLogo" alt="MEGADERE">
        </button>
      </div>

      <button class="theme-toggle" type="button" :aria-label="themeTitle" :title="themeTitle" @click="emit('cycle-theme')">
        <Monitor v-if="themeMode === 'system'" :size="18" aria-hidden="true" />
        <Sun v-else-if="themeMode === 'light'" :size="18" aria-hidden="true" />
        <Moon v-else :size="18" aria-hidden="true" />
      </button>
    </div>

    <div class="topbar-tools">
      <nav class="main-nav" aria-label="주요 메뉴">
        <button type="button" :class="{ 'is-active': currentView === 'search' }" @click="emit('show-search')">
          <Tags :size="16" aria-hidden="true" />
          <span>Search</span>
        </button>
        <button type="button" :class="{ 'is-active': currentView === 'gallery' }" @click="emit('show-gallery')">
          <Images :size="16" aria-hidden="true" />
          <span>Gallery</span>
        </button>
      </nav>

      <form class="search" autocomplete="off" @submit.prevent="emit('reset-search')">
        <label for="tagsInput">Tags</label>
        <div class="tag-search-field">
          <div class="tag-input-shell" @click="focusTagInput">
            <button
              v-for="(token, index) in tagTokens"
              :key="`${token}-${index}`"
              class="search-token"
              :class="tokenClass(token)"
              type="button"
              @click="emit('remove-token', index)"
            >
              <span>{{ token }}</span>
              <small>×</small>
            </button>
            <input
              id="tagsInput"
              ref="tagInput"
              v-model="tagDraft"
              type="search"
              :placeholder="tagTokens.length ? '태그 추가' : '예: hatsune_miku'"
              autocomplete="off"
              @input="emit('tag-input')"
              @focus="emit('tag-focus')"
              @keydown="emit('tag-keydown', $event)"
              @blur="emit('tag-blur')"
            >
          </div>
          <div v-if="tagSuggestOpen || tagSuggesting" class="tag-suggestions">
            <button
              v-for="(suggestion, index) in tagSuggestions"
              :key="suggestion.value"
              :class="[categoryClass(suggestion.category), { 'is-highlighted': index === highlightedSuggestion }]"
              type="button"
              @mouseenter="emit('highlight-suggestion', index)"
              @mousedown.prevent="emit('apply-suggestion', suggestion)"
            >
              <span>{{ suggestion.value }}</span>
              <small>
                {{ categoryLabel(suggestion.category) }}
                <template v-if="suggestion.postCount !== null && suggestion.postCount !== undefined">
                  · {{ formatCount(suggestion.postCount) }}
                </template>
              </small>
            </button>
            <div v-if="tagSuggesting && tagSuggestions.length === 0" class="tag-suggestion-empty">
              검색 중...
            </div>
          </div>
        </div>
        <button type="submit">검색</button>
      </form>
    </div>
  </header>
</template>
