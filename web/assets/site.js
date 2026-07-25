/* Marks the current page in the nav from the filename, so each page does not hand-maintain it. */
document.addEventListener("DOMContentLoaded", function () {
  var page = location.pathname.split("/").pop() || "index.html";
  document.querySelectorAll(".site-nav a").forEach(function (a) {
    if (a.getAttribute("href") === page) { a.setAttribute("aria-current", "page"); }
  });
});
