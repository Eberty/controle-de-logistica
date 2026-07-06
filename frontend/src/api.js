export const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5256";
export const TOKEN_KEY = "ams_token";

export async function request(path, { token, method = "GET", body, raw = false } = {}) {
  const response = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (response.ok && raw) {
    return response;
  }

  if (response.status === 204) {
    return null;
  }

  const text = await response.text();
  let payload = null;

  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = { message: text };
    }
  }

  if (!response.ok) {
    const fallback =
      response.status === 429
        ? "Muitas tentativas. Aguarde e tente novamente."
        : "Erro na requisição";
    const message = payload?.message || payload?.title || fallback;
    const error = new Error(message);
    error.status = response.status;
    throw error;
  }

  return payload;
}

export function normalizeIsoDate(value) {
  return typeof value === "string" &&
    /^\d{4}-\d{2}-\d{2}T/.test(value) &&
    !/(Z|[+-]\d{2}:\d{2})$/.test(value)
    ? `${value}Z`
    : value;
}

export function formatDateTime(value) {
  if (!value) return "-";
  return new Intl.DateTimeFormat("pt-BR", {
    dateStyle: "short",
    timeStyle: "short",
  }).format(new Date(normalizeIsoDate(value)));
}

export function formatDate(value) {
  if (!value) return "-";
  return new Intl.DateTimeFormat("pt-BR", {
    dateStyle: "short",
  }).format(new Date(normalizeIsoDate(value)));
}

export function localDateIso(date) {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

export function todayIso() {
  return localDateIso(new Date());
}

export function daysAgoIso(days) {
  const date = new Date();
  date.setDate(date.getDate() - days);
  return localDateIso(date);
}
