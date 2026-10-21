import { BadgeInfo, BookOpen, Sparkles, Tags, UserRound } from "@lucide/vue";

export function ratingLabel(rating) {
  return {
    g: "General",
    s: "Sensitive",
    q: "Questionable",
    e: "Explicit"
  }[rating] || rating || "Unknown";
}

export function ratingClass(rating) {
  return `rating-${rating || "unknown"}`;
}

export function visibleTags(post) {
  if (post?.tagGroups?.length) {
    return post.tagGroups.flatMap((group) =>
      group.tags.map((tag) => ({
        name: tag,
        category: group.category
      }))
    );
  }

  return (post?.tags || "")
    .split(/\s+/)
    .filter(Boolean)
    .map((tag) => ({
      name: tag,
      category: "general"
    }));
}

export function detailTagGroups(post) {
  if (post?.tagGroups?.length) {
    return post.tagGroups.filter((group) => group.tags?.length);
  }

  const tags = visibleTags(post).map((tag) => tag.name);
  return tags.length ? [{ category: "general", tags }] : [];
}

export function isVideoPost(post) {
  return post?.mediaType === "video";
}

export function isArchivePost(post) {
  return post?.mediaType === "archive";
}

export function tokenClass(token) {
  return `tag-token-${tokenCategory(token)}`;
}

export function tokenCategory(token) {
  if (token.startsWith("-")) return "negative";
  if (token.includes(":")) return "meta";
  return "general";
}

export function categoryClass(category) {
  return `tag-token-${normalizeCategory(category)}`;
}

export function normalizeCategory(category) {
  const normalized = String(category || "general").toLowerCase();
  if (["artist", "character", "copyright", "general", "meta", "negative"].includes(normalized)) {
    return normalized;
  }
  return "general";
}

export function categoryLabel(category) {
  return {
    artist: "artist",
    character: "character",
    copyright: "copyright",
    general: "general",
    meta: "meta",
    negative: "exclude"
  }[normalizeCategory(category)] || "tag";
}

export function tagIcon(category) {
  return {
    artist: UserRound,
    character: Sparkles,
    copyright: BookOpen,
    general: Tags,
    meta: BadgeInfo,
    negative: BadgeInfo
  }[normalizeCategory(category)] || Tags;
}

export function formatCount(count) {
  if (count === null || count === undefined) return "";
  return Intl.NumberFormat().format(count);
}

export function formatDate(value) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return new Intl.DateTimeFormat("ko-KR", {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(date);
}

export function relativeTime(value) {
  if (!value) return "아직 갱신 전";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "갱신 시간 알 수 없음";

  const diffSeconds = Math.round((date.getTime() - Date.now()) / 1000);
  const units = [
    ["year", 31536000],
    ["month", 2592000],
    ["day", 86400],
    ["hour", 3600],
    ["minute", 60]
  ];
  const formatter = new Intl.RelativeTimeFormat("ko-KR", { numeric: "auto" });
  for (const [unit, seconds] of units) {
    if (Math.abs(diffSeconds) >= seconds) {
      return formatter.format(Math.round(diffSeconds / seconds), unit);
    }
  }
  return "방금 전";
}
