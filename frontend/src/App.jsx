import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import "./App.css";
import {
  daysAgoIso,
  formatDate,
  formatDateTime,
  localDateIso,
  request,
  todayIso,
  TOKEN_KEY,
} from "./api";
import { Icon } from "./components/Icon";
import { NumberInput } from "./components/NumberInput";
import { TagList } from "./components/TagList";
import {
  emptyCalendarForm,
  emptyCalendarSearch,
  emptyInventoryFilters,
  emptyItemForm,
  emptyNoteForm,
  emptyTransferForm,
  itemConditionOptions,
  itemNatureOptions,
  locationOptions,
} from "./constants";

const auditPageSize = 100;
const auditTimelineLimit = 5000;
const inventoryPageSize = 20;
const INVENTORY_COLUMNS = [
  { key: "quantity", label: "Quantidade" },
  { key: "assetTag", label: "Tombo" },
  { key: "nature", label: "Natureza" },
  { key: "location", label: "Localização" },
  { key: "condition", label: "Conservação" },
  { key: "responsible", label: "Detentor" },
  { key: "discharged", label: "Descargueado" },
  { key: "photo", label: "Foto" },
];
const INVENTORY_COLUMNS_LS_KEY = "ams_inventory_columns";
const VIEW_TITLES = {
  items: "Cadastro de itens",
  search: "Inventário",
  notes: "Anotações privadas",
  mural: "Mural",
  calendar: "Calendário",
  audit: "Auditoria administrativa",
};
const defaultInventoryColumns = INVENTORY_COLUMNS.map((c) => c.key).filter((k) => k !== "responsible");
const usernamePattern = "[A-Za-z0-9._-]+";
const usernamePatternTitle = "Use apenas letras, números, ponto, hífen ou underline.";
const defaultAuditPeriod = "24h";
const auditPeriodOptions = [
  { value: "custom", label: "Selecionar período" },
  { value: "24h", label: "24 horas" },
  { value: "7d", label: "7 dias" },
  { value: "1m", label: "1 mês" },
  { value: "6m", label: "6 meses" },
  { value: "1y", label: "1 ano" },
  { value: "all", label: "Histórico completo" },
];

const emptyManagedUserForm = {
  username: "",
  fullName: "",
  militaryId: "",
  isAdmin: false,
  currentPassword: "",
  password: "",
  adminPassword: "",
};

const emptyRegisterForm = {
  username: "",
  password: "",
  fullName: "",
  isAdmin: false,
  militaryId: "",
  adminUsername: "",
  adminPassword: "",
};

const emptyLocationForm = {
  open: false,
  value: "",
  saving: false,
  editValue: "",
  editName: "",
  renaming: false,
  editing: false,
  removing: false,
};

function subtractMonths(date, months) {
  const day = date.getDate();
  const shifted = new Date(date);
  shifted.setDate(1);
  shifted.setMonth(shifted.getMonth() - months);
  const lastDay = new Date(shifted.getFullYear(), shifted.getMonth() + 1, 0).getDate();
  shifted.setDate(Math.min(day, lastDay));
  return shifted;
}

function getAuditPeriodBounds(period, range) {
  if (period === "all") {
    return {};
  }

  if (period === "custom") {
    return {
      start: range.startDate ? new Date(`${range.startDate}T00:00:00`).toISOString() : "",
      end: range.endDate ? new Date(`${range.endDate}T23:59:59.999`).toISOString() : "",
    };
  }

  const end = new Date();
  let start = new Date(end);

  if (period === "24h") start.setDate(start.getDate() - 1);
  if (period === "7d") start.setDate(start.getDate() - 7);
  if (period === "1m") start = subtractMonths(end, 1);
  if (period === "6m") start = subtractMonths(end, 6);
  if (period === "1y") start = subtractMonths(end, 12);

  return {
    start: start.toISOString(),
    end: end.toISOString(),
  };
}

function buildAuditTimelinePath(period, range) {
  const params = new URLSearchParams();
  const bounds = getAuditPeriodBounds(period, range);

  if (bounds.start) params.set("start", bounds.start);
  if (bounds.end) params.set("end", bounds.end);
  params.set("limit", String(auditTimelineLimit));

  const query = params.toString();
  return `/api/audit/timeline${query ? `?${query}` : ""}`;
}

function getAuditPeriodLabel(period, range) {
  if (period === "custom") {
    const startLabel = range.startDate ? formatIsoDateBr(range.startDate) : "início";
    const endLabel = range.endDate ? formatIsoDateBr(range.endDate) : "fim";
    return `${startLabel} até ${endLabel}`;
  }

  return auditPeriodOptions.find((option) => option.value === period)?.label ?? "Período selecionado";
}

function getUserRoleLabel(role) {
  return role === "Admin" ? "Administrador" : "Usuário";
}

function normalizeFilterText(value) {
  return String(value ?? "").trim().toLocaleLowerCase("pt-BR");
}

function itemMatchesInventoryFilters(item, filters) {
  const nameFilter = normalizeFilterText(filters.name);
  const assetTagFilter = normalizeFilterText(filters.assetTag);
  const responsibleFilter = normalizeFilterText(filters.responsible);

  const matchesName = normalizeFilterText(item.name).includes(nameFilter);
  const matchesAssetTag = normalizeFilterText(item.assetTag).includes(assetTagFilter);
  const matchesCondition = !filters.condition || item.condition === filters.condition;
  const matchesNature = !filters.nature || item.nature === filters.nature;
  const matchesLocation = !filters.location || item.location === filters.location;
  const matchesResponsible = !responsibleFilter || normalizeFilterText(item.responsiblePerson).includes(responsibleFilter);

  return matchesName && matchesAssetTag && matchesCondition && matchesNature && matchesLocation && matchesResponsible;
}

function createDefaultAuditRange() {
  return {
    startDate: daysAgoIso(7),
    endDate: todayIso(),
  };
}

const calendarWeekDayLabels = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];
const calendarMonthNames = [
  "Janeiro",
  "Fevereiro",
  "Março",
  "Abril",
  "Maio",
  "Junho",
  "Julho",
  "Agosto",
  "Setembro",
  "Outubro",
  "Novembro",
  "Dezembro",
];

function buildCalendarYearOptions(selectedYear) {
  const currentYear = new Date().getFullYear();
  const start = Math.min(currentYear - 5, selectedYear);
  const end = Math.max(currentYear + 10, selectedYear);
  const years = [];

  for (let year = start; year <= end; year += 1) years.push(year);

  return years;
}
const calendarUrgentWindowDays = 7;
const calendarSearchWindowOptions = [
  { value: "", label: "Qualquer prazo" },
  { value: "7", label: "7 dias" },
  { value: "15", label: "15 dias" },
  { value: "31", label: "31 dias" },
];

function formatIsoDateBr(isoDate) {
  const [year, month, day] = String(isoDate ?? "").split("-");
  if (!year || !month || !day) return isoDate ?? "";
  return `${day}/${month}/${year}`;
}

function daysUntilIsoDate(isoDate) {
  const target = new Date(`${isoDate}T00:00:00`);
  const today = new Date(`${todayIso()}T00:00:00`);
  return Math.round((target.getTime() - today.getTime()) / 86400000);
}

function isUrgentIsoDate(isoDate) {
  return daysUntilIsoDate(isoDate) < calendarUrgentWindowDays;
}

function buildCalendarDays(year, month) {
  const firstDay = new Date(year, month, 1);
  const gridStart = new Date(year, month, 1 - firstDay.getDay());
  const days = [];

  for (let index = 0; index < 42; index += 1) {
    const date = new Date(gridStart.getFullYear(), gridStart.getMonth(), gridStart.getDate() + index);
    days.push({
      iso: localDateIso(date),
      dayNumber: date.getDate(),
      inMonth: date.getMonth() === month,
    });
  }

  return days;
}

function compressImage(file) {
  return new Promise((resolve, reject) => {
    if (!file.type.startsWith("image/")) {
      reject(new Error("Selecione um arquivo de imagem."));
      return;
    }

    const image = new Image();
    const objectUrl = URL.createObjectURL(file);

    image.onload = () => {
      URL.revokeObjectURL(objectUrl);

      const maxSize = 1280;
      const scale = Math.min(1, maxSize / Math.max(image.width, image.height));
      const canvas = document.createElement("canvas");
      canvas.width = Math.max(1, Math.round(image.width * scale));
      canvas.height = Math.max(1, Math.round(image.height * scale));

      const context = canvas.getContext("2d");
      if (!context) {
        reject(new Error("Não foi possível processar a imagem."));
        return;
      }

      context.drawImage(image, 0, 0, canvas.width, canvas.height);
      resolve(canvas.toDataURL("image/jpeg", 0.75));
    };

    image.onerror = () => {
      URL.revokeObjectURL(objectUrl);
      reject(new Error("Não foi possível ler a imagem selecionada."));
    };

    image.src = objectUrl;
  });
}

function App() {
  const [ready, setReady] = useState(false);
  const [token, setToken] = useState(localStorage.getItem(TOKEN_KEY) ?? "");
  const [user, setUser] = useState(null);
  const [requiresInitialAdmin, setRequiresInitialAdmin] = useState(false);
  const [authMode, setAuthMode] = useState("login");
  const [initialAdminForm, setInitialAdminForm] = useState({ password: "" });
  const [authForm, setAuthForm] = useState({
    username: "",
    password: "",
  });
  const [registerForm, setRegisterForm] = useState(emptyRegisterForm);
  const [authError, setAuthError] = useState("");
  const [authSuccess, setAuthSuccess] = useState("");
  const [busy, setBusy] = useState(false);
  const [items, setItems] = useState([]);
  const [customLocations, setCustomLocations] = useState([]);
  const [newLocationForm, setNewLocationForm] = useState(emptyLocationForm);
  const [itemForm, setItemForm] = useState(emptyItemForm);
  const [editingItemId, setEditingItemId] = useState(null);
  const [itemEditReturnView, setItemEditReturnView] = useState(null);
  const [focusedItemId, setFocusedItemId] = useState(null);
  const [itemHistory, setItemHistory] = useState([]);
  const [transferForm, setTransferForm] = useState(emptyTransferForm);
  const [notes, setNotes] = useState([]);
  const [noteForm, setNoteForm] = useState(emptyNoteForm);
  const [editingNoteId, setEditingNoteId] = useState(null);
  const [noteEditReturnView, setNoteEditReturnView] = useState(null);
  const [muralNotes, setMuralNotes] = useState([]);
  const [calendarEntries, setCalendarEntries] = useState([]);
  const [calendarMonth, setCalendarMonth] = useState(() => {
    const now = new Date();
    return { year: now.getFullYear(), month: now.getMonth() };
  });
  const [selectedCalendarDate, setSelectedCalendarDate] = useState(() => todayIso());
  const [calendarForm, setCalendarForm] = useState(emptyCalendarForm);
  const [editingCalendarEntryId, setEditingCalendarEntryId] = useState(null);
  const [calendarFormOpen, setCalendarFormOpen] = useState(false);
  const [calendarFormReturnToDay, setCalendarFormReturnToDay] = useState(false);
  const [calendarDayModalOpen, setCalendarDayModalOpen] = useState(false);
  const [calendarDetailEntryId, setCalendarDetailEntryId] = useState(null);
  const [calendarSearch, setCalendarSearch] = useState(emptyCalendarSearch);
  const [inventoryFilters, setInventoryFilters] = useState(emptyInventoryFilters);
  const [inventoryPage, setInventoryPage] = useState(1);
  const [visibleColumns, setVisibleColumns] = useState(() => {
    try {
      const saved = localStorage.getItem(INVENTORY_COLUMNS_LS_KEY);
      if (saved) {
        const parsed = JSON.parse(saved);
        if (Array.isArray(parsed) && parsed.length > 0) return parsed;
      }
    } catch {
      return defaultInventoryColumns;
    }
    return defaultInventoryColumns;
  });
  const [columnsMenuOpen, setColumnsMenuOpen] = useState(false);
  const [columnsMenuPos, setColumnsMenuPos] = useState({ top: 0, right: 0 });
  const columnsMenuRef = useRef(null);
  const columnsButtonRef = useRef(null);
  const columnsPortalRef = useRef(null);
  const [auditTimeline, setAuditTimeline] = useState([]);
  const [auditPeriod, setAuditPeriod] = useState(defaultAuditPeriod);
  const [auditCustomRange, setAuditCustomRange] = useState(createDefaultAuditRange);
  const [loadedAuditLabel, setLoadedAuditLabel] = useState(() =>
    getAuditPeriodLabel(defaultAuditPeriod, createDefaultAuditRange()),
  );
  const [auditPage, setAuditPage] = useState(1);
  const [auditLoading, setAuditLoading] = useState(false);
  const [toasts, setToasts] = useState([]);
  const [activeView, setActiveView] = useState("items");
  const [itemAction, setItemAction] = useState(null);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [adminSettingsOpen, setAdminSettingsOpen] = useState(false);
  const [adminUsers, setAdminUsers] = useState([]);
  const [adminSettingsLoading, setAdminSettingsLoading] = useState(false);
  const [editingManagedUserId, setEditingManagedUserId] = useState(null);
  const [managedUserForm, setManagedUserForm] = useState(emptyManagedUserForm);
  const [photoViewer, setPhotoViewer] = useState(null);
  const [itemPhotoUrls, setItemPhotoUrls] = useState({});
  const [conditionForm, setConditionForm] = useState({ condition: "" });
  const keepScrollAtBottomRef = useRef(false);
  const toastTimeoutsRef = useRef(new Map());
  const adminSettingsRef = useRef(null);
  const itemPhotoObjectUrlsRef = useRef(new Map());
  const photoInputRef = useRef(null);
  const itemHistoryRequestRef = useRef(0);
  const auditTimelineRequestRef = useRef(0);
  const workspaceRequestRef = useRef(0);
  const availableLocationOptions = useMemo(
    () =>
      Array.from(new Set([...locationOptions, ...customLocations]))
        .filter(Boolean)
        .sort((left, right) => left.localeCompare(right, "pt-BR")),
    [customLocations]
  );
  const customLocationOptions = useMemo(
    () => customLocations.slice().sort((left, right) => left.localeCompare(right, "pt-BR")),
    [customLocations]
  );
  const calendarDays = useMemo(
    () => buildCalendarDays(calendarMonth.year, calendarMonth.month),
    [calendarMonth]
  );
  const calendarYearOptions = useMemo(
    () => buildCalendarYearOptions(calendarMonth.year),
    [calendarMonth.year]
  );
  const todayIsoDate = todayIso();
  const calendarEntriesByDate = useMemo(() => {
    const map = new Map();
    calendarEntries.forEach((entry) => {
      const list = map.get(entry.dueDate) ?? [];
      list.push(entry);
      map.set(entry.dueDate, list);
    });
    return map;
  }, [calendarEntries]);
  const calendarSearchActive =
    calendarSearch.seiNumber.trim() !== "" || calendarSearch.subject.trim() !== "" || calendarSearch.window !== "";
  const calendarSearchResults = useMemo(() => {
    if (!calendarSearchActive) return [];

    const seiFilter = normalizeFilterText(calendarSearch.seiNumber);
    const subjectFilter = normalizeFilterText(calendarSearch.subject);
    const windowDays = calendarSearch.window ? Number(calendarSearch.window) : null;

    return calendarEntries.filter((entry) => {
      const matchesSei = !seiFilter || normalizeFilterText(entry.seiNumber).includes(seiFilter);
      const matchesSubject = !subjectFilter || normalizeFilterText(entry.subject).includes(subjectFilter);
      const daysUntilDue = daysUntilIsoDate(entry.dueDate);
      const matchesWindow = windowDays === null || (daysUntilDue >= 0 && daysUntilDue <= windowDays);
      return matchesSei && matchesSubject && matchesWindow;
    });
  }, [calendarEntries, calendarSearch, calendarSearchActive]);
  const selectedCalendarDayEntries = selectedCalendarDate
    ? (calendarEntriesByDate.get(selectedCalendarDate) ?? [])
    : [];
  const calendarDetailEntry =
    calendarDetailEntryId == null
      ? null
      : (calendarEntries.find((entry) => entry.id === calendarDetailEntryId) ?? null);

  function setMessage(nextMessage, tone = "error") {
    if (!nextMessage) {
      setToasts((current) => {
        if (!current.some((toast) => toast.tone === "error")) return current;
        current.forEach((toast) => {
          if (toast.tone !== "error") return;
          const timeoutId = toastTimeoutsRef.current.get(toast.id);
          if (timeoutId) window.clearTimeout(timeoutId);
          toastTimeoutsRef.current.delete(toast.id);
        });
        return current.filter((toast) => toast.tone !== "error");
      });
      return;
    }

    const toastId = `${Date.now()}-${Math.random()}`;
    const toast = {
      id: toastId,
      text: String(nextMessage),
      tone,
    };

    setToasts((current) => [...current, toast]);

    const timeoutId = window.setTimeout(() => {
      setToasts((current) => current.filter((item) => item.id !== toastId));
      toastTimeoutsRef.current.delete(toastId);
    }, 8000);

    toastTimeoutsRef.current.set(toastId, timeoutId);
  }

  function sameTextPtBr(a, b) {
    return normalizeFilterText(a) === normalizeFilterText(b);
  }

  function formatMovementOrigin(movement) {
    return movement.fromLocation?.trim() || "-";
  }

  function formatMovementDestination(movement) {
    if (movement.destinationType === "Pessoa") {
      return movement.destinationPerson?.trim() || "-";
    }

    return movement.toLocation || "-";
  }

  function isSameResponsiblePersonMovement(movement) {
    return Boolean(
      movement.destinationType === "Pessoa" &&
        movement.originPerson?.trim() &&
        movement.destinationPerson?.trim() &&
        sameTextPtBr(movement.originPerson, movement.destinationPerson),
    );
  }

  function isReturnToLocationMovement(movement) {
    const origin = movement.fromLocation?.trim();
    const destination = movement.toLocation?.trim();

    return Boolean(
      movement.destinationType !== "Pessoa" &&
        movement.originPerson?.trim() &&
        origin &&
        destination &&
        sameTextPtBr(origin, destination),
    );
  }

  function formatMovementRoute(movement) {
    if (isReturnToLocationMovement(movement)) {
      return `${movement.originPerson.trim()} -> ${formatMovementDestination(movement)}`;
    }

    const origin = formatMovementOrigin(movement);
    const destination = formatMovementDestination(movement);

    if (
      origin !== "-" &&
      destination !== "-" &&
      (movement.destinationType !== "Pessoa"
        ? sameTextPtBr(origin, destination)
        : isSameResponsiblePersonMovement(movement))
    ) {
      return "";
    }

    return `${origin} -> ${destination}`;
  }

  function formatMovementTitle(movement) {
    if (movement.destinationType === "Pessoa") {
      return isSameResponsiblePersonMovement(movement) ? "Atualização" : "Empréstimo";
    }

    if (isReturnToLocationMovement(movement)) return "Devolução";

    const origin = movement.fromLocation?.trim() ?? "";
    const dest = movement.toLocation?.trim() ?? "";
    if (origin && dest && !sameTextPtBr(origin, dest)) return "Transferência";
    return "Atualização";
  }

  function formatMovementConditionChange(movement) {
    const fromCondition = movement.fromCondition?.trim();
    const toCondition = movement.toCondition?.trim();

    if (!fromCondition || !toCondition || fromCondition === toCondition) {
      return "";
    }

    return `${fromCondition} -> ${toCondition}`;
  }

  function formatBooleanLabel(value) {
    return value ? "Sim" : "Não";
  }

  function formatMovementDischargeChange(movement) {
    if (movement.fromIsDischarged === movement.toIsDischarged) {
      return "";
    }

    return `${formatBooleanLabel(movement.fromIsDischarged)} -> ${formatBooleanLabel(movement.toIsDischarged)}`;
  }

  function getItemPhotoUrl(item) {
    return item ? itemPhotoUrls[item.id] ?? "" : "";
  }

  function resetCalendarUi() {
    setCalendarFormOpen(false);
    setCalendarFormReturnToDay(false);
    setCalendarDayModalOpen(false);
    setCalendarDetailEntryId(null);
    setEditingCalendarEntryId(null);
    setCalendarForm(emptyCalendarForm);
    setCalendarSearch(emptyCalendarSearch);
  }

  function navigateTo(view) {
    setNewLocationForm(emptyLocationForm);
    setFocusedItemId(null);
    closeItemAction();
    setItemHistory([]);
    setInventoryFilters(emptyInventoryFilters);
    setInventoryPage(1);
    resetCalendarUi();
    setActiveView(view);
    setMobileMenuOpen(false);
    setAdminSettingsOpen(false);
    setEditingManagedUserId(null);
    setManagedUserForm(emptyManagedUserForm);
  }

  function closeItemAction() {
    setItemAction(null);
    setTransferForm(emptyTransferForm);
    setConditionForm({ condition: "" });
  }

  function isAtPageBottom() {
    const documentElement = document.documentElement;
    return window.scrollY + window.innerHeight >= documentElement.scrollHeight - 8;
  }

  function setAuditPagePreservingBottom(nextPage) {
    keepScrollAtBottomRef.current = isAtPageBottom();
    setAuditPage(nextPage);
  }

  function setInventoryPagePreservingBottom(nextPage) {
    keepScrollAtBottomRef.current = isAtPageBottom();
    setInventoryPage(nextPage);
  }

  useEffect(() => {
    let active = true;

    async function bootstrap() {
      if (!token) {
        try {
          const setup = await request("/api/auth/setup");
          if (!active) return;
          setRequiresInitialAdmin(setup.requiresInitialAdmin);
        } catch {
          // Ignore connection errors and keep the login screen available.
        } finally {
          if (active) setReady(true);
        }
        return;
      }

      try {
        const currentUser = await request("/api/auth/me", { token });
        if (!active) return;
        setUser(currentUser);
        if (currentUser.role !== "Admin") {
          setActiveView("search");
        }

        const [itemsResponse, noteResponse, usersResponse, locationResponse] = await Promise.all([
          request("/api/items", { token }),
          request("/api/me/notes", { token }),
          request("/api/auth/users", { token }),
          request("/api/locations", { token }),
        ]);

        if (!active) return;

        setItems(itemsResponse);
        setNotes(noteResponse);
        setAdminUsers(usersResponse);
        setCustomLocations(locationResponse);

        try {
          const [muralResponse, calendarResponse] = await Promise.all([
            request("/api/mural", { token }),
            request("/api/calendar", { token }),
          ]);

          if (active) {
            setMuralNotes(muralResponse);
            setCalendarEntries(calendarResponse);
          }
        } catch {
          if (active) {
            setMuralNotes([]);
            setCalendarEntries([]);
          }
        }

        if (currentUser.role === "Admin") {
          const auditResponse = await request(buildAuditTimelinePath(defaultAuditPeriod, createDefaultAuditRange()), { token });
          if (active) {
            setAuditTimeline(auditResponse);
            setAuditPage(1);
          }
        } else {
          setAuditTimeline([]);
        }
      } catch (error) {
        if (!active) return;
        if (error.status === 401) {
          localStorage.removeItem(TOKEN_KEY);
          setToken("");
        } else {
          setMessage("Não foi possível carregar os dados. Verifique a conexão e recarregue a página.");
        }
      } finally {
        if (active) setReady(true);
      }
    }

    bootstrap();

    return () => {
      active = false;
    };
  }, [token]);

  useEffect(
    () => () => {
      toastTimeoutsRef.current.forEach((timeoutId) => window.clearTimeout(timeoutId));
      toastTimeoutsRef.current.clear();
    },
    [],
  );

  useEffect(
    () => () => {
      itemPhotoObjectUrlsRef.current.forEach((entry) => URL.revokeObjectURL(entry.url));
      itemPhotoObjectUrlsRef.current.clear();
    },
    [],
  );

  useEffect(() => {
    const clearPhotoUrls = () => {
      itemPhotoObjectUrlsRef.current.forEach((entry) => URL.revokeObjectURL(entry.url));
      itemPhotoObjectUrlsRef.current.clear();
      setItemPhotoUrls({});
    };

    if (!token) {
      clearPhotoUrls();
      return undefined;
    }

    let cancelled = false;
    const itemsById = new Map(items.map((item) => [item.id, item]));

    const filtered = items.filter((item) => itemMatchesInventoryFilters(item, inventoryFilters));
    const totalPages = Math.max(1, Math.ceil(filtered.length / inventoryPageSize));
    const page = Math.min(inventoryPage, totalPages);
    const visibleIds = new Set(
      filtered
        .slice((page - 1) * inventoryPageSize, page * inventoryPageSize)
        .map((item) => item.id),
    );
    if (editingItemId) visibleIds.add(editingItemId);
    if (focusedItemId) visibleIds.add(focusedItemId);

    itemPhotoObjectUrlsRef.current.forEach((entry, itemId) => {
      const item = itemsById.get(itemId);

      if (!item?.photoFileName || item.photoFileName !== entry.fileName || !visibleIds.has(itemId)) {
        URL.revokeObjectURL(entry.url);
        itemPhotoObjectUrlsRef.current.delete(itemId);
      }
    });

    const nextPhotoUrls = {};
    itemPhotoObjectUrlsRef.current.forEach((entry, itemId) => {
      nextPhotoUrls[itemId] = entry.url;
    });
    setItemPhotoUrls(nextPhotoUrls);

    items
      .filter((item) => item.photoFileName && visibleIds.has(item.id))
      .forEach(async (item) => {
        const currentEntry = itemPhotoObjectUrlsRef.current.get(item.id);
        if (currentEntry?.fileName === item.photoFileName) return;

        try {
          const response = await request(`/api/items/${item.id}/photo`, { token, raw: true });
          const blob = await response.blob();
          const objectUrl = URL.createObjectURL(blob);

          if (cancelled) {
            URL.revokeObjectURL(objectUrl);
            return;
          }

          const previousEntry = itemPhotoObjectUrlsRef.current.get(item.id);
          if (previousEntry) {
            URL.revokeObjectURL(previousEntry.url);
          }

          itemPhotoObjectUrlsRef.current.set(item.id, {
            fileName: item.photoFileName,
            url: objectUrl,
          });
          setItemPhotoUrls((current) => ({ ...current, [item.id]: objectUrl }));
        } catch {
          // Missing photos should not block inventory usage.
        }
      });

    return () => {
      cancelled = true;
    };
  }, [items, token, inventoryFilters, inventoryPage, editingItemId, focusedItemId]);

  useEffect(() => {
    if (!adminSettingsOpen) return undefined;

    function handlePointerDown(event) {
      if (adminSettingsRef.current?.contains(event.target)) return;

      setAdminSettingsOpen(false);
      setEditingManagedUserId(null);
      setManagedUserForm(emptyManagedUserForm);
    }

    document.addEventListener("pointerdown", handlePointerDown);
    return () => document.removeEventListener("pointerdown", handlePointerDown);
  }, [adminSettingsOpen]);

  useEffect(() => {
    if (!columnsMenuOpen) return undefined;

    let rafId = 0;
    function updatePos() {
      cancelAnimationFrame(rafId);
      rafId = requestAnimationFrame(() => {
        if (!columnsButtonRef.current) return;
        const rect = columnsButtonRef.current.getBoundingClientRect();
        setColumnsMenuPos({ top: rect.bottom + 8, right: window.innerWidth - rect.right });
      });
    }

    function handlePointerDown(event) {
      if (columnsMenuRef.current?.contains(event.target)) return;
      if (columnsButtonRef.current?.contains(event.target)) return;
      if (columnsPortalRef.current?.contains(event.target)) return;
      setColumnsMenuOpen(false);
    }

    document.addEventListener("pointerdown", handlePointerDown);
    window.addEventListener("scroll", updatePos, true);
    window.addEventListener("resize", updatePos);
    return () => {
      cancelAnimationFrame(rafId);
      document.removeEventListener("pointerdown", handlePointerDown);
      window.removeEventListener("scroll", updatePos, true);
      window.removeEventListener("resize", updatePos);
    };
  }, [columnsMenuOpen]);

  useEffect(() => {
    if (!keepScrollAtBottomRef.current) return undefined;

    let secondFrameId = 0;
    const firstFrameId = window.requestAnimationFrame(() => {
      secondFrameId = window.requestAnimationFrame(() => {
        window.scrollTo({ top: document.documentElement.scrollHeight });
        keepScrollAtBottomRef.current = false;
      });
    });

    return () => {
      window.cancelAnimationFrame(firstFrameId);
      window.cancelAnimationFrame(secondFrameId);
    };
  }, [auditPage, auditTimeline.length, inventoryPage]);

  async function loadWorkspace(currentToken, currentUser = user) {
    const requestId = ++workspaceRequestRef.current;
    const isStale = () => requestId !== workspaceRequestRef.current;
    const [itemsResponse, noteResponse, locationResponse] = await Promise.all([
      request("/api/items", { token: currentToken }),
      request("/api/me/notes", { token: currentToken }),
      request("/api/locations", { token: currentToken }),
    ]);

    if (isStale()) {
      return null;
    }

    setItems(itemsResponse);
    setNotes(noteResponse);
    setCustomLocations(locationResponse);

    try {
      const [muralResponse, calendarResponse] = await Promise.all([
        request("/api/mural", { token: currentToken }),
        request("/api/calendar", { token: currentToken }),
      ]);
      if (!isStale()) {
        setMuralNotes(muralResponse);
        setCalendarEntries(calendarResponse);
      }
    } catch {
      if (!isStale()) {
        setMuralNotes([]);
        setCalendarEntries([]);
      }
    }

    if (isStale()) {
      return null;
    }

    const nextItemId = itemsResponse.find((item) => item.id === focusedItemId)?.id ?? null;
    setFocusedItemId(nextItemId);

    if (nextItemId) {
      await loadItemHistory(currentToken, nextItemId);
    } else {
      setItemHistory([]);
    }

    if (currentUser?.role === "Admin") {
      await loadAuditTimeline(currentToken);
    } else {
      setAuditTimeline([]);
    }

    return itemsResponse;
  }

  async function loadItemHistory(currentToken, itemId) {
    if (!itemId) {
      setItemHistory([]);
      return;
    }

    const requestId = ++itemHistoryRequestRef.current;
    const response = await request(`/api/items/${itemId}/movements`, { token: currentToken });
    if (requestId === itemHistoryRequestRef.current) {
      setItemHistory(response);
    }
  }

  async function refreshData() {
    if (!token) return;
    return await loadWorkspace(token, user);
  }

  async function loadAuditTimeline(currentToken = token, period = auditPeriod, range = auditCustomRange) {
    if (!currentToken) return;

    setAuditLoading(true);

    const requestId = ++auditTimelineRequestRef.current;
    try {
      const auditResponse = await request(buildAuditTimelinePath(period, range), { token: currentToken });
      if (requestId === auditTimelineRequestRef.current) {
        setAuditTimeline(auditResponse);
        setAuditPage(1);
        setLoadedAuditLabel(getAuditPeriodLabel(period, range));
      }
    } catch (error) {
      handleRequestError(error);
    } finally {
      if (requestId === auditTimelineRequestRef.current) {
        setAuditLoading(false);
      }
    }
  }

  async function handleAuditPeriodChange(event) {
    const nextPeriod = event.target.value;
    setAuditPeriod(nextPeriod);
    setAuditPage(1);

    if (nextPeriod !== "custom") {
      await loadAuditTimeline(token, nextPeriod, auditCustomRange);
    }
  }

  async function handleAuditFilterSubmit(event) {
    event.preventDefault();
    await loadAuditTimeline();
  }

  async function handleLogin(event) {
    event.preventDefault();
    setAuthError("");
    setAuthSuccess("");
    setBusy(true);

    try {
      const response = await request("/api/auth/login", {
        method: "POST",
        body: authForm,
      });

      localStorage.setItem(TOKEN_KEY, response.token);
      setToken(response.token);
      setUser(response.user);
      if (response.user.role !== "Admin") {
        setActiveView("search");
      }
    } catch (error) {
      setAuthError(error.message);
    } finally {
      setBusy(false);
      setReady(true);
    }
  }

  async function handleCreateInitialAdmin(event) {
    event.preventDefault();
    setAuthError("");
    setAuthSuccess("");
    setBusy(true);

    try {
      await request("/api/auth/initial-admin", {
        method: "POST",
        body: initialAdminForm,
      });

      setInitialAdminForm({ password: "" });
      setRequiresInitialAdmin(false);
      setAuthMode("login");
      setAuthSuccess("Senha do admin criada.");
    } catch (error) {
      setAuthError(error.message);
    } finally {
      setBusy(false);
      setReady(true);
    }
  }

  async function handleRegister(event) {
    event.preventDefault();
    setAuthError("");
    setAuthSuccess("");
    setBusy(true);

    try {
      await request("/api/auth/register", {
        method: "POST",
        body: registerForm,
      });

      setAuthForm({ username: registerForm.username, password: "" });
      setRegisterForm(emptyRegisterForm);
      setAuthMode("login");
      setAuthSuccess("Cadastro criado. Entre com a senha cadastrada.");
    } catch (error) {
      setAuthError(error.message);
    } finally {
      setBusy(false);
      setReady(true);
    }
  }

  function clearLocalSession() {
    localStorage.removeItem(TOKEN_KEY);
    setToken("");
    setUser(null);
    setItems([]);
    setCustomLocations([]);
    setNewLocationForm(emptyLocationForm);
    setItemHistory([]);
    setAuditTimeline([]);
    setNotes([]);
    setNoteForm(emptyNoteForm);
    setEditingNoteId(null);
    setNoteEditReturnView(null);
    setMuralNotes([]);
    setCalendarEntries([]);
    resetCalendarUi();
    setEditingItemId(null);
    setItemEditReturnView(null);
    setFocusedItemId(null);
    setActiveView("items");
    setMobileMenuOpen(false);
    setAdminSettingsOpen(false);
    setAdminUsers([]);
    setEditingManagedUserId(null);
    setManagedUserForm(emptyManagedUserForm);
  }

  function handleRequestError(error) {
    if (error.status === 401) {
      clearLocalSession();
      setAuthError("Sessão expirada. Entre novamente.");
      return;
    }
    setMessage(error.message);
  }

  async function handleLogout() {
    try {
      await request("/api/auth/logout", { token, method: "POST" });
    } catch {
      // Ignore logout transport errors; local session is still cleared.
    } finally {
      clearLocalSession();
    }
  }

  async function loadAdminUsers(currentToken = token) {
    if (!currentToken) return;

    setAdminSettingsLoading(true);

    try {
      const usersResponse = await request("/api/auth/users", { token: currentToken });
      setAdminUsers(usersResponse);
    } catch (error) {
      handleRequestError(error);
    } finally {
      setAdminSettingsLoading(false);
    }
  }

  function handleToggleAdminSettings() {
    const willOpen = !adminSettingsOpen;
    setAdminSettingsOpen(willOpen);

    if (!willOpen) {
      setEditingManagedUserId(null);
      setManagedUserForm(emptyManagedUserForm);
    }

  }

  function handleStartEditUser(targetUser) {
    setEditingManagedUserId(targetUser.id);
    setManagedUserForm({
      username: targetUser.username,
      fullName: targetUser.fullName,
      militaryId: targetUser.militaryId,
      isAdmin: targetUser.role === "Admin",
      currentPassword: "",
      password: "",
      adminPassword: "",
    });
  }

  function handleCancelEditUser() {
    setEditingManagedUserId(null);
    setManagedUserForm(emptyManagedUserForm);
  }

  async function handleSaveManagedUser(event) {
    event.preventDefault();
    if (!editingManagedUserId) return;

    setBusy(true);
    setMessage("");

    try {
      const updatedUser = await request(`/api/auth/users/${editingManagedUserId}`, {
        token,
        method: "PUT",
        body: managedUserForm,
      });

      setAdminUsers((current) => current.map((item) => (item.id === updatedUser.id ? updatedUser : item)));

      if (updatedUser.id === user?.id) {
        setUser(updatedUser);
      }

      setEditingManagedUserId(null);
      setManagedUserForm(emptyManagedUserForm);
      setMessage("Usuário atualizado.", "success");
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  async function handleDeleteUser(targetUser) {
    if (targetUser.id === user?.id) return;

    const confirmed = window.confirm(`Excluir o usuário "${targetUser.username}"?`);
    if (!confirmed) return;

    setBusy(true);
    setMessage("");

    try {
      await request(`/api/auth/users/${targetUser.id}`, {
        token,
        method: "DELETE",
      });

      setAdminUsers((current) => current.filter((item) => item.id !== targetUser.id));
      setMessage("Usuário excluído.", "success");
      await refreshData();
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  async function handleAddLocation() {
    const nextLocation = newLocationForm.value.trim();
    if (!nextLocation) {
      setMessage("Informe a localização.");
      return;
    }

    const existingLocation = availableLocationOptions.find(
      (location) => location.localeCompare(nextLocation, "pt-BR", { sensitivity: "accent" }) === 0
    );

    if (existingLocation) {
      setItemForm((current) => ({ ...current, location: existingLocation }));
      setNewLocationForm((current) => ({
        ...current,
        open: true,
        saving: false,
      }));
      setMessage("Já existe uma localização igual cadastrada.");
      return;
    }

    setNewLocationForm((current) => ({ ...current, saving: true }));
    setMessage("");

    try {
      const createdLocation = await request("/api/locations", {
        token,
        method: "POST",
        body: { name: nextLocation },
      });

      const resolvedLocation = typeof createdLocation === "string" ? createdLocation : nextLocation;
      setCustomLocations((current) => Array.from(new Set([...current, resolvedLocation])));
      setItemForm((current) => ({ ...current, location: resolvedLocation }));
      setNewLocationForm(emptyLocationForm);
      setMessage("Localização adicionada.", "success");
    } catch (error) {
      handleRequestError(error);
      setNewLocationForm((current) => ({ ...current, saving: false }));
    }
  }

  async function handleEditLocation() {
    const currentLocation = newLocationForm.editName;
    const nextLocation = newLocationForm.editValue.trim();

    if (!currentLocation) {
      setMessage("Selecione a localização para editar.");
      return;
    }

    if (!nextLocation) {
      setMessage("Informe o novo nome da localização.");
      return;
    }

    if (currentLocation === nextLocation) {
      setNewLocationForm((current) => ({
        ...current,
        renaming: false,
        editValue: currentLocation,
        editing: false,
      }));
      return;
    }

    const duplicateLocation = availableLocationOptions.find(
      (location) =>
        location.localeCompare(nextLocation, "pt-BR", { sensitivity: "accent" }) === 0 &&
        location.localeCompare(currentLocation, "pt-BR", { sensitivity: "accent" }) !== 0
    );

    if (duplicateLocation) {
      setMessage("Essa localização já existe.");
      return;
    }

    setNewLocationForm((current) => ({ ...current, editing: true }));
    setMessage("");

    try {
      const updatedLocation = await request("/api/locations", {
        token,
        method: "PUT",
        body: { currentName: currentLocation, newName: nextLocation },
      });

      const resolvedLocation = typeof updatedLocation === "string" ? updatedLocation : nextLocation;
      setCustomLocations((current) =>
        current
          .map((location) => (location === currentLocation ? resolvedLocation : location))
          .filter((location, index, locations) => locations.indexOf(location) === index)
      );
      setItemForm((current) => ({
        ...current,
        location: current.location === currentLocation ? resolvedLocation : current.location,
      }));
      setInventoryFilters((current) => ({
        ...current,
        location: current.location === currentLocation ? resolvedLocation : current.location,
      }));
      setNewLocationForm(emptyLocationForm);
      setMessage("Localização editada.", "success");
      await refreshData();
    } catch (error) {
      handleRequestError(error);
      setNewLocationForm((current) => ({ ...current, editing: false }));
    }
  }

  async function handleRenameLocation() {
    if (!newLocationForm.editName) {
      setMessage("Selecione a localização para renomear.");
      return;
    }

    if (!newLocationForm.renaming) {
      setNewLocationForm((current) => ({
        ...current,
        renaming: true,
        editValue: current.editValue || current.editName,
      }));
      return;
    }

    await handleEditLocation();
  }

  async function handleRemoveLocation() {
    const locationToRemove = newLocationForm.editName;
    if (!locationToRemove) {
      setMessage("Selecione a localização para excluir.");
      return;
    }

    if (!window.confirm(`Excluir a localização "${locationToRemove}"?`)) return;

    setNewLocationForm((current) => ({ ...current, removing: true }));
    setMessage("");

    try {
      await request("/api/locations", {
        token,
        method: "DELETE",
        body: { name: locationToRemove },
      });

      setCustomLocations((current) => current.filter((location) => location !== locationToRemove));
      setItemForm((current) => ({
        ...current,
        location: current.location === locationToRemove ? "" : current.location,
      }));
      setTransferForm((current) => ({
        ...current,
        toLocation: current.toLocation === locationToRemove ? "" : current.toLocation,
      }));
      setNewLocationForm((current) => ({
        ...current,
        editName: "",
        editValue: "",
        removing: false,
      }));
      setMessage("Localização excluída.", "success");
    } catch (error) {
      handleRequestError(error);
      setNewLocationForm((current) => ({ ...current, removing: false }));
    }
  }

  async function handleSaveItem(event) {
    event.preventDefault();
    setBusy(true);
    setMessage("");

    try {
      const payload = {
        name: itemForm.name,
        quantity: Number(itemForm.quantity),
        assetTag: itemForm.assetTag,
        nature: itemForm.nature,
        location: itemForm.location,
        condition: itemForm.condition,
        notes: itemForm.notes,
        photoDataUrl: itemForm.photoDataUrl,
        removePhoto: itemForm.removePhoto,
        isDischarged: itemForm.isDischarged,
      };

      const wasEditingItem = Boolean(editingItemId);
      let savedItem = null;

      if (wasEditingItem) {
        savedItem = await request(`/api/items/${editingItemId}`, {
          token,
          method: "PUT",
          body: payload,
        });
        setMessage("Item atualizado com sucesso.", "success");
      } else {
        await request("/api/items", {
          token,
          method: "POST",
          body: payload,
        });
        setMessage("Item criado com sucesso.", "success");
      }

      setItemForm(emptyItemForm);
      setNewLocationForm(emptyLocationForm);
      setEditingItemId(null);
      setItemEditReturnView(null);
      const freshItems = await refreshData();
      if (wasEditingItem) {
        setActiveView("search");
        if (savedItem && Array.isArray(freshItems)) {
          const filtered = freshItems.filter((item) => itemMatchesInventoryFilters(item, inventoryFilters));
          const itemIndex = filtered.findIndex((item) => item.id === savedItem.id);
          if (itemIndex >= 0) {
            setInventoryPage(Math.floor(itemIndex / inventoryPageSize) + 1);
          }
        }
      }
      setItemAction(null);
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  async function handleDeleteItem(itemId) {
    if (!window.confirm("Excluir este item do inventário?")) return;

    setBusy(true);
    try {
      await request(`/api/items/${itemId}`, { token, method: "DELETE" });
      if (editingItemId === itemId) {
        setEditingItemId(null);
        setItemEditReturnView(null);
        setItemForm(emptyItemForm);
      }
      if (focusedItemId === itemId) {
        setFocusedItemId(null);
        closeItemAction();
      }
      await refreshData();
      setMessage("Item removido do inventário.", "success");
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  function handleEditItem(item) {
    setItemEditReturnView(activeView);
    setActiveView("items");
    closeItemAction();
    setEditingItemId(item.id);
    setNewLocationForm(emptyLocationForm);
    setItemForm({
      name: item.name ?? "",
      quantity: item.quantity ?? 0,
      assetTag: item.assetTag ?? "",
      nature: item.nature ?? "",
      location: item.location ?? "",
      condition: item.condition ?? "",
      notes: item.notes ?? "",
      photoDataUrl: "",
      photoPreviewUrl: getItemPhotoUrl(item),
      hasPhoto: Boolean(item.photoFileName),
      removePhoto: false,
      isDischarged: Boolean(item.isDischarged),
    });
  }

  async function setItemPhotoFile(file) {
    if (!file) return;

    try {
      const photoDataUrl = await compressImage(file);
      setItemForm((current) => ({
        ...current,
        photoDataUrl,
        photoPreviewUrl: photoDataUrl,
        hasPhoto: true,
        removePhoto: false,
      }));
    } catch (error) {
      handleRequestError(error);
    }
  }

  async function handleItemPhotoChange(event) {
    await setItemPhotoFile(event.target.files?.[0]);
    event.target.value = "";
  }

  async function handleItemPhotoDrop(event) {
    event.preventDefault();
    await setItemPhotoFile(event.dataTransfer.files?.[0]);
  }

  function handleRemoveItemPhoto() {
    setItemForm((current) => ({
      ...current,
      photoDataUrl: "",
      photoPreviewUrl: "",
      hasPhoto: false,
      removePhoto: Boolean(editingItemId),
    }));
  }

  function handleCancelItemEdit() {
    setEditingItemId(null);
    setItemEditReturnView(null);
    setItemForm(emptyItemForm);
    setActiveView(itemEditReturnView ?? "search");
  }

  async function handleTransfer(event) {
    event.preventDefault();
    setBusy(true);
    setMessage("");

    try {
      if (!transferForm.itemId) {
        throw new Error("Selecione um item para a transferência.");
      }

      const selectedTransferItem = items.find((item) => String(item.id) === transferForm.itemId);
      const resolvedFromLocation = transferForm.fromLocation.trim() || selectedTransferItem?.location?.trim() || "";

      if (!selectedTransferItem) {
        throw new Error("Item selecionado não encontrado.");
      }

      if (Number(transferForm.quantity) > selectedTransferItem.quantity) {
        throw new Error("A quantidade não pode ser maior que o estoque atual do item.");
      }

      if (transferForm.destinationType === "Local" && !transferForm.toLocation.trim()) {
        throw new Error("Informe o destino físico da transferência.");
      }

      const clearsResponsiblePerson =
        transferForm.destinationType === "Local" &&
        Boolean(selectedTransferItem.responsiblePerson?.trim());
      const sameResponsiblePerson =
        transferForm.destinationType === "Pessoa" &&
        Boolean(selectedTransferItem.responsiblePerson?.trim()) &&
        sameTextPtBr(transferForm.destinationPerson, selectedTransferItem.responsiblePerson);

      if (
        transferForm.destinationType === "Local" &&
        sameTextPtBr(transferForm.toLocation, selectedTransferItem.location) &&
        !clearsResponsiblePerson
      ) {
        throw new Error("Altere o destino do item.");
      }

      if (transferForm.destinationType === "Pessoa" && !transferForm.destinationPerson.trim()) {
        throw new Error("Informe o militar responsável pelo recebimento.");
      }

      if (sameResponsiblePerson) {
        throw new Error("Altere o responsável pelo item.");
      }

      await request("/api/movements", {
        token,
        method: "POST",
        body: {
          itemId: Number(transferForm.itemId),
          quantity: Number(transferForm.quantity),
          fromLocation: resolvedFromLocation,
          destinationType: transferForm.destinationType,
          toLocation: transferForm.toLocation,
          destinationPerson: transferForm.destinationPerson,
          condition: selectedTransferItem.condition,
          isDischarged: Boolean(selectedTransferItem.isDischarged),
          notes: "",
        },
      });

      setMessage("Transferência registrada com sucesso.", "success");
      closeItemAction();
      await refreshData();
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  async function handleUpdateItemCondition(event) {
    event.preventDefault();
    setBusy(true);
    setMessage("");

    try {
      if (!selectedItem) {
        throw new Error("Selecione um item para alterar a conservação.");
      }

      const nextCondition = conditionForm.condition.trim();
      const currentCondition = selectedItem.condition?.trim() ?? "";

      if (!nextCondition) {
        throw new Error("Informe o estado de conservação.");
      }

      if (nextCondition === currentCondition) {
        throw new Error("Selecione uma conservação diferente da atual.");
      }

      await request("/api/movements", {
        token,
        method: "POST",
        body: {
          itemId: selectedItem.id,
          quantity: selectedItem.quantity,
          fromLocation: selectedItem.location,
          destinationType: selectedItem.responsiblePerson?.trim() ? "Pessoa" : "Local",
          toLocation: selectedItem.location,
          destinationPerson: selectedItem.responsiblePerson?.trim() ?? "",
          condition: nextCondition,
          isDischarged: Boolean(selectedItem.isDischarged),
          notes: "",
        },
      });

      setMessage("Conservação atualizada com sucesso.", "success");
      closeItemAction();
      await refreshData();
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  async function loadInto(endpoint, setter, currentToken = token) {
    const response = await request(endpoint, { token: currentToken });
    setter(response);
  }

  async function loadNotes(currentToken = token) {
    await loadInto("/api/me/notes", setNotes, currentToken);
  }

  async function loadMural(currentToken = token) {
    await loadInto("/api/mural", setMuralNotes, currentToken);
  }

  async function loadCalendar(currentToken = token) {
    await loadInto("/api/calendar", setCalendarEntries, currentToken);
  }

  async function handleSaveNote(event) {
    event.preventDefault();
    setBusy(true);
    setMessage("");

    try {
      const payload = {
        title: noteForm.title,
        content: noteForm.content,
        tags: noteForm.tags,
        isPublic: noteForm.isPublic,
      };

      await request(editingNoteId ? `/api/me/notes/${editingNoteId}` : "/api/me/notes", {
        token,
        method: editingNoteId ? "PUT" : "POST",
        body: payload,
      });
      setMessage(editingNoteId ? "Anotação atualizada." : "Anotação criada.", "success");
      setNoteForm(emptyNoteForm);
      setEditingNoteId(null);
      if (editingNoteId && noteEditReturnView) {
        setActiveView(noteEditReturnView);
      }
      setNoteEditReturnView(null);
      await Promise.all([loadNotes(), loadMural()]);
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  function handleEditNote(noteItem) {
    setEditingNoteId(noteItem.id);
    setNoteEditReturnView(null);
    setNoteForm({
      title: noteItem.title ?? "",
      content: noteItem.content ?? "",
      tags: noteItem.tags ?? "",
      isPublic: Boolean(noteItem.isPublic),
    });
  }

  async function handleEditMuralNote(muralNote) {
    let ownNote = notes.find((noteItem) => noteItem.id === muralNote.id);

    if (!ownNote) {
      try {
        const response = await request("/api/me/notes", { token });
        setNotes(response);
        ownNote = response.find((noteItem) => noteItem.id === muralNote.id);
      } catch (error) {
        handleRequestError(error);
        return;
      }
    }

    if (!ownNote) {
      setMessage("Anotação não encontrada. O mural foi atualizado.");
      await loadMural();
      return;
    }

    navigateTo("notes");
    handleEditNote(ownNote);
    setNoteEditReturnView("mural");
  }

  async function handleDeleteNote(noteId) {
    if (!window.confirm("Excluir esta anotação?")) return;

    setBusy(true);
    setMessage("");

    try {
      await request(`/api/me/notes/${noteId}`, { token, method: "DELETE" });
      if (editingNoteId === noteId) {
        setEditingNoteId(null);
        setNoteForm(emptyNoteForm);
        setNoteEditReturnView(null);
      }
      setMessage("Anotação excluída.", "success");
      await Promise.all([loadNotes(), loadMural()]);
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  function canManageCalendarEntry(entry) {
    return isAdmin || entry.createdByUserId === user?.id;
  }

  function openCalendarCreate(dateIso = "", returnToDay = false) {
    setEditingCalendarEntryId(null);
    setCalendarForm({
      ...emptyCalendarForm,
      dueDate: dateIso || selectedCalendarDate || todayIso(),
    });
    setCalendarDetailEntryId(null);
    setCalendarDayModalOpen(false);
    setCalendarFormReturnToDay(returnToDay);
    setCalendarFormOpen(true);
  }

  function openCalendarEdit(entry, returnToDay = false) {
    setEditingCalendarEntryId(entry.id);
    setCalendarForm({
      dueDate: entry.dueDate ?? "",
      seiNumber: entry.seiNumber ?? "",
      subject: entry.subject ?? "",
      notes: entry.notes ?? "",
    });
    setCalendarDetailEntryId(null);
    setCalendarDayModalOpen(false);
    setCalendarFormReturnToDay(returnToDay);
    setCalendarFormOpen(true);
  }

  function openCalendarDayModal(dateIso) {
    setSelectedCalendarDate(dateIso);
    setCalendarDetailEntryId(null);
    setCalendarDayModalOpen(true);
  }

  function closeCalendarDayModal() {
    setCalendarDayModalOpen(false);
    setCalendarDetailEntryId(null);
  }

  function renderCalendarDetailBody(entry, fromDayModal = false) {
    return (
      <>
        <div className="detail-summary">
          {fromDayModal ? null : (
            <p>
              <strong>Prazo:</strong> {formatIsoDateBr(entry.dueDate)}
            </p>
          )}
          <p>
            <strong>Número do SEI:</strong> {entry.seiNumber?.trim() || "-"}
          </p>
          <p className="item-notes-display">
            <strong>Observações:</strong> {entry.notes?.trim() || "-"}
          </p>
          <p>
            <strong>Criado por:</strong> {entry.authorName}
          </p>
          <small>Criado em {formatDateTime(entry.createdAt)}</small>
        </div>
        {canManageCalendarEntry(entry) ? (
          <div className="button-row">
            <button className="primary" type="button" onClick={() => openCalendarEdit(entry, fromDayModal)}>
              Editar
            </button>
            <button
              className="ghost"
              type="button"
              onClick={() => handleDeleteCalendarEntry(entry.id)}
              disabled={busy}
            >
              Excluir
            </button>
          </div>
        ) : null}
      </>
    );
  }

  function closeCalendarForm() {
    const returnEntryId = calendarFormReturnToDay ? editingCalendarEntryId : null;
    setCalendarFormOpen(false);
    setEditingCalendarEntryId(null);
    setCalendarForm(emptyCalendarForm);
    if (calendarFormReturnToDay) {
      setCalendarFormReturnToDay(false);
      setCalendarDetailEntryId(returnEntryId);
      setCalendarDayModalOpen(true);
    }
  }

  async function handleSaveCalendarEntry(event) {
    event.preventDefault();
    setBusy(true);
    setMessage("");

    try {
      const payload = {
        dueDate: calendarForm.dueDate,
        seiNumber: calendarForm.seiNumber,
        subject: calendarForm.subject,
        notes: calendarForm.notes,
      };

      await request(editingCalendarEntryId ? `/api/calendar/${editingCalendarEntryId}` : "/api/calendar", {
        token,
        method: editingCalendarEntryId ? "PUT" : "POST",
        body: payload,
      });
      setMessage(
        editingCalendarEntryId ? "Anotação do calendário atualizada." : "Anotação adicionada ao calendário.",
        "success",
      );
      const [savedYear, savedMonth] = calendarForm.dueDate.split("-").map(Number);
      if (savedYear && savedMonth) {
        setCalendarMonth({ year: savedYear, month: savedMonth - 1 });
      }
      setSelectedCalendarDate(calendarForm.dueDate);
      closeCalendarForm();
      await loadCalendar();
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  async function handleDeleteCalendarEntry(entryId) {
    if (!window.confirm("Excluir esta anotação do calendário?")) return;

    setBusy(true);
    setMessage("");

    try {
      await request(`/api/calendar/${entryId}`, { token, method: "DELETE" });
      setCalendarDetailEntryId(null);
      setMessage("Anotação do calendário excluída.", "success");
      await loadCalendar();
    } catch (error) {
      handleRequestError(error);
    } finally {
      setBusy(false);
    }
  }

  function changeCalendarMonth(offset) {
    setCalendarMonth((current) => {
      const date = new Date(current.year, current.month + offset, 1);
      return { year: date.getFullYear(), month: date.getMonth() };
    });
  }

  function goToCalendarToday() {
    const now = new Date();
    setCalendarMonth({ year: now.getFullYear(), month: now.getMonth() });
    setSelectedCalendarDate(localDateIso(now));
  }

  function makeItemActionHandler(actionType, loader = null) {
    return async function (itemId) {
      if (focusedItemId === itemId && itemAction === actionType) {
        closeItemAction();
        return;
      }

      setFocusedItemId(itemId);
      setActiveView("search");
      setItemAction(actionType);
      if (loader) {
        setItemHistory([]);
      }

      if (!loader) return;

      setBusy(true);
      try {
        await loader(token, itemId);
      } catch (error) {
        handleRequestError(error);
      } finally {
        setBusy(false);
      }
    };
  }

  const handleSelectHistoryItem = makeItemActionHandler("history", loadItemHistory);
  const handleSelectItemNotes = makeItemActionHandler("notes");

  function handleSelectTransferItem(item) {
    if (focusedItemId === item.id && itemAction === "transfer") {
      closeItemAction();
      return;
    }

    setFocusedItemId(item.id);
    setActiveView("search");
    setItemAction("transfer");
    setTransferForm({
      ...emptyTransferForm,
      itemId: String(item.id),
      quantity: item.quantity ?? emptyTransferForm.quantity,
      fromLocation: item.location ?? "",
      toLocation: item.location ?? "",
    });
  }

  function handleSelectConditionItem(item) {
    if (focusedItemId === item.id && itemAction === "condition") {
      closeItemAction();
      return;
    }

    setFocusedItemId(item.id);
    setActiveView("search");
    setItemAction("condition");
    setConditionForm({ condition: item.condition ?? "" });
  }

  function toggleColumn(key) {
    setVisibleColumns((prev) => {
      const next = prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key];
      try {
        localStorage.setItem(INVENTORY_COLUMNS_LS_KEY, JSON.stringify(next));
      } catch {
        // Prefer keeping the UI responsive when browser storage is unavailable.
      }
      return next;
    });
  }

  function handleExportInventoryPdf() {
    const rows = filteredItems.map((item) => [
      item.name,
      item.quantity,
      item.assetTag,
      item.nature,
      `${item.location}${item.responsiblePerson?.trim() ? ` (${item.responsiblePerson.trim()})` : ""}`,
      item.condition,
      item.isDischarged ? formatDate(item.dischargedAt) : "-",
    ]);

    if (!printReport({
      iframeTitle: "Exportação do inventário",
      title: "Inventário",
      subtitle: `Exportado em ${formatDateTime(new Date().toISOString())}`,
      tableStyle: "table { font-size: 11px; } th:first-child, td:first-child { min-width: 20%; }",
      columns: ["Item", "Qtd.", "Tombo", "Natureza", "Localização", "Conservação", "Desc."],
      rows,
      emptyMessage: "Nenhum item encontrado.",
    })) {
      setMessage("Não foi possível preparar a exportação em PDF.");
    }
  }

  function handleExportAuditPdf() {
    const rows = auditTimeline.map((entry) => [
      formatDateTime(entry.timestamp),
      entry.actorUserName,
      entry.action,
      entry.entityType,
      entry.summary,
      entry.details,
    ]);

    if (!printReport({
      iframeTitle: "Exportação da auditoria",
      title: "Auditoria administrativa",
      subtitle: `Período: ${loadedAuditLabel} · Exportado em ${formatDateTime(new Date().toISOString())}`,
      tableStyle: "table { font-size: 10px; }",
      columns: ["Data", "Responsável", "Ação", "Área", "Resumo", "Detalhes"],
      rows,
      emptyMessage: "Nenhum registro de auditoria encontrado.",
    })) {
      setMessage("Não foi possível preparar a exportação em PDF.");
    }
  }

  const selectedItem = items.find((item) => item.id === focusedItemId) ?? null;
  const selectedTransferItem = items.find((item) => String(item.id) === transferForm.itemId) ?? null;
  const selectedTransferMaxQuantity = selectedTransferItem?.quantity ?? 0;
  const isAdmin = user?.role === "Admin";
  const sortedAdminUsers = [...adminUsers].sort((first, second) => {
    if (first.id === user?.id) return -1;
    if (second.id === user?.id) return 1;
    return (
      first.fullName.localeCompare(second.fullName, "pt-BR", { sensitivity: "base" }) ||
      first.username.localeCompare(second.username, "pt-BR", { sensitivity: "base" })
    );
  });
  const editingManagedUser = adminUsers.find((item) => item.id === editingManagedUserId) ?? null;
  const isManagedAdminLocked = editingManagedUser?.username?.toLowerCase() === "admin";
  const canEditManagedPassword =
    (isAdmin || editingManagedUser?.id === user?.id) && !(isManagedAdminLocked && editingManagedUser?.id !== user?.id);
  const wantsToChangeManagedUsername =
    editingManagedUser?.id === user?.id && managedUserForm.username.trim() !== (editingManagedUser?.username ?? "");
  const wantsToChangeManagedPassword = managedUserForm.password.trim().length > 0;
  const requiresManagedCurrentPassword =
    editingManagedUser?.id === user?.id && (wantsToChangeManagedUsername || wantsToChangeManagedPassword);
  const canShowManagedAdminOption = isAdmin;
  const adminWillPromoteManagedUser =
    Boolean(editingManagedUser) && isAdmin && editingManagedUser.role !== "Admin" && managedUserForm.isAdmin;
  const adminWillChangeOtherPassword =
    Boolean(editingManagedUser) && isAdmin && editingManagedUser.id !== user?.id && wantsToChangeManagedPassword;
  const requiresManagedAdminPassword =
    adminWillPromoteManagedUser || adminWillChangeOtherPassword;
  const hasInventoryFilters = Object.values(inventoryFilters).some(Boolean);
  const auditTotalPages = Math.max(1, Math.ceil(auditTimeline.length / auditPageSize));
  const safeAuditPage = Math.min(auditPage, auditTotalPages);
  const paginatedAuditTimeline = auditTimeline.slice((safeAuditPage - 1) * auditPageSize, safeAuditPage * auditPageSize);
  const filteredItems = useMemo(
    () => items.filter((item) => itemMatchesInventoryFilters(item, inventoryFilters)),
    [items, inventoryFilters],
  );
  const inventoryTotalPages = Math.max(1, Math.ceil(filteredItems.length / inventoryPageSize));
  const safeInventoryPage = Math.min(inventoryPage, inventoryTotalPages);
  const paginatedInventoryItems = useMemo(
    () =>
      filteredItems.slice(
        (safeInventoryPage - 1) * inventoryPageSize,
        safeInventoryPage * inventoryPageSize,
      ),
    [filteredItems, safeInventoryPage],
  );
  const selectedItemIsOnInventoryPage = paginatedInventoryItems.some((item) => item.id === focusedItemId);
  const itemFormPhotoPreviewUrl = itemForm.removePhoto
    ? ""
    : itemForm.photoPreviewUrl || (editingItemId ? itemPhotoUrls[editingItemId] ?? "" : "");

  if (!ready) {
    return (
      <main className="shell loading-shell">
        <div className="panel hero-panel">
          <p className="eyebrow">Controle Patrimonial 18º BBM</p>
          <h1>Carregando ambiente de operação...</h1>
          <p className="muted">Validando sessão e sincronizando dados iniciais.</p>
        </div>
      </main>
    );
  }

  if (!user) {
    return (
      <main className="shell auth-shell">
        <section className="auth-card">
          <div className="auth-copy">
            <h1 className="auth-title">CONTROLE PATRIMONIAL</h1>
            <p>18º Batalhão de Bombeiros Militar</p>
            <p className="auth-subtitle-small">Seção de Logística e Telemática</p>
            <img className="unit-crest" src="/18bbm.png" alt="Brasão do 18º BBM" />
          </div>

          <form
            className="panel auth-form"
            autoComplete={!requiresInitialAdmin && authMode === "register" ? "off" : "on"}
            onSubmit={
              requiresInitialAdmin ? handleCreateInitialAdmin : authMode === "login" ? handleLogin : handleRegister
            }
          >
            {requiresInitialAdmin ? (
              <>
                <label>
                  Usuário
                  <input value="admin" disabled />
                </label>
                <label>
                  Crie a senha do administrador
                  <input
                    name="initial-admin-password"
                    autoComplete="new-password"
                    type="password"
                    value={initialAdminForm.password}
                    onChange={(event) => setInitialAdminForm({ password: event.target.value })}
                    placeholder="Senha do admin"
                    required
                  />
                </label>
              </>
            ) : authMode === "login" ? (
              <>
                <label>
                  Usuário
                  <input
                    name="username"
                    autoComplete="username"
                    value={authForm.username}
                    onChange={(event) => setAuthForm((current) => ({ ...current, username: event.target.value }))}
                    placeholder="Seu usuário"
                    required
                  />
                </label>
                <label>
                  Senha
                  <input
                    name="current-password"
                    autoComplete="current-password"
                    type="password"
                    value={authForm.password}
                    onChange={(event) => setAuthForm((current) => ({ ...current, password: event.target.value }))}
                    placeholder="Sua senha"
                    required
                  />
                </label>
              </>
            ) : (
              <>
                <label>
                  Usuário
                  <input
                    name="register-user-field"
                    autoComplete="new-password"
                    autoCapitalize="none"
                    spellCheck={false}
                    pattern={usernamePattern}
                    title={usernamePatternTitle}
                    value={registerForm.username}
                    onChange={(event) => setRegisterForm((current) => ({ ...current, username: event.target.value }))}
                    placeholder="Usuário de acesso"
                    required
                  />
                </label>
                <label>
                  Senha
                  <input
                    name="register-secret-field"
                    autoComplete="new-password"
                    type="password"
                    value={registerForm.password}
                    onChange={(event) => setRegisterForm((current) => ({ ...current, password: event.target.value }))}
                    placeholder="Senha de acesso"
                    required
                  />
                </label>
                <label>
                  Nome completo
                  <input
                    name="full-name"
                    autoComplete="off"
                    value={registerForm.fullName}
                    onChange={(event) => setRegisterForm((current) => ({ ...current, fullName: event.target.value }))}
                    placeholder="Nome da pessoa"
                    required
                  />
                </label>
                <label>
                  Identificação funcional
                  <input
                    name="employee-id"
                    autoComplete="off"
                    value={registerForm.militaryId}
                    onChange={(event) => setRegisterForm((current) => ({ ...current, militaryId: event.target.value }))}
                    placeholder="Matrícula ou identificação"
                    required
                  />
                </label>
                <p className="auth-admin-note">
                  O cadastro precisa ser autorizado por um administrador
                </p>
                <label>
                  Usuário admin
                  <input
                    name="admin-username"
                    autoComplete="username"
                    value={registerForm.adminUsername}
                    onChange={(event) =>
                      setRegisterForm((current) => ({ ...current, adminUsername: event.target.value }))
                    }
                    placeholder="Admin autorizador"
                    required
                  />
                </label>
                <label>
                  Senha do administrador
                  <input
                    name="admin-password"
                    autoComplete="current-password"
                    type="password"
                    value={registerForm.adminPassword}
                    onChange={(event) =>
                      setRegisterForm((current) => ({ ...current, adminPassword: event.target.value }))
                    }
                    placeholder="Senha do admin autorizador"
                    required
                  />
                </label>
                <label className="checkbox-field">
                  <input
                    type="checkbox"
                    checked={registerForm.isAdmin}
                    onChange={(event) =>
                      setRegisterForm((current) => ({
                        ...current,
                        isAdmin: event.target.checked,
                      }))
                    }
                  />
                  Definir este usuário como administrador
                </label>
              </>
            )}
            {authSuccess ? (
              <p className="status status-success" role="status">
                {authSuccess}
              </p>
            ) : null}
            {authError ? (
              <p className="status status-error" role="alert">
                {authError}
              </p>
            ) : null}
            <button className="primary" type="submit" disabled={busy}>
              {requiresInitialAdmin ? "Criar senha" : authMode === "login" ? "Entrar" : "Cadastrar"}
            </button>
            {requiresInitialAdmin ? null : (
              <button
                className="ghost"
                type="button"
                onClick={() => {
                  setAuthError("");
                  if (authMode === "login") {
                    setRegisterForm(emptyRegisterForm);
                    setAuthMode("register");
                    return;
                  }

                  setRegisterForm(emptyRegisterForm);
                  setAuthMode("login");
                }}
              >
                {authMode === "login" ? "Registrar" : "Voltar ao login"}
              </button>
            )}
          </form>
        </section>
      </main>
    );
  }

  return (
    <main className="shell app-shell">
      <button
        className="mobile-menu-button"
        type="button"
        aria-label={mobileMenuOpen ? "Fechar menu" : "Abrir menu"}
        aria-expanded={mobileMenuOpen}
        onClick={() => setMobileMenuOpen((current) => !current)}
      >
        <span />
        <span />
        <span />
      </button>
      <button
        className={`mobile-menu-backdrop ${mobileMenuOpen ? "open" : ""}`}
        type="button"
        aria-label="Fechar menu"
        onClick={() => setMobileMenuOpen(false)}
      />
      <aside className={`sidebar panel ${mobileMenuOpen ? "open" : ""}`}>
        <div>
          <p className="eyebrow">Controle Patrimonial</p>
          <div className="sidebar-brand">
            <img className="sidebar-logo" src="/18bbm.png" alt="Brasão do 18º BBM" />
            <h1>18º BBM</h1>
          </div>
          <p className="muted">
            {user.fullName}
            <br />
            {getUserRoleLabel(user.role)} · {user.militaryId}
          </p>
        </div>

        <nav className="side-nav" aria-label="Menu principal">
          {isAdmin ? (
            <button
              className={activeView === "items" ? "active" : ""}
              type="button"
              onClick={() => navigateTo("items")}
            >
              Cadastro
            </button>
          ) : null}
          <button
            className={activeView === "search" ? "active" : ""}
            type="button"
            onClick={() => navigateTo("search")}
          >
            Inventário
          </button>
          <button
            className={activeView === "notes" ? "active" : ""}
            type="button"
            onClick={() => navigateTo("notes")}
          >
            Anotações
          </button>
          <button
            className={activeView === "mural" ? "active" : ""}
            type="button"
            onClick={() => navigateTo("mural")}
          >
            Mural
          </button>
          <button
            className={activeView === "calendar" ? "active" : ""}
            type="button"
            onClick={() => navigateTo("calendar")}
          >
            Calendário
          </button>
          {isAdmin ? (
            <button
              className={activeView === "audit" ? "active" : ""}
              type="button"
              onClick={() => navigateTo("audit")}
            >
              Auditoria
            </button>
          ) : null}
        </nav>

        <button className="ghost sidebar-logout" type="button" onClick={handleLogout}>
          Sair
        </button>
      </aside>

      <section className="content-area">
        <header className="page-header">
          <div>
            <p className="eyebrow">Painel operacional</p>
            <h1>{VIEW_TITLES[activeView] ?? "Painel operacional"}</h1>
          </div>
          <div className="admin-settings" ref={adminSettingsRef}>
              <button
                className="icon-button"
                type="button"
                aria-label="Configurações administrativas"
                title="Configurações administrativas"
                aria-expanded={adminSettingsOpen}
                onClick={handleToggleAdminSettings}
              >
                <Icon type="settings" />
              </button>
              {adminSettingsOpen ? (
                <div className="admin-settings-panel">
                  <div className="admin-settings-panel-header">
                    <div>
                      <p className="eyebrow">Administração</p>
                      <h2>{editingManagedUser ? "Editar usuário" : "Usuários"}</h2>
                    </div>
                    {!editingManagedUser ? (
                      <button
                        className="icon-button"
                        type="button"
                        aria-label="Atualizar usuários"
                        title="Atualizar usuários"
                        onClick={() => loadAdminUsers()}
                        disabled={adminSettingsLoading}
                      >
                        <Icon type="refresh" />
                      </button>
                    ) : null}
                  </div>
                  {adminSettingsLoading ? (
                    <p className="muted">Carregando usuários...</p>
                  ) : editingManagedUser ? (
                    <article className="user-row">
                      <form className="managed-user-form" onSubmit={handleSaveManagedUser}>
                        <label>
                          Nome completo
                          <input
                            name="managed-full-name"
                            autoComplete="off"
                            value={managedUserForm.fullName}
                            onChange={(event) =>
                              setManagedUserForm((current) => ({ ...current, fullName: event.target.value }))
                            }
                            placeholder="Nome da pessoa"
                            required
                          />
                        </label>
                        <label>
                          Identificação funcional
                          <input
                            name="managed-employee-id"
                            autoComplete="off"
                            value={managedUserForm.militaryId}
                            onChange={(event) =>
                              setManagedUserForm((current) => ({ ...current, militaryId: event.target.value }))
                            }
                            placeholder="Matrícula ou identificação"
                            required
                          />
                        </label>
                        <label>
                          Usuário
                          <input
                            name="managed-username"
                            autoComplete="username"
                            autoCapitalize="none"
                            spellCheck={false}
                            pattern={usernamePattern}
                            title={
                              isManagedAdminLocked
                                ? "O nome de usuário do administrador principal não pode ser alterado."
                                : usernamePatternTitle
                            }
                            disabled={isManagedAdminLocked}
                            value={managedUserForm.username}
                            onChange={(event) =>
                              setManagedUserForm((current) => ({
                                ...current,
                                username: event.target.value,
                              }))
                            }
                            placeholder="Usuário de acesso"
                            required
                          />
                        </label>
                        {canEditManagedPassword ? (
                          <>
                            <label>
                              Nova senha
                              <input
                                type="password"
                                value={managedUserForm.password}
                                onChange={(event) =>
                                  setManagedUserForm((current) => ({
                                    ...current,
                                    password: event.target.value,
                                  }))
                                }
                                placeholder="Deixe em branco para manter"
                                autoComplete="new-password"
                              />
                            </label>
                            {requiresManagedCurrentPassword ? (
                              <label>
                                Senha atual
                                <input
                                  type="password"
                                  value={managedUserForm.currentPassword}
                                  onChange={(event) =>
                                    setManagedUserForm((current) => ({
                                      ...current,
                                      currentPassword: event.target.value,
                                    }))
                                  }
                                  placeholder="Confirme sua senha atual"
                                  autoComplete="current-password"
                                  required
                                />
                              </label>
                            ) : null}
                          </>
                        ) : null}
                        {canShowManagedAdminOption ? (
                          <>
                            <label
                              className="checkbox-field"
                              title={isManagedAdminLocked ? "O administrador principal não pode ser alterado." : undefined}
                            >
                              <input
                                type="checkbox"
                                checked={managedUserForm.isAdmin}
                                disabled={isManagedAdminLocked}
                                onChange={(event) =>
                                  setManagedUserForm((current) => ({
                                    ...current,
                                    isAdmin: event.target.checked,
                                    adminPassword: event.target.checked ? current.adminPassword : "",
                                  }))
                                }
                              />
                              Administrador
                            </label>
                            {requiresManagedAdminPassword ? (
                              <label>
                                Senha do administrador atual
                                <input
                                  type="password"
                                  value={managedUserForm.adminPassword}
                                  onChange={(event) =>
                                    setManagedUserForm((current) => ({
                                      ...current,
                                      adminPassword: event.target.value,
                                    }))
                                  }
                                  placeholder="Confirme a senha do administrador"
                                  autoComplete="current-password"
                                  required
                                />
                              </label>
                            ) : null}
                          </>
                        ) : null}
                        <div className="button-row">
                          <button className="primary" type="submit" disabled={busy}>
                            Salvar
                          </button>
                          <button className="ghost" type="button" onClick={handleCancelEditUser}>
                            Cancelar
                          </button>
                        </div>
                      </form>
                    </article>
                  ) : sortedAdminUsers.length ? (
                    <div className="user-list">
                      {sortedAdminUsers.map((managedUser) => (
                        <article className="user-row" key={managedUser.id}>
                          <div>
                            <strong>{managedUser.fullName}</strong>
                            <span>
                              {managedUser.username} · {getUserRoleLabel(managedUser.role)}
                            </span>
                          </div>
                          <div className="user-actions">
                            <button
                              className="icon-button"
                              type="button"
                              aria-label={`Editar ${managedUser.username}`}
                              title={`Editar ${managedUser.username}`}
                              disabled={busy}
                              onClick={() => handleStartEditUser(managedUser)}
                            >
                              <Icon type="edit" />
                            </button>
                            {isAdmin &&
                            managedUser.id !== user?.id &&
                            managedUser.username.toLowerCase() !== "admin" ? (
                              <button
                                className="danger-icon"
                                type="button"
                                aria-label={`Excluir ${managedUser.username}`}
                                title={`Excluir ${managedUser.username}`}
                                disabled={busy}
                                onClick={() => handleDeleteUser(managedUser)}
                              >
                                <Icon type="trash" />
                              </button>
                            ) : null}
                          </div>
                        </article>
                      ))}
                    </div>
                  ) : (
                    <p className="muted">Não há outros usuários cadastrados.</p>
                  )}
                </div>
              ) : null}
          </div>
        </header>

        {activeView === "items" ? (
          <article className="panel section-card">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Inventário</p>
              <h2>{isAdmin ? "Dados do item" : "Não é possível cadastrar itens"}</h2>
            </div>
            {isAdmin && !editingItemId ? (
              <div className="section-actions">
                <button
                  className="ghost"
                  type="button"
                  onClick={() => {
                    setEditingItemId(null);
                    setItemEditReturnView(null);
                    setItemForm(emptyItemForm);
                  }}
                >
                  Novo item
                </button>
              </div>
            ) : null}
          </div>

          {isAdmin ? (
            <form className="form-grid" onSubmit={handleSaveItem}>
              <label>
                Nome do item
                <input
                  value={itemForm.name}
                  onChange={(event) => setItemForm((current) => ({ ...current, name: event.target.value }))}
                  placeholder="Insira o nome do item"
                  maxLength={300}
                  required
                />
              </label>
              <label>
                Quantidade
                <NumberInput
                  value={itemForm.quantity}
                  min={1}
                  onValueChange={(value) => setItemForm((current) => ({ ...current, quantity: value }))}
                  required
                />
              </label>
              <label>
                Natureza
                <select
                  value={itemForm.nature}
                  onChange={(event) => setItemForm((current) => ({ ...current, nature: event.target.value }))}
                  required
                >
                  <option value="">Selecione</option>
                  {itemNatureOptions.map((nature) => (
                    <option key={nature} value={nature}>
                      {nature}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Tombo
                <input
                  type="text"
                  inputMode="numeric"
                  pattern="[0-9]*"
                  value={itemForm.assetTag}
                  onChange={(event) =>
                    setItemForm((current) => ({
                      ...current,
                      assetTag: event.target.value.replace(/\D/g, ""),
                    }))
                  }
                  placeholder="Informar apenas números"
                />
              </label>
              <label>
                Localização
                <div className="location-select-row">
                  <select
                    value={itemForm.location}
                    onChange={(event) => setItemForm((current) => ({ ...current, location: event.target.value }))}
                    required
                  >
                    <option value="">Selecione</option>
                    {availableLocationOptions.map((location) => (
                      <option key={location} value={location}>
                        {location}
                      </option>
                    ))}
                  </select>
                  <button
                    className="icon-button location-add-toggle"
                    type="button"
                    onClick={() =>
                      setNewLocationForm((current) => ({
                        ...current,
                        open: !current.open,
                        value: "",
                        editValue: "",
                        editName: "",
                        renaming: false,
                      }))
                    }
                    title={newLocationForm.open ? "Fechar gerenciamento de localizações" : "Adicionar localização"}
                    aria-label={newLocationForm.open ? "Fechar gerenciamento de localizações" : "Adicionar localização"}
                  >
                    <span>{newLocationForm.open ? "-" : "+"}</span>
                  </button>
                </div>
              </label>
              <label>
                Estado de conservação
                <select
                  value={itemForm.condition}
                  onChange={(event) => setItemForm((current) => ({ ...current, condition: event.target.value }))}
                  required
                >
                  <option value="">Selecione</option>
                  {itemConditionOptions.map((condition) => (
                    <option key={condition} value={condition}>
                      {condition}
                    </option>
                  ))}
                </select>
              </label>
              {newLocationForm.open ? (
                <div className="full-width location-add-row">
                  <div className="location-create-row">
                    <label>
                      Nova localização
                      <input
                        value={newLocationForm.value}
                        onChange={(event) =>
                          setNewLocationForm((current) => ({ ...current, value: event.target.value }))
                        }
                        placeholder="Digite a nova localização"
                      />
                    </label>
                    <button
                      className="primary"
                      type="button"
                      onClick={handleAddLocation}
                      disabled={newLocationForm.saving}
                    >
                      Adicionar
                    </button>
                  </div>
                  {customLocations.length ? (
                    <div className={`location-manage-row${newLocationForm.renaming ? " is-renaming" : ""}`}>
                      <label>
                        <span title="Localizações predefinidas não podem ser alteradas.">
                          Editar localizações adicionadas
                        </span>
                        <select
                          value={newLocationForm.editName}
                          onChange={(event) =>
                            setNewLocationForm((current) => ({
                              ...current,
                              editName: event.target.value,
                              editValue: event.target.value,
                              renaming: false,
                            }))
                          }
                        >
                          <option value="">Selecionar</option>
                          {customLocationOptions.map((location) => (
                            <option key={location} value={location}>
                              {location}
                            </option>
                          ))}
                        </select>
                      </label>
                      {newLocationForm.renaming ? (
                        <label>
                          Novo nome
                          <input
                            value={newLocationForm.editValue}
                            onChange={(event) =>
                              setNewLocationForm((current) => ({ ...current, editValue: event.target.value }))
                            }
                            placeholder="Digite o novo nome"
                          />
                        </label>
                      ) : null}
                      <button
                        className="ghost"
                        type="button"
                        onClick={handleRenameLocation}
                        disabled={newLocationForm.editing || !newLocationForm.editName}
                      >
                        {newLocationForm.renaming ? "Salvar" : "Renomear"}
                      </button>
                      <button
                        className="ghost danger"
                        type="button"
                        onClick={handleRemoveLocation}
                        disabled={newLocationForm.removing || !newLocationForm.editName}
                      >
                        Excluir
                      </button>
                    </div>
                  ) : null}
                </div>
              ) : null}
              <div className="full-width notes-photo-row">
                <div className={`photo-field${itemForm.hasPhoto ? " has-photo" : ""}`}>
                  <span>Foto do item</span>
                  <label
                    className="photo-dropzone"
                    title="Arraste ou selecione uma foto para adicionar ou substituir"
                    onDrop={handleItemPhotoDrop}
                    onDragOver={(event) => event.preventDefault()}
                  >
                    {itemFormPhotoPreviewUrl ? (
                      <img className="item-photo-preview" src={itemFormPhotoPreviewUrl} alt="Prévia da foto do item" />
                    ) : itemForm.hasPhoto ? (
                      <span>Prévia da foto do item</span>
                    ) : (
                      <span>Arraste ou selecione</span>
                    )}
                    <input ref={photoInputRef} type="file" accept="image/*" onChange={handleItemPhotoChange} />
                  </label>
                  {itemForm.hasPhoto ? (
                    <button className="ghost photo-remove-button" type="button" onClick={handleRemoveItemPhoto}>
                      Remover foto
                    </button>
                  ) : null}
                </div>
                <label>
                  Observações
                  <textarea
                    className="item-notes-textarea"
                    rows="2"
                    value={itemForm.notes}
                    onChange={(event) => setItemForm((current) => ({ ...current, notes: event.target.value }))}
                    placeholder="Informações adicionais sobre o item"
                    maxLength={5000}
                  />
                </label>
              </div>
              {editingItemId ? (
                <label className="checkbox-field full-width">
                  <input
                    type="checkbox"
                    checked={itemForm.isDischarged}
                    onChange={(event) =>
                      setItemForm((current) => ({ ...current, isDischarged: event.target.checked }))
                    }
                  />
                  Item descargueado
                </label>
              ) : null}
              <div className="button-row">
                <button className="primary" type="submit" disabled={busy}>
                  {editingItemId ? "Salvar alterações" : "Cadastrar item"}
                </button>
                {editingItemId ? (
                  <button
                    className="ghost"
                    type="button"
                    onClick={handleCancelItemEdit}
                  >
                    Cancelar edição
                  </button>
                ) : null}
              </div>
            </form>
          ) : (
            <p className="muted wide-text">
              O usuário logado pode consultar o inventário e registrar transferências, mas não alterar cadastro de itens.
            </p>
          )}
          </article>
        ) : null}

        {activeView === "search" ? (
          <div className="content-stack">
            <article className="panel section-card">
              <div className="section-heading">
                <div>
                  <p className="eyebrow">Inventário</p>
                  <h2>Consulta e ações</h2>
                </div>
                <div className="section-actions">
                  <button
                    className="icon-button"
                    type="button"
                    onClick={() => refreshData().catch((error) => handleRequestError(error))}
                    disabled={busy}
                    title="Atualizar inventário"
                    aria-label="Atualizar inventário"
                  >
                    <Icon type="refresh" />
                  </button>
                  <div className="column-picker" ref={columnsMenuRef}>
                    <button
                      ref={columnsButtonRef}
                      className="icon-button"
                      type="button"
                      onClick={() => {
                        if (!columnsMenuOpen && columnsButtonRef.current) {
                          const rect = columnsButtonRef.current.getBoundingClientRect();
                          setColumnsMenuPos({
                            top: rect.bottom + 8,
                            right: window.innerWidth - rect.right,
                          });
                        }
                        setColumnsMenuOpen((o) => !o);
                      }}
                      title="Configurar colunas visíveis"
                      aria-label="Configurar colunas visíveis"
                      aria-expanded={columnsMenuOpen}
                    >
                      <Icon type="columns" />
                    </button>
                    {columnsMenuOpen
                      ? createPortal(
                          <div
                            className="column-picker-menu"
                            ref={columnsPortalRef}
                            style={{ top: columnsMenuPos.top, right: columnsMenuPos.right }}
                          >
                            {INVENTORY_COLUMNS.map((c) => (
                              <label key={c.key} className="checkbox-field">
                                <input
                                  type="checkbox"
                                  checked={visibleColumns.includes(c.key)}
                                  onChange={() => toggleColumn(c.key)}
                                />
                                {c.label}
                              </label>
                            ))}
                          </div>,
                          document.body,
                        )
                      : null}
                  </div>
                  <button className="ghost" type="button" onClick={handleExportInventoryPdf}>
                    Exportar PDF
                  </button>
                  {hasInventoryFilters ? (
                    <button
                      className="ghost"
                      type="button"
                      onClick={() => {
                        setInventoryFilters(emptyInventoryFilters);
                        setInventoryPage(1);
                      }}
                    >
                      Limpar filtro
                    </button>
                  ) : null}
                </div>
              </div>
              <div className="filter-grid">
                <label>
                  Nome
                  <input
                    value={inventoryFilters.name}
                    onChange={(event) => {
                      setInventoryPage(1);
                      setInventoryFilters((current) => ({ ...current, name: event.target.value }));
                    }}
                    placeholder="Filtrar por nome"
                  />
                </label>
                <label>
                  Tombo
                  <input
                    inputMode="numeric"
                    pattern="[0-9]*"
                    value={inventoryFilters.assetTag}
                    onChange={(event) => {
                      setInventoryPage(1);
                      setInventoryFilters((current) => ({
                        ...current,
                        assetTag: event.target.value.replace(/\D/g, ""),
                      }));
                    }}
                    placeholder="Filtrar por número"
                  />
                </label>
                <label>
                  Conservação
                  <select
                    value={inventoryFilters.condition}
                    onChange={(event) => {
                      setInventoryPage(1);
                      setInventoryFilters((current) => ({ ...current, condition: event.target.value }));
                    }}
                  >
                    <option value="">Todos</option>
                    {itemConditionOptions.map((condition) => (
                      <option key={condition} value={condition}>
                        {condition}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Natureza
                  <select
                    value={inventoryFilters.nature}
                    onChange={(event) => {
                      setInventoryPage(1);
                      setInventoryFilters((current) => ({ ...current, nature: event.target.value }));
                    }}
                  >
                    <option value="">Todas</option>
                    {itemNatureOptions.map((nature) => (
                      <option key={nature} value={nature}>
                        {nature}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Localização
                  <select
                    value={inventoryFilters.location}
                    onChange={(event) => {
                      setInventoryPage(1);
                      setInventoryFilters((current) => ({ ...current, location: event.target.value }));
                    }}
                  >
                    <option value="">Todas</option>
                    {availableLocationOptions.map((location) => (
                      <option key={location} value={location}>
                        {location}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Detentor
                  <input
                    value={inventoryFilters.responsible}
                    onChange={(event) => {
                      setInventoryPage(1);
                      setInventoryFilters((current) => ({ ...current, responsible: event.target.value }));
                    }}
                    placeholder="Filtrar por detentor do item"
                  />
                </label>
              </div>
          <div className="table-wrap inventory-table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Item</th>
                  {visibleColumns.includes("quantity") && <th title="Quantidade">Qtd.</th>}
                  {visibleColumns.includes("assetTag") && <th>Tombo</th>}
                  {visibleColumns.includes("nature") && <th>Natureza</th>}
                  {visibleColumns.includes("location") && <th>Localização</th>}
                  {visibleColumns.includes("condition") && <th>Conservação</th>}
                  {visibleColumns.includes("responsible") && <th>Detentor</th>}
                  {visibleColumns.includes("discharged") && (
                    <th className="center-column" title="Descargueado">
                      Desc.
                    </th>
                  )}
                  {visibleColumns.includes("photo") && <th className="center-column">Foto</th>}
                  <th>Ações</th>
                </tr>
              </thead>
              <tbody>
                {paginatedInventoryItems.length === 0 ? (
                  <tr>
                    <td className="table-empty" colSpan={visibleColumns.length + 2}>
                      {hasInventoryFilters
                        ? "Nenhum item encontrado para os filtros aplicados."
                        : "Nenhum item cadastrado no inventário."}
                    </td>
                  </tr>
                ) : null}
                {paginatedInventoryItems.map((item) => (
                  <tr key={item.id} className={focusedItemId === item.id && itemAction ? "active-row" : ""}>
                    <td className="item-name-cell">{item.name}</td>
                    {visibleColumns.includes("quantity") && <td>{item.quantity}</td>}
                    {visibleColumns.includes("assetTag") && <td>{item.assetTag?.trim() || "-"}</td>}
                    {visibleColumns.includes("nature") && <td>{item.nature}</td>}
                    {visibleColumns.includes("location") && <td>{item.location}</td>}
                    {visibleColumns.includes("condition") && <td>{item.condition}</td>}
                    {visibleColumns.includes("responsible") && (
                      <td>{item.responsiblePerson || ""}</td>
                    )}
                    {visibleColumns.includes("discharged") && (
                      <td className="center-column">
                        {item.isDischarged ? (
                          <span title={formatDateTime(item.dischargedAt)}>
                            {formatDate(item.dischargedAt)}
                          </span>
                        ) : (
                          "-"
                        )}
                      </td>
                    )}
                    {visibleColumns.includes("photo") && (
                      <td className="center-column">
                        {getItemPhotoUrl(item) ? (
                          <button
                            className="photo-thumb-button"
                            type="button"
                            title="Clique para exibir"
                            aria-label={`Exibir foto de ${item.name}`}
                            onClick={() => setPhotoViewer({ src: getItemPhotoUrl(item), alt: `Foto de ${item.name}` })}
                          >
                            <img className="photo-thumb" src={getItemPhotoUrl(item)} alt="" />
                          </button>
                        ) : (
                          "-"
                        )}
                      </td>
                    )}
                    <td>
                      <div className="row-actions">
                        <button
                          type="button"
                          className="icon-button"
                          onClick={() => handleSelectItemNotes(item.id)}
                          title="Ver observações"
                          aria-label="Ver observações"
                        >
                          <Icon type="eye" />
                        </button>
                        <button
                          type="button"
                          className="icon-button"
                          onClick={() => handleSelectTransferItem(item)}
                          title="Transferir"
                          aria-label="Transferir"
                        >
                          <Icon type="transfer" />
                        </button>
                        <button
                          type="button"
                          className="icon-button"
                          onClick={() => handleSelectConditionItem(item)}
                          title="Alterar conservação"
                          aria-label="Alterar conservação"
                        >
                          <Icon type="condition" />
                        </button>
                        <button
                          type="button"
                          className="icon-button"
                          onClick={() => handleSelectHistoryItem(item.id)}
                          title="Histórico individual"
                          aria-label="Histórico individual"
                        >
                          <Icon type="history" />
                        </button>
                        {isAdmin ? (
                          <>
                            <button
                              type="button"
                              className="icon-button"
                              onClick={() => handleEditItem(item)}
                              title="Editar"
                              aria-label="Editar"
                            >
                              <Icon type="edit" />
                            </button>
                            <button
                              type="button"
                              className="icon-button danger"
                              onClick={() => handleDeleteItem(item.id)}
                              title="Excluir"
                              aria-label="Excluir"
                            >
                              <Icon type="trash" />
                            </button>
                          </>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {filteredItems.length > inventoryPageSize ? (
            <div className="pagination">
              <label className="pagination-picker">
                Página
                <span className="pagination-number">
                  <NumberInput
                    value={String(safeInventoryPage)}
                    min={1}
                    max={inventoryTotalPages}
                    onValueChange={(value) => {
                      const pageNumber = Number(value);
                      if (Number.isFinite(pageNumber) && pageNumber >= 1) {
                        setInventoryPagePreservingBottom(Math.min(inventoryTotalPages, pageNumber));
                      }
                    }}
                  />
                </span>
                de {inventoryTotalPages}
              </label>
            </div>
          ) : null}
            </article>
          </div>
        ) : null}

        {activeView === "notes" ? (
          <article className="panel section-card">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Pessoal</p>
              <h2>Registro de anotações</h2>
            </div>
          </div>

          <form className="stacked" onSubmit={handleSaveNote}>
            <input
              value={noteForm.title}
              maxLength={200}
              onChange={(event) => setNoteForm((current) => ({ ...current, title: event.target.value }))}
              placeholder="Título da anotação"
              required
            />
            <textarea
              rows="9"
              value={noteForm.content}
              maxLength={10000}
              onChange={(event) => setNoteForm((current) => ({ ...current, content: event.target.value }))}
              placeholder="Conteúdo da anotação..."
              required
            />
            <input
              value={noteForm.tags}
              maxLength={500}
              onChange={(event) => setNoteForm((current) => ({ ...current, tags: event.target.value }))}
              placeholder="Etiquetas separadas por vírgula"
            />
            <label className="checkbox-field">
              <input
                type="checkbox"
                checked={noteForm.isPublic}
                onChange={(event) => setNoteForm((current) => ({ ...current, isPublic: event.target.checked }))}
              />
              Disponibilizar no mural para todos os usuários
            </label>
            <div className="button-row">
              <button className="primary" type="submit" disabled={busy}>
                {editingNoteId ? "Salvar anotação" : "Adicionar anotação"}
              </button>
              {editingNoteId ? (
                <button
                  className="ghost"
                  type="button"
                  onClick={() => {
                    setEditingNoteId(null);
                    setNoteForm(emptyNoteForm);
                    if (noteEditReturnView) {
                      setActiveView(noteEditReturnView);
                    }
                    setNoteEditReturnView(null);
                  }}
                >
                  Cancelar edição
                </button>
              ) : null}
            </div>
          </form>

          <div className="card-grid">
            {notes.map((noteItem) => (
              <article className="content-card" key={noteItem.id}>
                <div className="card-header">
                  <div>
                    <h3>{noteItem.title}</h3>
                    <small>
                      {formatDateTime(noteItem.updatedAt)}
                      {noteItem.isPublic ? " · Publicada no mural" : ""}
                    </small>
                  </div>
                  <div className="row-actions">
                    <button
                      type="button"
                      className="icon-button"
                      onClick={() => handleEditNote(noteItem)}
                      title="Editar"
                      aria-label="Editar"
                    >
                      <Icon type="edit" />
                    </button>
                    <button
                      type="button"
                      className="icon-button danger"
                      onClick={() => handleDeleteNote(noteItem.id)}
                      title="Excluir"
                      aria-label="Excluir"
                    >
                      <Icon type="trash" />
                    </button>
                  </div>
                </div>
                <p>{noteItem.content}</p>
                <TagList tags={noteItem.tags} />
              </article>
            ))}
          </div>
          </article>
        ) : null}

        {activeView === "mural" ? (
          <article className="panel section-card">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Compartilhado</p>
                <h2>Mural de anotações</h2>
              </div>
              <div className="section-actions">
                <button
                  className="icon-button"
                  type="button"
                  aria-label="Atualizar mural"
                  title="Atualizar mural"
                  onClick={() => loadMural().catch((error) => handleRequestError(error))}
                >
                  <Icon type="refresh" />
                </button>
              </div>
            </div>

            {muralNotes.length === 0 ? (
              <p className="muted wide-text">
                Nenhuma anotação publicada no mural.
                <br />
                Para publicar, marque a opção "Disponibilizar no mural" ao salvar uma anotação.
              </p>
            ) : (
              <div className="card-grid">
                {muralNotes.map((muralNote) => (
                  <article className="content-card" key={muralNote.id}>
                    <div className="card-header">
                      <div>
                        <h3>{muralNote.title}</h3>
                        <small>
                          {muralNote.authorName} · {formatDateTime(muralNote.updatedAt)}
                        </small>
                      </div>
                      {muralNote.authorUserId === user.id ? (
                        <div className="row-actions">
                          <button
                            type="button"
                            className="icon-button"
                            onClick={() => handleEditMuralNote(muralNote)}
                            title="Editar"
                            aria-label="Editar"
                          >
                            <Icon type="edit" />
                          </button>
                        </div>
                      ) : null}
                    </div>
                    <p>{muralNote.content}</p>
                    <TagList tags={muralNote.tags} />
                  </article>
                ))}
              </div>
            )}
          </article>
        ) : null}

        {activeView === "calendar" ? (
          <article className="panel section-card">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Agenda</p>
                <h2>Prazos e processos</h2>
              </div>
              <div className="section-actions">
                <button
                  className="icon-button"
                  type="button"
                  aria-label="Atualizar calendário"
                  title="Atualizar calendário"
                  onClick={() => loadCalendar().catch((error) => handleRequestError(error))}
                >
                  <Icon type="refresh" />
                </button>
                <button className="primary" type="button" onClick={() => openCalendarCreate()}>
                  Adicionar anotação
                </button>
              </div>
            </div>

            <div className="filter-grid">
              <label>
                Número do SEI
                <input
                  value={calendarSearch.seiNumber}
                  onChange={(event) =>
                    setCalendarSearch((current) => ({ ...current, seiNumber: event.target.value }))
                  }
                  placeholder="Pesquisar por número do SEI"
                />
              </label>
              <label>
                Período de prazo
                <select
                  value={calendarSearch.window}
                  onChange={(event) =>
                    setCalendarSearch((current) => ({ ...current, window: event.target.value }))
                  }
                >
                  {calendarSearchWindowOptions.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </label>
              <label className="full-width">
                Assunto
                <input
                  value={calendarSearch.subject}
                  onChange={(event) =>
                    setCalendarSearch((current) => ({ ...current, subject: event.target.value }))
                  }
                  placeholder="Pesquisar por assunto"
                />
              </label>
            </div>

            {calendarSearchActive ? (
              <>
                <div className="section-heading calendar-results-heading">
                  <div>
                    <p className="eyebrow">Resultado da pesquisa</p>
                    <h2>
                      {calendarSearchResults.length === 0
                        ? "Nenhuma anotação"
                        : calendarSearchResults.length === 1
                          ? "1 anotação"
                          : `${calendarSearchResults.length} anotações`}
                    </h2>
                  </div>
                  <div className="section-actions">
                    <button
                      className="ghost"
                      type="button"
                      onClick={() => setCalendarSearch(emptyCalendarSearch)}
                    >
                      Limpar pesquisa
                    </button>
                  </div>
                </div>
                {calendarSearchResults.length === 0 ? (
                  <p className="muted">Nenhuma anotação encontrada para a pesquisa.</p>
                ) : (
                  <div className="card-grid">
                    {calendarSearchResults.map((entry) => (
                      <article className="content-card" key={entry.id}>
                        <div className="card-header">
                          <div>
                            <h3>{entry.subject}</h3>
                            <small>
                              Prazo: {formatIsoDateBr(entry.dueDate)}
                              {entry.seiNumber?.trim() ? ` · SEI ${entry.seiNumber}` : ""}
                            </small>
                          </div>
                          <div className="row-actions">
                            <button
                              type="button"
                              className="icon-button"
                              onClick={() => setCalendarDetailEntryId(entry.id)}
                              title="Ver detalhes"
                              aria-label="Ver detalhes"
                            >
                              <Icon type="eye" />
                            </button>
                          </div>
                        </div>
                      </article>
                    ))}
                  </div>
                )}
              </>
            ) : (
              <>
                <div className="section-divider" />
                <div className="calendar-toolbar">
                  <button
                    className="ghost"
                    type="button"
                    onClick={() => changeCalendarMonth(-1)}
                    aria-label="Mês anterior"
                  >
                    ‹
                  </button>
                  <select
                    aria-label="Mês"
                    value={calendarMonth.month}
                    onChange={(event) =>
                      setCalendarMonth((current) => ({ ...current, month: Number(event.target.value) }))
                    }
                  >
                    {calendarMonthNames.map((label, index) => (
                      <option key={label} value={index}>
                        {label}
                      </option>
                    ))}
                  </select>
                  <select
                    aria-label="Ano"
                    value={calendarMonth.year}
                    onChange={(event) =>
                      setCalendarMonth((current) => ({ ...current, year: Number(event.target.value) }))
                    }
                  >
                    {calendarYearOptions.map((year) => (
                      <option key={year} value={year}>
                        {year}
                      </option>
                    ))}
                  </select>
                  <button
                    className="ghost"
                    type="button"
                    onClick={() => changeCalendarMonth(1)}
                    aria-label="Próximo mês"
                  >
                    ›
                  </button>
                  <button className="ghost" type="button" onClick={goToCalendarToday}>
                    Hoje
                  </button>
                </div>

                <div className="calendar-grid">
                  {calendarWeekDayLabels.map((label) => (
                    <span className="calendar-weekday" key={label}>
                      {label}
                    </span>
                  ))}
                  {calendarDays.map((day) => {
                    const dayEntries = day.inMonth ? (calendarEntriesByDate.get(day.iso) ?? []) : [];
                    const classes = ["calendar-day"];
                    if (!day.inMonth) classes.push("outside");
                    if (day.iso === todayIsoDate) classes.push("today");
                    if (dayEntries.length > 0) {
                      classes.push("has-entries");
                      const daysUntilDue = daysUntilIsoDate(day.iso);
                      if (daysUntilDue < 0) classes.push("overdue");
                      else if (daysUntilDue < calendarUrgentWindowDays) classes.push("urgent");
                    }
                    if (day.iso === selectedCalendarDate) classes.push("selected");
                    if (calendarDayModalOpen && day.iso === selectedCalendarDate) classes.push("active");

                    return (
                      <button
                        key={day.iso}
                        type="button"
                        className={classes.join(" ")}
                        disabled={!day.inMonth}
                        onClick={() => openCalendarDayModal(day.iso)}
                      >
                        <span className="calendar-day-number">{day.dayNumber}</span>
                        {dayEntries.length > 0 ? (
                          <span className="calendar-day-count">{dayEntries.length}</span>
                        ) : null}
                      </button>
                    );
                  })}
                </div>

              </>
            )}
          </article>
        ) : null}

        {activeView === "audit" && isAdmin ? (
          <article className="panel section-card">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Auditoria</p>
                <h2>Timeline administrativa</h2>
              </div>
              <div className="section-actions">
                <button
                  className="icon-button"
                  type="button"
                  aria-label="Atualizar auditoria"
                  title="Atualizar auditoria"
                  disabled={auditLoading}
                  onClick={() => loadAuditTimeline(token, auditPeriod, auditCustomRange)}
                >
                  <Icon type="refresh" />
                </button>
                <button className="ghost" type="button" onClick={handleExportAuditPdf}>
                  Exportar PDF
                </button>
              </div>
            </div>

            <form className="filter-grid audit-filter" onSubmit={handleAuditFilterSubmit}>
              <label>
                Período
                <select
                  value={auditPeriod}
                  onChange={handleAuditPeriodChange}
                  title={`Máximo de ${auditTimelineLimit.toLocaleString("pt-BR")} registros por consulta.`}
                >
                  {auditPeriodOptions.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </label>
              {auditPeriod === "custom" ? (
                <>
                  <button className="primary" type="submit" disabled={auditLoading}>
                    Filtrar
                  </button>
                  <label className="audit-date-field">
                    Início
                    <input
                      type="date"
                      value={auditCustomRange.startDate}
                      onChange={(event) =>
                        setAuditCustomRange((current) => ({ ...current, startDate: event.target.value }))
                      }
                    />
                  </label>
                  <label className="audit-date-field">
                    Fim
                    <input
                      type="date"
                      value={auditCustomRange.endDate}
                      onChange={(event) =>
                        setAuditCustomRange((current) => ({ ...current, endDate: event.target.value }))
                      }
                    />
                  </label>
                </>
              ) : null}
            </form>

            <div className="timeline">
              {auditLoading ? <p className="muted">Carregando registros...</p> : null}
              {!auditLoading && !auditTimeline.length ? (
                <p className="muted">Nenhum registro de auditoria encontrado no período selecionado.</p>
              ) : null}
              {!auditLoading && paginatedAuditTimeline.map((entry) => (
                <article key={entry.id} className="timeline-item">
                  <span className="timeline-dot admin" title="Registro de auditoria" />
                  <div>
                    <strong>
                      {entry.action} · {entry.entityType}
                    </strong>
                    <p>{entry.summary}</p>
                    <small>
                      {entry.actorUserName} · {formatDateTime(entry.timestamp)} · {entry.details}
                    </small>
                  </div>
                </article>
              ))}
            </div>
            {auditTimeline.length > auditPageSize ? (
              <div className="pagination">
                <label className="pagination-picker">
                  Página
                  <span className="pagination-number">
                    <NumberInput
                      value={String(safeAuditPage)}
                      min={1}
                      max={auditTotalPages}
                      onValueChange={(value) => {
                        const pageNumber = Number(value);
                        if (Number.isFinite(pageNumber) && pageNumber >= 1) {
                          setAuditPagePreservingBottom(Math.min(auditTotalPages, pageNumber));
                        }
                      }}
                    />
                  </span>
                  de {auditTotalPages}
                </label>
              </div>
            ) : null}
          </article>
        ) : null}
      </section>
      {itemAction && selectedItemIsOnInventoryPage ? (
        <div
          className="action-modal"
          role="dialog"
          aria-modal="true"
          aria-label="Ação do item"
          onMouseDown={closeItemAction}
        >
          <article className="panel section-card action-modal-card" onMouseDown={(event) => event.stopPropagation()}>
            <button
              className="photo-viewer-close action-modal-close"
              type="button"
              onClick={closeItemAction}
              aria-label="Fechar ação"
            >
              ×
            </button>

            {itemAction === "history" ? (
              <>
                <div className="section-heading">
                  <div>
                    <p className="eyebrow">Item selecionado</p>
                    <h2>Histórico individual do item</h2>
                  </div>
                </div>

                {selectedItem ? (
                  <>
                    <div className="detail-summary">
                      <div>
                        <strong>{selectedItem.name}</strong>
                      </div>
                      <span>
                        {selectedItem.assetTag?.trim()
                          ? `${selectedItem.assetTag.trim()} · ${selectedItem.location}`
                          : selectedItem.location}
                      </span>
                    </div>

                    <div className="timeline">
                      {itemHistory.map((entry) => (
                        <article key={`${entry.kind}-${entry.id}`} className="timeline-item">
                          <span
                            className={`timeline-dot${entry.kind === "audit" ? " timeline-dot-audit" : ""}`}
                            title={entry.kind === "audit" ? "Registro de auditoria" : "Transferência"}
                          />
                          <div>
                            {entry.kind === "movement" ? (
                              <>
                                <strong>{formatMovementTitle(entry)} · {entry.quantity} unidade(s)</strong>
                                {entry.originPerson?.trim() && !isReturnToLocationMovement(entry) ? (
                                  <p>Responsável: {entry.originPerson.trim()}</p>
                                ) : null}
                                {(() => { const route = formatMovementRoute(entry); return route ? <p>{route}</p> : null; })()}
                                {formatMovementConditionChange(entry) ? (
                                  <p>Conservação: {formatMovementConditionChange(entry)}</p>
                                ) : null}
                                {formatMovementDischargeChange(entry) ? (
                                  <p>Descargueado: {formatMovementDischargeChange(entry)}</p>
                                ) : null}
                                {entry.notes ? <p className="muted">{entry.notes}</p> : null}
                              </>
                            ) : (
                              <>
                                <strong>{entry.action}</strong>
                                {entry.details ? <p className="timeline-audit-details">{entry.details}</p> : null}
                              </>
                            )}
                            <small>
                              {formatDateTime(entry.createdAt)} · {entry.performedByUserName}
                            </small>
                          </div>
                        </article>
                      ))}
                    </div>
                  </>
                ) : (
                  <p className="muted">Cadastre ou selecione um item para ver o histórico.</p>
                )}
              </>
            ) : null}

            {itemAction === "notes" ? (
              <>
                <div className="section-heading">
                  <div>
                    <p className="eyebrow">Item selecionado</p>
                    <h2>Observações do item</h2>
                  </div>
                </div>

                {selectedItem ? (
                  <div className="detail-summary">
                    <div>
                      <strong>{selectedItem.name}</strong>
                    </div>
                    <p className="item-notes-display">
                      {selectedItem.notes?.trim() || "Este item não possui observações."}
                    </p>
                  </div>
                ) : (
                  <p className="muted">Cadastre ou selecione um item para ver as observações.</p>
                )}
              </>
            ) : null}

            {itemAction === "transfer" ? (
              <>
                <div className="section-heading">
                  <div>
                    <p className="eyebrow">Item selecionado</p>
                    <h2>Transferência de material</h2>
                  </div>
                </div>

                <form className="form-grid" onSubmit={handleTransfer}>
                  <label>
                    Quantidade disponível
                    <input value={selectedTransferMaxQuantity} disabled readOnly />
                  </label>
                  <label>
                    Quantidade a transferir
                    <NumberInput
                      value={transferForm.quantity}
                      min={1}
                      max={selectedTransferMaxQuantity}
                      disabled={!selectedTransferItem || selectedTransferMaxQuantity <= 0}
                      onValueChange={(value) => setTransferForm((current) => ({ ...current, quantity: value }))}
                      required
                    />
                  </label>
                  <label>
                    Origem
                    <input value={selectedTransferItem?.location ?? ""} disabled readOnly />
                  </label>
                  <label>
                    Tipo de destino
                    <select
                      value={transferForm.destinationType}
                      disabled={!selectedTransferItem}
                      onChange={(event) =>
                        setTransferForm((current) => ({
                          ...current,
                          destinationType: event.target.value,
                          toLocation: event.target.value === "Local" ? current.fromLocation : "",
                        }))
                      }
                    >
                      <option value="Local">Local físico</option>
                      <option value="Pessoa">Militar responsável</option>
                    </select>
                  </label>
                  {transferForm.destinationType === "Local" ? (
                    <label className="full-width">
                      Destino físico
                      <select
                        value={transferForm.toLocation}
                        disabled={!selectedTransferItem}
                        onChange={(event) => setTransferForm((current) => ({ ...current, toLocation: event.target.value }))}
                        required
                      >
                        <option value="">Selecione</option>
                        {availableLocationOptions.map((location) => (
                          <option key={location} value={location}>
                            {location}
                          </option>
                        ))}
                      </select>
                    </label>
                  ) : (
                    <label className="full-width">
                      Militar responsável pelo recebimento
                      <input
                        value={transferForm.destinationPerson}
                        disabled={!selectedTransferItem}
                        onChange={(event) =>
                          setTransferForm((current) => ({ ...current, destinationPerson: event.target.value }))
                        }
                        placeholder="Nome ou matrícula"
                        maxLength={200}
                      />
                    </label>
                  )}
                  <div className="button-row">
                    <button className="primary full-width" type="submit" disabled={busy}>
                      Registrar transferência
                    </button>
                  </div>
                </form>
              </>
            ) : null}

            {itemAction === "condition" ? (
              <>
                <div className="section-heading">
                  <div>
                    <p className="eyebrow">Item selecionado</p>
                    <h2>Alterar conservação</h2>
                  </div>
                </div>

                <form className="form-grid" onSubmit={handleUpdateItemCondition}>
                  <label>
                    Item
                    <input value={selectedItem?.name ?? ""} disabled readOnly />
                  </label>
                  <label>
                    Conservação atual
                    <input value={selectedItem?.condition ?? ""} disabled readOnly />
                  </label>
                  <label className="full-width">
                    Nova conservação
                    <select
                      value={conditionForm.condition}
                      disabled={!selectedItem}
                      onChange={(event) => setConditionForm({ condition: event.target.value })}
                      required
                    >
                      <option value="">Selecione</option>
                      {itemConditionOptions.map((condition) => (
                        <option key={condition} value={condition}>
                          {condition}
                        </option>
                      ))}
                    </select>
                  </label>
                  <div className="button-row">
                    <button className="primary full-width" type="submit" disabled={busy}>
                      Salvar conservação
                    </button>
                  </div>
                </form>
              </>
            ) : null}
          </article>
        </div>
      ) : null}
      {toasts.length ? (
        <div className="toast-stack" role="status" aria-live="polite">
          {toasts.map((toast) => (
            <div className={`notice toast-message ${toast.tone}`} key={toast.id}>
              {toast.text}
            </div>
          ))}
        </div>
      ) : null}
      {calendarDayModalOpen ? (
        <div
          className="action-modal"
          role="dialog"
          aria-modal="true"
          aria-label="Anotações do dia"
          onMouseDown={closeCalendarDayModal}
        >
          <article
            className="panel section-card action-modal-card calendar-day-modal"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <button
              className="photo-viewer-close action-modal-close"
              type="button"
              onClick={closeCalendarDayModal}
              aria-label="Fechar anotações do dia"
            >
              ×
            </button>
            {calendarDetailEntry ? (
              <div className="calendar-detail-view">
                <button
                  className="icon-button calendar-back-button"
                  type="button"
                  onClick={() => setCalendarDetailEntryId(null)}
                  aria-label="Voltar para as anotações do dia"
                  title="Voltar"
                >
                  ‹
                </button>
                <div className="calendar-detail-content">
                  <div className="section-heading">
                    <div>
                      <h2>{calendarDetailEntry.subject}</h2>
                      <p className="eyebrow">{formatIsoDateBr(calendarDetailEntry.dueDate)}</p>
                    </div>
                  </div>
                  {renderCalendarDetailBody(calendarDetailEntry, true)}
                </div>
              </div>
            ) : (
              <>
                <div className="section-heading">
                  <div className="calendar-day-heading">
                    <h2>Anotações do dia</h2>
                    <p className="eyebrow">{formatIsoDateBr(selectedCalendarDate)}</p>
                    <p className="muted calendar-day-total">
                      {selectedCalendarDayEntries.length === 0
                        ? "Nenhuma anotação para este dia"
                        : selectedCalendarDayEntries.length === 1
                          ? "1 anotação"
                          : `${selectedCalendarDayEntries.length} anotações`}
                    </p>
                  </div>
                </div>
                {selectedCalendarDayEntries.length > 0 ? (
                  <ul className="calendar-subject-list">
                    {selectedCalendarDayEntries.map((entry) => (
                      <li key={entry.id}>
                        <button type="button" onClick={() => setCalendarDetailEntryId(entry.id)}>
                          <span className="calendar-subject-name">{entry.subject}</span>
                          <small>
                            {entry.seiNumber?.trim() ? `SEI: ${entry.seiNumber}` : "SEI: N/A"}
                          </small>
                          {daysUntilIsoDate(entry.dueDate) < 0 ? (
                            <span className="calendar-subject-badge overdue">Vencido</span>
                          ) : isUrgentIsoDate(entry.dueDate) ? (
                            <span className="calendar-subject-badge">Prazo próximo</span>
                          ) : null}
                        </button>
                      </li>
                    ))}
                  </ul>
                ) : null}
                <div className="button-row">
                  <button
                    className="primary"
                    type="button"
                    title="Adicionar neste dia"
                    onClick={() => openCalendarCreate(selectedCalendarDate, true)}
                  >
                    Adicionar anotação
                  </button>
                </div>
              </>
            )}
          </article>
        </div>
      ) : null}
      {calendarDetailEntry && !calendarDayModalOpen ? (
        <div
          className="action-modal"
          role="dialog"
          aria-modal="true"
          aria-label="Detalhes da anotação do calendário"
          onMouseDown={() => setCalendarDetailEntryId(null)}
        >
          <article className="panel section-card action-modal-card" onMouseDown={(event) => event.stopPropagation()}>
            <button
              className="photo-viewer-close action-modal-close"
              type="button"
              onClick={() => setCalendarDetailEntryId(null)}
              aria-label="Fechar detalhes"
            >
              ×
            </button>
            <div className="section-heading">
              <div>
                <p className="eyebrow">Calendário</p>
                <h2>{calendarDetailEntry.subject}</h2>
              </div>
            </div>
            {renderCalendarDetailBody(calendarDetailEntry)}
          </article>
        </div>
      ) : null}
      {calendarFormOpen ? (
        <div
          className="action-modal"
          role="dialog"
          aria-modal="true"
          aria-label="Anotação do calendário"
          onMouseDown={closeCalendarForm}
        >
          <article className="panel section-card action-modal-card" onMouseDown={(event) => event.stopPropagation()}>
            <button
              className="photo-viewer-close action-modal-close"
              type="button"
              onClick={closeCalendarForm}
              aria-label="Fechar formulário"
            >
              ×
            </button>
            <div className="section-heading">
              <div>
                <p className="eyebrow">Calendário</p>
                <h2>{editingCalendarEntryId ? "Editar anotação" : "Nova anotação"}</h2>
              </div>
            </div>
            <form className="form-grid" onSubmit={handleSaveCalendarEntry}>
              <label>
                Data do prazo
                <input
                  type="date"
                  value={calendarForm.dueDate}
                  onChange={(event) =>
                    setCalendarForm((current) => ({ ...current, dueDate: event.target.value }))
                  }
                  required
                />
              </label>
              <label>
                Número do SEI
                <input
                  value={calendarForm.seiNumber}
                  maxLength={50}
                  onChange={(event) =>
                    setCalendarForm((current) => ({ ...current, seiNumber: event.target.value }))
                  }
                  placeholder="Número do processo no SEI"
                />
              </label>
              <label className="full-width">
                Assunto
                <input
                  value={calendarForm.subject}
                  maxLength={200}
                  onChange={(event) =>
                    setCalendarForm((current) => ({ ...current, subject: event.target.value }))
                  }
                  placeholder="Assunto do processo ou ofício"
                  required
                />
              </label>
              <label className="full-width">
                Observações
                <textarea
                  rows="5"
                  value={calendarForm.notes}
                  maxLength={5000}
                  onChange={(event) =>
                    setCalendarForm((current) => ({ ...current, notes: event.target.value }))
                  }
                  placeholder="Detalhes adicionais..."
                />
              </label>
              <div className="button-row full-width">
                <button className="primary" type="submit" disabled={busy}>
                  {editingCalendarEntryId ? "Salvar anotação" : "Adicionar anotação"}
                </button>
                <button className="ghost" type="button" onClick={closeCalendarForm}>
                  Cancelar
                </button>
              </div>
            </form>
          </article>
        </div>
      ) : null}
      {photoViewer ? (
        <div
          className="photo-viewer"
          role="dialog"
          aria-modal="true"
          aria-label="Foto do item"
          onMouseDown={() => setPhotoViewer(null)}
        >
          <div className="photo-viewer-content" onMouseDown={(event) => event.stopPropagation()}>
            <button
              className="photo-viewer-close"
              type="button"
              onClick={() => setPhotoViewer(null)}
              aria-label="Fechar foto"
            >
              ×
            </button>
            <img src={photoViewer.src} alt={photoViewer.alt} />
          </div>
        </div>
      ) : null}
    </main>
  );
}

function printReport({ iframeTitle, title, subtitle, tableStyle = "", columns, rows, emptyMessage }) {
  const printFrame = document.createElement("iframe");
  printFrame.title = iframeTitle;
  printFrame.style.position = "fixed";
  printFrame.style.right = "0";
  printFrame.style.bottom = "0";
  printFrame.style.width = "0";
  printFrame.style.height = "0";
  printFrame.style.border = "0";
  printFrame.style.visibility = "hidden";
  document.body.appendChild(printFrame);

  const printDocument = printFrame.contentWindow?.document;
  if (!printDocument) {
    printFrame.remove();
    return false;
  }

  const headerRow = columns.map((col) => `<th>${escapeHtml(col)}</th>`).join("");
  const bodyRows = renderReportRows(rows, columns.length, emptyMessage);

  printDocument.write(`
    <!doctype html>
    <html lang="pt-BR">
      <head>
        <meta charset="UTF-8" />
        <title>${escapeHtml(title)}</title>
        <style>
          @page { margin: 12mm 14mm 14mm; size: A4; }
          * { box-sizing: border-box; }
          body { font-family: Arial, sans-serif; color: #111827; margin: 0; background: #ffffff; }
          .report-header { margin-bottom: 20px; }
          h1 { margin: 0 0 10px; font-size: 28px; line-height: 1.15; }
          p { margin: 0; color: #4b5563; font-size: 14px; line-height: 1.35; }
          table { width: 100%; border-collapse: collapse; }
          th, td { border: 1px solid #d1d5db; padding: 7px 8px; text-align: left; vertical-align: top; line-height: 1.15; }
          th { background: #f3f4f6; font-weight: 700; }
          thead { display: table-header-group; }
          tbody tr { break-inside: avoid; page-break-inside: avoid; }
          ${tableStyle}
        </style>
      </head>
      <body>
        <header class="report-header">
          <h1>${escapeHtml(title)}</h1>
          <p>${escapeHtml(subtitle)}</p>
        </header>
        <table>
          <thead><tr>${headerRow}</tr></thead>
          <tbody>${bodyRows}</tbody>
        </table>
        <script>
          window.addEventListener("load", () => {
            window.focus();
            window.print();
          });
        </script>
      </body>
    </html>
  `);
  printDocument.close();

  const removePrintFrame = () => {
    window.setTimeout(() => {
      printFrame.remove();
    }, 500);
  };

  printFrame.contentWindow?.addEventListener("afterprint", removePrintFrame, { once: true });
  window.setTimeout(removePrintFrame, 60_000);
  return true;
}

function renderReportRows(rows, columnCount, emptyMessage = "Nenhum registro encontrado.") {
  if (!rows.length) {
    return `<tr><td colspan="${columnCount}">${escapeHtml(emptyMessage)}</td></tr>`;
  }

  return rows
    .map((row) => `<tr>${row.map((cell) => `<td>${escapeHtml(cell)}</td>`).join("")}</tr>`)
    .join("");
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

export default App;
