// public/sei.html forwards the number to the SEI Bahia public search, which only accepts it by POST.
export function buildSeiSearchUrl(number) {
  return `/sei.html?numero=${encodeURIComponent(String(number ?? "").trim())}`;
}

// Restricted processes do not show up in the public search, so the number is also copied
// for the logged-in quick search.
export function copySeiNumber(number) {
  navigator.clipboard?.writeText(String(number ?? "").trim()).catch(() => {});
}
