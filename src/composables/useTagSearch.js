import { ref } from "vue";

export function useTagSearch() {
  const committedTags = ref([]);
  const tagDraft = ref("");
  const tagSuggestions = ref([]);
  const tagSuggesting = ref(false);
  const tagSuggestOpen = ref(false);
  const highlightedSuggestion = ref(-1);
  let tagController;
  let tagSuggestTimer;

  function activeTagToken() {
    return tagDraft.value.trim();
  }

  function autocompleteQuery() {
    return activeTagToken().replace(/^-+/, "");
  }

  function composeTagQuery() {
    return [...committedTags.value, tagDraft.value.trim()]
      .filter(Boolean)
      .join(" ")
      .trim() || "all";
  }

  function scheduleTagSuggestions() {
    clearTimeout(tagSuggestTimer);
    tagSuggestTimer = window.setTimeout(fetchTagSuggestions, 180);
  }

  async function fetchTagSuggestions() {
    const query = autocompleteQuery();
    if (query.length < 2 || query.includes(":")) {
      tagSuggestions.value = [];
      tagSuggestOpen.value = false;
      highlightedSuggestion.value = -1;
      return;
    }

    if (tagController) tagController.abort();
    tagController = new AbortController();
    tagSuggesting.value = true;
    try {
      const response = await fetch(`/api/tags/autocomplete?${new URLSearchParams({ query, limit: "10" })}`, {
        signal: tagController.signal
      });
      const payload = await response.json();
      if (!response.ok) {
        throw new Error(payload.detail || payload.title || "태그 자동완성을 불러오지 못했습니다.");
      }
      tagSuggestions.value = payload;
      tagSuggestOpen.value = payload.length > 0;
      highlightedSuggestion.value = payload.length > 0 ? 0 : -1;
    } catch (caught) {
      if (caught.name !== "AbortError") {
        tagSuggestions.value = [];
        tagSuggestOpen.value = false;
        highlightedSuggestion.value = -1;
      }
    } finally {
      tagSuggesting.value = false;
    }
  }

  function addTagToken(token) {
    const normalized = token.trim();
    if (!normalized) return;
    if (normalized === "all") {
      committedTags.value = [];
      return;
    }
    if (!committedTags.value.includes(normalized)) {
      committedTags.value = [...committedTags.value, normalized];
    }
  }

  function commitDraftTags() {
    tagDraft.value.split(/\s+/).filter(Boolean).forEach(addTagToken);
    tagDraft.value = "";
    tagSuggestOpen.value = false;
    tagSuggestions.value = [];
    highlightedSuggestion.value = -1;
  }

  function applySuggestion(suggestion) {
    const prefix = activeTagToken().startsWith("-") ? "-" : "";
    addTagToken(`${prefix}${suggestion.value}`);
    tagDraft.value = "";
    tagSuggestOpen.value = false;
    tagSuggestions.value = [];
    highlightedSuggestion.value = -1;
  }

  function removeTagToken(index) {
    const next = [...committedTags.value];
    next.splice(index, 1);
    committedTags.value = next;
    scheduleTagSuggestions();
  }

  function onTagDraftInput() {
    if (!/\s/.test(tagDraft.value)) {
      scheduleTagSuggestions();
      return;
    }

    const parts = tagDraft.value.split(/\s+/);
    const tail = !/\s$/.test(tagDraft.value) ? parts.pop() || "" : "";
    parts.filter(Boolean).forEach(addTagToken);
    tagDraft.value = tail;
    scheduleTagSuggestions();
  }

  function closeTagSuggestionsSoon() {
    window.setTimeout(() => {
      tagSuggestOpen.value = false;
      highlightedSuggestion.value = -1;
    }, 120);
  }

  function onTagInputKeydown(event) {
    if (event.key === "Escape") {
      tagSuggestOpen.value = false;
      highlightedSuggestion.value = -1;
      return;
    }

    if (event.key === "Backspace" && !tagDraft.value && committedTags.value.length) {
      event.preventDefault();
      removeTagToken(committedTags.value.length - 1);
      return;
    }

    if ((event.key === "ArrowDown" || event.key === "ArrowUp") && tagSuggestions.value.length) {
      event.preventDefault();
      tagSuggestOpen.value = true;
      const direction = event.key === "ArrowDown" ? 1 : -1;
      const length = tagSuggestions.value.length;
      highlightedSuggestion.value = (highlightedSuggestion.value + direction + length) % length;
      return;
    }

    if ((event.key === "Enter" || event.key === "Tab") && tagSuggestOpen.value && highlightedSuggestion.value >= 0) {
      event.preventDefault();
      applySuggestion(tagSuggestions.value[highlightedSuggestion.value]);
      return;
    }

    if (event.key === "Enter" && tagDraft.value.trim()) {
      commitDraftTags();
    }
  }

  function resetTagSearch() {
    committedTags.value = [];
    tagDraft.value = "";
    tagSuggestOpen.value = false;
    tagSuggestions.value = [];
    highlightedSuggestion.value = -1;
  }

  function abortTagSearch() {
    if (tagController) tagController.abort();
    clearTimeout(tagSuggestTimer);
  }

  return {
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
  };
}
