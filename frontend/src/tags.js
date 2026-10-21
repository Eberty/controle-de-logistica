export function splitTags(tags) {
  if (!tags) return [];

  return tags
    .split(",")
    .map((tag) => tag.trim())
    .filter(Boolean);
}

export function tagColorKey(name) {
  return String(name ?? "").trim().toLocaleLowerCase("pt-BR");
}

function tagTextColor(hexColor) {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hexColor ?? "");
  if (!match) return undefined;

  const [red, green, blue] = match.slice(1).map((part) => parseInt(part, 16));
  const luminance = (0.299 * red + 0.587 * green + 0.114 * blue) / 255;
  return luminance > 0.6 ? "#1b1414" : "#fff7ef";
}

export function tagStyle(color) {
  return color ? { background: color, color: tagTextColor(color) } : undefined;
}
