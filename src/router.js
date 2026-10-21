import { createRouter, createWebHistory } from "vue-router";

export const routeNames = {
  search: "search",
  galleries: "galleries",
  gallery: "gallery",
  post: "post",
  galleryPost: "gallery-post",
  notFound: "not-found"
};

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: routeNames.search, component: { template: "<span />" } },
    { path: "/galleries", name: routeNames.galleries, component: { template: "<span />" } },
    { path: "/galleries/:galleryId", name: routeNames.gallery, component: { template: "<span />" } },
    { path: "/post/:postId", name: routeNames.post, component: { template: "<span />" } },
    { path: "/galleries/:galleryId/post/:postId", name: routeNames.galleryPost, component: { template: "<span />" } },
    { path: "/:pathMatch(.*)*", name: routeNames.notFound, component: { template: "<span />" } }
  ]
});
