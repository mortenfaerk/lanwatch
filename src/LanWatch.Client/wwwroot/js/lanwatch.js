// Small browser helpers for LanWatch. Everything else lives in C#.
window.lanwatch = {
  getTheme() {
    try { return localStorage.getItem("lanwatch.theme") || "system"; } catch { return "system"; }
  },
  setTheme(theme) {
    try {
      if (theme === "light" || theme === "dark") {
        localStorage.setItem("lanwatch.theme", theme);
        document.documentElement.setAttribute("data-theme", theme);
      } else {
        localStorage.removeItem("lanwatch.theme");
        document.documentElement.removeAttribute("data-theme");
      }
    } catch { /* storage blocked: theme still applies for this page */ }
  },
  // Pointer x relative to an element, as a 0..1 fraction (charts use it for hover).
  relX(el, clientX) {
    const r = el.getBoundingClientRect();
    return r.width === 0 ? 0 : Math.min(1, Math.max(0, (clientX - r.left) / r.width));
  },
  width(el) {
    return el ? el.getBoundingClientRect().width : 0;
  },
};

document.addEventListener("click", (e) => {
  if (e.target && e.target.classList && e.target.classList.contains("dismiss")) {
    document.getElementById("blazor-error-ui").style.display = "none";
  }
});
