import { useEffect, useMemo, useRef, useState } from "react";
import "./App.css";
import {
  API_BASE,
  daysAgoIso,
  formatDate,
  formatDateTime,
  request,
  todayIso,
  TOKEN_KEY,
} from "./api";
import { Icon } from "./components/Icon";
import { NumberInput } from "./components/NumberInput";
import {
  emptyInventoryFilters,
  emptyItemForm,
  emptyNoteForm,
  emptyTransferForm,
  itemConditionOptions,
  itemNatureOptions,
  locationOptions,
} from "./constants";

const auditPageSize = 100;
const inventoryPageSize = 20;
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
  removeValue: "",
  removing: false,
};

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
  const start = new Date(end);

  if (period === "24h") start.setDate(start.getDate() - 1);
  if (period === "7d") start.setDate(start.getDate() - 7);
  if (period === "1m") start.setMonth(start.getMonth() - 1);
  if (period === "6m") start.setMonth(start.getMonth() - 6);
  if (period === "1y") start.setFullYear(start.getFullYear() - 1);

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

  const query = params.toString();
  return `/api/audit/timeline${query ? `?${query}` : ""}`;
}

function getAuditPeriodLabel(period, range) {
  if (period === "custom") {
    const startLabel = range.startDate || "início";
    const endLabel = range.endDate || "fim";
    return `${startLabel} até ${endLabel}`;
  }

  return auditPeriodOptions.find((option) => option.value === period)?.label ?? "Período selecionado";
}

function getUserRoleLabel(role) {
  return role === "Admin" ? "Administrador" : "Usuário";
}

function createDefaultAuditRange() {
  return {
    startDate: daysAgoIso(7),
    endDate: todayIso(),
  };
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
  const [inventoryFilters, setInventoryFilters] = useState(emptyInventoryFilters);
  const [inventoryPage, setInventoryPage] = useState(1);
  const [reportRange, setReportRange] = useState({
    startDate: daysAgoIso(7),
    endDate: todayIso(),
  });
  const [auditTimeline, setAuditTimeline] = useState([]);
  const [auditPeriod, setAuditPeriod] = useState(defaultAuditPeriod);
  const [auditCustomRange, setAuditCustomRange] = useState(createDefaultAuditRange);
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
  const keepScrollAtBottomRef = useRef(false);
  const toastTimeoutsRef = useRef(new Map());
  const adminSettingsRef = useRef(null);
  const itemPhotoObjectUrlsRef = useRef(new Map());
  const itemActionPanelRef = useRef(null);
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

  function setMessage(nextMessage) {
    if (!nextMessage) {
      return;
    }

    const toastId = `${Date.now()}-${Math.random()}`;
    const toast = {
      id: toastId,
      text: String(nextMessage),
    };

    setToasts((current) => [...current, toast]);

    const timeoutId = window.setTimeout(() => {
      setToasts((current) => current.filter((item) => item.id !== toastId));
      toastTimeoutsRef.current.delete(toastId);
    }, 8000);

    toastTimeoutsRef.current.set(toastId, timeoutId);
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

  function getItemPhotoUrl(item) {
    return item ? itemPhotoUrls[item.id] ?? "" : "";
  }

  function navigateTo(view) {
    setEditingItemId(null);
    setItemEditReturnView(null);
    setItemForm(emptyItemForm);
    setNewLocationForm(emptyLocationForm);
    setFocusedItemId(null);
    setItemAction(null);
    setItemHistory([]);
    setInventoryFilters(emptyInventoryFilters);
    setInventoryPage(1);
    setTransferForm(emptyTransferForm);
    setActiveView(view);
    setMobileMenuOpen(false);
    setAdminSettingsOpen(false);
    setEditingManagedUserId(null);
    setManagedUserForm(emptyManagedUserForm);
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
        const setup = await request("/api/auth/setup");
        if (!active) return;
        setRequiresInitialAdmin(setup.requiresInitialAdmin);
        setReady(true);
        return;
      }

      try {
        const currentUser = await request("/api/auth/me", { token });
        if (!active) return;
        setUser(currentUser);

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

        if (currentUser.role === "Admin") {
          const auditResponse = await request(buildAuditTimelinePath(defaultAuditPeriod, createDefaultAuditRange()), { token });
          if (active) {
            setAuditTimeline(auditResponse);
            setAuditPage(1);
          }
        } else {
          setAuditTimeline([]);
        }
      } catch {
        localStorage.removeItem(TOKEN_KEY);
        if (!active) return;
        setToken("");
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

    itemPhotoObjectUrlsRef.current.forEach((entry, itemId) => {
      const item = itemsById.get(itemId);

      if (!item?.photoFileName || item.photoFileName !== entry.fileName) {
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
      .filter((item) => item.photoFileName)
      .forEach(async (item) => {
        const currentEntry = itemPhotoObjectUrlsRef.current.get(item.id);
        if (currentEntry?.fileName === item.photoFileName) return;

        try {
          const response = await fetch(`${API_BASE}/api/items/${item.id}/photo`, {
            headers: {
              Authorization: `Bearer ${token}`,
            },
          });

          if (!response.ok) return;

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
          // Foto indisponível não deve bloquear o uso do inventário.
        }
      });

    return () => {
      cancelled = true;
    };
  }, [items, token]);

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
    if (activeView !== "transfer") return;

    setTransferForm(emptyTransferForm);
  }, [activeView]);

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
    const [itemsResponse, noteResponse, locationResponse] = await Promise.all([
      request("/api/items", { token: currentToken }),
      request("/api/me/notes", { token: currentToken }),
      request("/api/locations", { token: currentToken }),
    ]);

    setItems(itemsResponse);
    setNotes(noteResponse);
    setCustomLocations(locationResponse);

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
  }

  async function loadItemHistory(currentToken, itemId) {
    if (!itemId) {
      setItemHistory([]);
      return;
    }

    const response = await request(`/api/items/${itemId}/movements`, { token: currentToken });
    setItemHistory(response);
  }

  async function refreshData(nextToken = token, nextUser = user) {
    if (!nextToken) return;
    await loadWorkspace(nextToken, nextUser);
  }

  async function loadAuditTimeline(currentToken = token, period = auditPeriod, range = auditCustomRange) {
    if (!currentToken) return;

    setAuditLoading(true);

    try {
      const auditResponse = await request(buildAuditTimelinePath(period, range), { token: currentToken });
      setAuditTimeline(auditResponse);
      setAuditPage(1);
    } catch (error) {
      setMessage(error.message);
    } finally {
      setAuditLoading(false);
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
    setBusy(true);

    try {
      const response = await request("/api/auth/login", {
        method: "POST",
        body: authForm,
      });

      localStorage.setItem(TOKEN_KEY, response.token);
      setToken(response.token);
      setUser(response.user);
      await loadWorkspace(response.token, response.user);
      await loadAdminUsers(response.token);
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
    setBusy(true);

    try {
      await request("/api/auth/initial-admin", {
        method: "POST",
        body: initialAdminForm,
      });

      setInitialAdminForm({ password: "" });
      setRequiresInitialAdmin(false);
      setAuthMode("login");
      setAuthError("Senha do admin criada.");
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
    setBusy(true);

    try {
      await request("/api/auth/register", {
        method: "POST",
        body: registerForm,
      });

      setAuthForm({ username: registerForm.username, password: "" });
      setRegisterForm(emptyRegisterForm);
      setAuthMode("login");
      setAuthError("Cadastro criado. Entre com a senha cadastrada.");
    } catch (error) {
      setAuthError(error.message);
    } finally {
      setBusy(false);
      setReady(true);
    }
  }

  async function handleLogout() {
    try {
      await request("/api/auth/logout", { token, method: "POST" });
    } catch {
      // Ignore logout transport errors; local session is still cleared.
    } finally {
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
  }

  async function loadAdminUsers(currentToken = token) {
    if (!currentToken) return;

    setAdminSettingsLoading(true);
    setMessage("");

    try {
      const usersResponse = await request("/api/auth/users", { token: currentToken });
      setAdminUsers(usersResponse);
    } catch (error) {
      setMessage(error.message);
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
      setMessage("Usuário atualizado.");
    } catch (error) {
      setMessage(error.message);
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
      setMessage("Usuário excluído.");
      await refreshData();
    } catch (error) {
      setMessage(error.message);
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
      (location) => location.localeCompare(nextLocation, "pt-BR", { sensitivity: "base" }) === 0
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
      setMessage("Localização adicionada.");
    } catch (error) {
      setMessage(error.message);
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
        location.localeCompare(nextLocation, "pt-BR", { sensitivity: "base" }) === 0 &&
        location.localeCompare(currentLocation, "pt-BR", { sensitivity: "base" }) !== 0
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
      setMessage("Localização editada.");
      await refreshData();
    } catch (error) {
      setMessage(error.message);
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
    const locationToRemove = newLocationForm.editName || newLocationForm.removeValue;
    if (!locationToRemove) {
      setMessage("Selecione a localização para remover.");
      return;
    }

    if (!window.confirm(`Remover a localização "${locationToRemove}"?`)) return;

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
      setNewLocationForm((current) => ({
        ...current,
        editName: "",
        editValue: "",
        removeValue: "",
        removing: false,
      }));
      setMessage("Localização removida.");
    } catch (error) {
      setMessage(error.message);
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

      if (wasEditingItem) {
        await request(`/api/items/${editingItemId}`, {
          token,
          method: "PUT",
          body: payload,
        });
        setMessage("Item atualizado com sucesso.");
      } else {
        await request("/api/items", {
          token,
          method: "POST",
          body: payload,
        });
        setMessage("Item criado com sucesso.");
      }

      setItemForm(emptyItemForm);
      setNewLocationForm(emptyLocationForm);
      setEditingItemId(null);
      setItemEditReturnView(null);
      await refreshData();
      if (wasEditingItem) {
        setActiveView("search");
      }
      setItemAction(null);
    } catch (error) {
      setMessage(error.message);
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
      }
      await refreshData();
      setMessage("Item removido do inventário.");
    } catch (error) {
      setMessage(error.message);
    } finally {
      setBusy(false);
    }
  }

  function handleEditItem(item) {
    setItemEditReturnView(activeView);
    setActiveView("items");
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
      setMessage(error.message);
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

      if (resolvedFromLocation !== selectedTransferItem.location?.trim()) {
        throw new Error("A origem precisa ser a localização atual do item.");
      }

      if (Number(transferForm.quantity) > selectedTransferItem.quantity) {
        throw new Error("A quantidade não pode ser maior que o estoque atual do item.");
      }

      if (transferForm.destinationType === "Local" && !transferForm.toLocation.trim()) {
        throw new Error("Informe o destino físico da transferência.");
      }

      if (
        transferForm.destinationType === "Local" &&
        transferForm.toLocation.trim() === selectedTransferItem.location?.trim()
      ) {
        throw new Error("O destino precisa ser diferente da origem.");
      }

      if (transferForm.destinationType === "Pessoa" && !transferForm.destinationPerson.trim()) {
        throw new Error("Informe o militar responsável pelo recebimento.");
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
          notes: transferForm.notes,
        },
      });

      setMessage("Transferência registrada com sucesso.");
      setTransferForm(emptyTransferForm);
      await refreshData();
    } catch (error) {
      setMessage(error.message);
    } finally {
      setBusy(false);
    }
  }

  async function loadNotes(currentToken = token) {
    const response = await request("/api/me/notes", { token: currentToken });
    setNotes(response);
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
      };

      await request(editingNoteId ? `/api/me/notes/${editingNoteId}` : "/api/me/notes", {
        token,
        method: editingNoteId ? "PUT" : "POST",
        body: payload,
      });
      setMessage(editingNoteId ? "Anotação atualizada." : "Anotação criada.");
      setNoteForm(emptyNoteForm);
      setEditingNoteId(null);
      await loadNotes();
    } catch (error) {
      setMessage(error.message);
    } finally {
      setBusy(false);
    }
  }

  function handleEditNote(noteItem) {
    setEditingNoteId(noteItem.id);
    setNoteForm({
      title: noteItem.title ?? "",
      content: noteItem.content ?? "",
      tags: noteItem.tags ?? "",
    });
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
      }
      setMessage("Anotação excluída.");
      await loadNotes();
    } catch (error) {
      setMessage(error.message);
    } finally {
      setBusy(false);
    }
  }

  async function handleLoadReport(event) {
    event.preventDefault();
    setBusy(true);
    setMessage("");

    try {
      if (reportRange.startDate > reportRange.endDate) {
        throw new Error("A data inicial precisa ser anterior ou igual à data final.");
      }

      await loadItemHistory(token, selectedItem?.id);
    } catch (error) {
      setMessage(error.message);
    } finally {
      setBusy(false);
    }
  }

  async function handleSelectHistoryItem(itemId) {
    if (focusedItemId === itemId && itemAction === "history") {
      setItemAction(null);
      return;
    }

    setFocusedItemId(itemId);
    setActiveView("search");
    setItemAction("history");
    setBusy(true);
    try {
      await loadItemHistory(token, itemId);
    } catch (error) {
      setMessage(error.message);
    } finally {
      setBusy(false);
    }
  }

  async function handleSelectPeriodItem(itemId) {
    if (focusedItemId === itemId && itemAction === "period") {
      setItemAction(null);
      return;
    }

    setFocusedItemId(itemId);
    setActiveView("search");
    setItemAction("period");
    setBusy(true);
    try {
      await loadItemHistory(token, itemId);
    } catch (error) {
      setMessage(error.message);
    } finally {
      setBusy(false);
    }
  }

  function handleExportInventoryPdf() {
    const printFrame = document.createElement("iframe");
    printFrame.title = "Exportação do inventário";
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
      setMessage("Não foi possível preparar a exportação em PDF.");
      return;
    }

    const rows = (filteredItems.length ? filteredItems : [null])
      .map((item) =>
        item
          ? `
            <tr>
              <td>${escapeHtml(item.name)}</td>
              <td>${escapeHtml(item.quantity)}</td>
              <td>${escapeHtml(item.assetTag)}</td>
              <td>${escapeHtml(item.nature)}</td>
              <td>${escapeHtml(item.location)}</td>
              <td>${escapeHtml(item.condition)}</td>
              <td>${escapeHtml(item.isDischarged ? formatDateTime(item.dischargedAt) : "-")}</td>
            </tr>
          `
          : '<tr><td colspan="7">Nenhum item encontrado.</td></tr>',
      )
      .join("");

    printDocument.write(`
      <!doctype html>
      <html lang="pt-BR">
        <head>
          <meta charset="UTF-8" />
          <title>Inventário</title>
          <style>
            @page {
              margin: 12mm 14mm 14mm;
              size: A4;
            }

            * {
              box-sizing: border-box;
            }

            body {
              font-family: Arial, sans-serif;
              color: #111827;
              margin: 0;
              background: #ffffff;
            }

            .report-header {
              margin-bottom: 20px;
            }

            h1 {
              margin: 0 0 10px;
              font-size: 28px;
              line-height: 1.15;
            }

            p {
              margin: 0;
              color: #4b5563;
              font-size: 14px;
              line-height: 1.35;
            }

            table {
              width: 100%;
              border-collapse: collapse;
              font-size: 11px;
            }

            th,
            td {
              border: 1px solid #d1d5db;
              padding: 7px 8px;
              text-align: left;
              vertical-align: top;
              line-height: 1.15;
            }

            th {
              background: #f3f4f6;
              font-weight: 700;
            }

            thead {
              display: table-header-group;
            }

            tbody tr {
              break-inside: avoid;
              page-break-inside: avoid;
            }

          </style>
        </head>
        <body>
          <header class="report-header">
            <h1>Inventário</h1>
            <p>Exportado em ${escapeHtml(formatDateTime(new Date().toISOString()))}</p>
          </header>
          <table>
            <thead>
              <tr>
                <th>Item</th>
                <th>Qtd.</th>
                <th>Tombo</th>
                <th>Natureza</th>
                <th>Localização</th>
                <th>Conservação</th>
                <th>Descargueado</th>
              </tr>
            </thead>
            <tbody>${rows}</tbody>
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
  }

  function handleExportAuditPdf() {
    const printFrame = document.createElement("iframe");
    printFrame.title = "Exportação da auditoria";
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
      setMessage("Não foi possível preparar a exportação em PDF.");
      return;
    }

    const rows = (auditTimeline.length ? auditTimeline : [null])
      .map((entry) =>
        entry
          ? `
            <tr>
              <td>${escapeHtml(formatDateTime(entry.timestamp))}</td>
              <td>${escapeHtml(entry.actorUserName)}</td>
              <td>${escapeHtml(entry.action)}</td>
              <td>${escapeHtml(entry.entityType)}</td>
              <td>${escapeHtml(entry.summary)}</td>
              <td>${escapeHtml(entry.details)}</td>
            </tr>
          `
          : '<tr><td colspan="6">Nenhum registro de auditoria encontrado.</td></tr>',
      )
      .join("");

    printDocument.write(`
      <!doctype html>
      <html lang="pt-BR">
        <head>
          <meta charset="UTF-8" />
          <title>Auditoria administrativa</title>
          <style>
            @page {
              margin: 12mm 14mm 14mm;
              size: A4;
            }

            * {
              box-sizing: border-box;
            }

            body {
              font-family: Arial, sans-serif;
              color: #111827;
              margin: 0;
              background: #ffffff;
            }

            .report-header {
              margin-bottom: 20px;
            }

            h1 {
              margin: 0 0 10px;
              font-size: 28px;
              line-height: 1.15;
            }

            p {
              margin: 0;
              color: #4b5563;
              font-size: 14px;
              line-height: 1.35;
            }

            table {
              width: 100%;
              border-collapse: collapse;
              font-size: 10px;
            }

            th,
            td {
              border: 1px solid #d1d5db;
              padding: 7px 8px;
              text-align: left;
              vertical-align: top;
              line-height: 1.15;
            }

            th {
              background: #f3f4f6;
              font-weight: 700;
            }

            thead {
              display: table-header-group;
            }

            tbody tr {
              break-inside: avoid;
              page-break-inside: avoid;
            }

          </style>
        </head>
        <body>
          <header class="report-header">
            <h1>Auditoria administrativa</h1>
            <p>Período: ${escapeHtml(getAuditPeriodLabel(auditPeriod, auditCustomRange))} · Exportado em ${escapeHtml(formatDateTime(new Date().toISOString()))}</p>
          </header>
          <table>
            <thead>
              <tr>
                <th>Data</th>
                <th>Responsável</th>
                <th>Ação</th>
                <th>Área</th>
                <th>Resumo</th>
                <th>Detalhes</th>
              </tr>
            </thead>
            <tbody>${rows}</tbody>
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
  }

  const selectedItem = items.find((item) => item.id === focusedItemId) ?? null;
  const selectedTransferItem = items.find((item) => String(item.id) === transferForm.itemId) ?? null;
  const selectedTransferMaxQuantity = selectedTransferItem?.quantity ?? 0;
  const isAdmin = user?.role === "Admin";
  const managedUsers = adminUsers;
  const editingManagedUser = managedUsers.find((item) => item.id === editingManagedUserId) ?? null;
  const isManagedAdminLocked = editingManagedUser?.role === "Admin";
  const canEditManagedPassword = isAdmin || editingManagedUser?.id === user?.id;
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
  const filteredItemMovements = itemHistory.filter((movement) => {
    const movementDate = movement.createdAt?.slice(0, 10);
    return movementDate >= reportRange.startDate && movementDate <= reportRange.endDate;
  });
  const filteredItems = items.filter((item) => {
    const matchesName = item.name.toLowerCase().includes(inventoryFilters.name.trim().toLowerCase());
    const matchesAssetTag = item.assetTag.toLowerCase().includes(inventoryFilters.assetTag.trim().toLowerCase());
    const matchesCondition = !inventoryFilters.condition || item.condition === inventoryFilters.condition;
    const matchesNature = !inventoryFilters.nature || item.nature === inventoryFilters.nature;
    const matchesLocation = !inventoryFilters.location || item.location === inventoryFilters.location;

    return matchesName && matchesAssetTag && matchesCondition && matchesNature && matchesLocation;
  });
  const inventoryTotalPages = Math.max(1, Math.ceil(filteredItems.length / inventoryPageSize));
  const safeInventoryPage = Math.min(inventoryPage, inventoryTotalPages);
  const paginatedInventoryItems = filteredItems.slice(
    (safeInventoryPage - 1) * inventoryPageSize,
    safeInventoryPage * inventoryPageSize,
  );
  const selectedItemIsOnInventoryPage = paginatedInventoryItems.some((item) => item.id === focusedItemId);
  const itemFormPhotoPreviewUrl = itemForm.removePhoto
    ? ""
    : itemForm.photoPreviewUrl || (editingItemId ? itemPhotoUrls[editingItemId] ?? "" : "");

  useEffect(() => {
    if (!itemAction || !selectedItemIsOnInventoryPage) return;

    requestAnimationFrame(() => {
      itemActionPanelRef.current?.scrollIntoView({
        behavior: "smooth",
        block: "nearest",
      });
    });
  }, [itemAction, focusedItemId, selectedItemIsOnInventoryPage]);

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
                  O cadastro precisa ser autorizado por um admin
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
          <button
            className={activeView === "items" ? "active" : ""}
            type="button"
            onClick={() => navigateTo("items")}
          >
            Cadastro
          </button>
          <button
            className={activeView === "search" ? "active" : ""}
            type="button"
            onClick={() => navigateTo("search")}
          >
            Inventário
          </button>
          <button
            className={activeView === "transfer" ? "active" : ""}
            type="button"
            onClick={() => navigateTo("transfer")}
          >
            Transferência
          </button>
          <button
            className={activeView === "notes" ? "active" : ""}
            type="button"
            onClick={() => navigateTo("notes")}
          >
            Anotações
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
            <h1>
              {activeView === "items"
                ? "Cadastro de itens"
                : activeView === "search"
                  ? "Inventário"
                  : activeView === "transfer"
                    ? "Transferência de material"
                    : activeView === "notes"
                      ? "Anotações privadas"
                      : "Auditoria administrativa"}
            </h1>
          </div>
          <div className="admin-settings" ref={adminSettingsRef}>
              <button
                className="icon-button"
                type="button"
                aria-label="Configurações administrativas"
                aria-expanded={adminSettingsOpen}
                onClick={handleToggleAdminSettings}
              >
                <Icon type="settings" />
              </button>
              {adminSettingsOpen ? (
                <div className="admin-settings-panel">
                  <div className="settings-panel-header">
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
                              title={isManagedAdminLocked ? "Esta permissão não pode ser alterada." : undefined}
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
                  ) : managedUsers.length ? (
                    <div className="user-list">
                      {managedUsers.map((managedUser) => (
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
                              disabled={busy}
                              onClick={() => handleStartEditUser(managedUser)}
                            >
                              <Icon type="edit" />
                            </button>
                            {isAdmin && managedUser.id !== user?.id ? (
                              <button
                                className="danger-icon"
                                type="button"
                                aria-label={`Excluir ${managedUser.username}`}
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
              <h2>Dados do item</h2>
            </div>
            {isAdmin && !editingItemId ? (
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
                  required
                />
              </label>
              <label>
                Quantidade
                <NumberInput
                  value={itemForm.quantity}
                  min={0}
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
                Número de patrimônio
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
                        removeValue: "",
                      }))
                    }
                    title="Adicionar localização"
                    aria-label="Adicionar localização"
                  >
                    {newLocationForm.open ? "-" : "+"}
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
                        Localização adicionada
                        <select
                          value={newLocationForm.editName}
                          onChange={(event) =>
                            setNewLocationForm((current) => ({
                              ...current,
                              editName: event.target.value,
                              editValue: event.target.value,
                              removeValue: event.target.value,
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
                        className="ghost"
                        type="button"
                        onClick={handleRemoveLocation}
                        disabled={newLocationForm.removing || !newLocationForm.editName}
                      >
                        Remover
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
                    <input type="file" accept="image/*" onChange={handleItemPhotoChange} />
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
                    onClick={() => refreshData()}
                    disabled={busy}
                    title="Atualizar inventário"
                    aria-label="Atualizar inventário"
                  >
                    <Icon type="refresh" />
                  </button>
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
                  Patrimônio
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
                <label className="full-width">
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
              </div>
          <div className="table-wrap inventory-table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Item</th>
                  <th>Qtd.</th>
                  <th>Tombo</th>
                  <th>Natureza</th>
                  <th>Localização</th>
                  <th>Conservação</th>
                  <th className="center-column">Descargueado</th>
                  <th className="center-column">Foto</th>
                  <th>Ações</th>
                </tr>
              </thead>
              <tbody>
                {paginatedInventoryItems.map((item) => (
                  <tr key={item.id} className={focusedItemId === item.id ? "active-row" : ""}>
                    <td>{item.name}</td>
                    <td>{item.quantity}</td>
                    <td>{item.assetTag}</td>
                    <td>{item.nature}</td>
                    <td>{item.location}</td>
                    <td>{item.condition}</td>
                    <td className="center-column">
                      {item.isDischarged ? (
                        <span title={formatDateTime(item.dischargedAt)}>
                          {formatDate(item.dischargedAt)}
                        </span>
                      ) : (
                        "-"
                      )}
                    </td>
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
                    <td>
                      <div className="row-actions">
                        <button
                          type="button"
                          className="icon-button"
                          onClick={() => handleSelectHistoryItem(item.id)}
                          title="Histórico individual"
                          aria-label="Histórico individual"
                        >
                          <Icon type="history" />
                        </button>
                        <button
                          type="button"
                          className="icon-button"
                          onClick={() => handleSelectPeriodItem(item.id)}
                          title="Movimentação por período"
                          aria-label="Movimentação por período"
                        >
                          <Icon type="calendar" />
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

            {itemAction === "history" && selectedItemIsOnInventoryPage ? (
              <article className="panel section-card" ref={itemActionPanelRef}>
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
                      {selectedItem.assetTag} · {selectedItem.location}
                    </span>
                  </div>

                  <div className="timeline">
                    {itemHistory.map((movement) => (
                      <article key={movement.id} className="timeline-item">
                        <span className="timeline-dot" />
                        <div>
                          <strong>{movement.quantity} unidade(s)</strong>
                          <p>
                            {formatMovementOrigin(movement)} → {formatMovementDestination(movement)}
                          </p>
                          <small>
                            {formatDateTime(movement.createdAt)} · {movement.performedByUserName}
                          </small>
                        </div>
                      </article>
                    ))}
                  </div>
                </>
              ) : (
                <p className="muted">Cadastre ou selecione um item para ver o histórico.</p>
              )}
              </article>
            ) : null}

            {itemAction === "period" && selectedItemIsOnInventoryPage ? (
              <article className="panel section-card" ref={itemActionPanelRef}>
              <div className="section-heading">
                <div>
                  <p className="eyebrow">Item selecionado</p>
                  <h2>Movimentação por período</h2>
                </div>
              </div>

              <form className="filter-row period-filter" onSubmit={handleLoadReport}>
                <label>
                  Início
                  <input
                    type="date"
                    value={reportRange.startDate}
                    onChange={(event) => setReportRange((current) => ({ ...current, startDate: event.target.value }))}
                  />
                </label>
                <label>
                  Fim
                  <input
                    type="date"
                    value={reportRange.endDate}
                    onChange={(event) => setReportRange((current) => ({ ...current, endDate: event.target.value }))}
                  />
                </label>
                <button className="primary" type="submit" disabled={busy || !selectedItem}>
                  Filtrar
                </button>
              </form>

              <div className="table-wrap compact">
                <table>
                  <thead>
                    <tr>
                      <th>Data</th>
                      <th>Qtd.</th>
                      <th>Origem</th>
                      <th>Destino</th>
                      <th>Responsável</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredItemMovements.map((movement) => (
                      <tr key={movement.id}>
                        <td>{formatDateTime(movement.createdAt)}</td>
                        <td>{movement.quantity}</td>
                        <td>{formatMovementOrigin(movement)}</td>
                        <td>{formatMovementDestination(movement)}</td>
                        <td>{movement.performedByUserName}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              </article>
            ) : null}
          </div>
        ) : null}

        {activeView === "transfer" ? (
          <article className="panel section-card">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Movimentação</p>
              <h2>Origem, destino e quantidade</h2>
            </div>
          </div>

          <form className="form-grid" onSubmit={handleTransfer}>
            <label>
              Item
              <select
                value={transferForm.itemId}
                onChange={(event) =>
                  setTransferForm({
                    ...emptyTransferForm,
                    itemId: event.target.value,
                    fromLocation: items.find((item) => String(item.id) === event.target.value)?.location ?? "",
                  })
                }
                required
              >
                <option value="">Selecione</option>
                {items.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Quantidade
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
              <select
                value={transferForm.fromLocation}
                disabled={!selectedTransferItem}
                onChange={() => {}}
                required
              >
                {selectedTransferItem ? (
                  <option value={selectedTransferItem.location}>{selectedTransferItem.location}</option>
                ) : (
                  <option value="">Selecione um item</option>
                )}
              </select>
            </label>
            <label>
              Tipo de destino
              <select
                value={transferForm.destinationType}
                disabled={!selectedTransferItem}
                onChange={(event) => setTransferForm((current) => ({ ...current, destinationType: event.target.value }))}
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
                >
                  <option value="">Selecione</option>
                  {availableLocationOptions.map((location) => (
                    <option key={location} value={location} disabled={location === selectedTransferItem?.location}>
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
                />
              </label>
            )}
            <label className="full-width">
              Observações da transferência
              <textarea
                rows="3"
                value={transferForm.notes}
                disabled={!selectedTransferItem}
                onChange={(event) => setTransferForm((current) => ({ ...current, notes: event.target.value }))}
                placeholder="Informações adicionais sobre a transferência"
              />
            </label>
            <div className="button-row">
                <button className="primary full-width" type="submit" disabled={busy}>
                Registrar transferência
              </button>
            </div>
          </form>
          </article>
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
              onChange={(event) => setNoteForm((current) => ({ ...current, title: event.target.value }))}
              placeholder="Título da anotação"
              required
            />
            <textarea
              rows="9"
              value={noteForm.content}
              onChange={(event) => setNoteForm((current) => ({ ...current, content: event.target.value }))}
              placeholder="Conteúdo da anotação..."
              required
            />
            <input
              value={noteForm.tags}
              onChange={(event) => setNoteForm((current) => ({ ...current, tags: event.target.value }))}
              placeholder="Etiquetas separadas por vírgula"
            />
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
                    <small>{formatDateTime(noteItem.updatedAt)}</small>
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
                {noteItem.tags ? (
                  <div className="tag-list">
                    {noteItem.tags.split(",").map((tag) => {
                      const trimmedTag = tag.trim();
                      return trimmedTag ? <span key={trimmedTag}>{trimmedTag}</span> : null;
                    })}
                  </div>
                ) : null}
              </article>
            ))}
          </div>
          </article>
        ) : null}

        {activeView === "audit" && isAdmin ? (
          <article className="panel section-card">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Auditoria</p>
                <h2>Timeline administrativa</h2>
              </div>
              <button className="ghost" type="button" onClick={handleExportAuditPdf}>
                Exportar PDF
              </button>
            </div>

            <form className="filter-grid audit-filter" onSubmit={handleAuditFilterSubmit}>
              <label>
                Período
                <select value={auditPeriod} onChange={handleAuditPeriodChange}>
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
                  <span className="timeline-dot admin" />
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
      {toasts.length ? (
        <div className="toast-stack" role="status" aria-live="polite">
          {toasts.map((toast) => (
            <div className="notice toast-message" key={toast.id}>
              {toast.text}
            </div>
          ))}
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

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

export default App;
