const webSettingsStorageKey = "ownDesk.webSettings.v1";
const webRuntimeStorageKey = "ownDesk.webRuntime.v1";

function initializeWebConsole() {
  const injectedSession = hasAuthSession()
    ? {
        account: state.account,
        token: state.token,
        password: state.password,
        sessionToken: state.sessionToken
      }
    : null;

  loadWebSettings();
  bindOrganizationList();
  bindSelectedOrganization();
  if (injectedSession) {
    Object.assign(state, injectedSession);
  }

  bindCollapseState();
  renderAuthState();

  if (hasAuthSession()) {
    startDeviceUpdates();
    refreshDevicesAfterLogin();
    return;
  }

  renderDevices([]);
}

function loadWebSettings() {
  const fallback = {
    organizations: [createOrganization("默认组织")],
    selectedOrganizationId: "",
    collapsedSections: []
  };

  let stored = fallback;
  try {
    const raw = localStorage.getItem(webSettingsStorageKey);
    stored = raw ? JSON.parse(raw) : fallback;
  } catch {
    stored = fallback;
  }

  state.organizations = normalizeOrganizations(stored.organizations);
  applyRuntimeOrganizations(loadRuntimeSettings());
  state.selectedOrganizationId = state.organizations.some((organization) => organization.id === stored.selectedOrganizationId)
    ? stored.selectedOrganizationId
    : state.organizations[0].id;
  state.collapsedSections = new Set(Array.isArray(stored.collapsedSections) ? stored.collapsedSections : []);
  saveWebSettings();
}

function saveWebSettings() {
  const payload = {
    organizations: state.organizations.map(toStoredOrganization),
    selectedOrganizationId: state.selectedOrganizationId,
    collapsedSections: [...state.collapsedSections]
  };
  localStorage.setItem(webSettingsStorageKey, JSON.stringify(payload));
  saveRuntimeSettings();
}

function loadRuntimeSettings() {
  try {
    return JSON.parse(sessionStorage.getItem(webRuntimeStorageKey) || "{}");
  } catch {
    return {};
  }
}

function saveRuntimeSettings() {
  const organizations = {};
  for (const organization of state.organizations) {
    if (!organization.token && !organization.account && !organization.sessionToken) {
      continue;
    }

    organizations[organization.id] = {
      token: organization.token,
      account: organization.account,
      signedIn: organization.signedIn,
      sessionToken: organization.sessionToken
    };
  }

  sessionStorage.setItem(webRuntimeStorageKey, JSON.stringify({ organizations }));
}

function applyRuntimeOrganizations(runtime) {
  const sessions = runtime?.organizations || {};
  state.organizations = state.organizations.map((organization) => {
    const session = sessions[organization.id] || {};
    return {
      ...organization,
      token: session.token || organization.token,
      account: session.account || organization.account,
      signedIn: Boolean(session.signedIn && session.sessionToken),
      sessionToken: session.sessionToken || ""
    };
  });
}

function createOrganization(name) {
  return {
    id: createId(),
    name,
    server: location.origin,
    token: "",
    account: "",
    password: "",
    signedIn: false,
    sessionToken: ""
  };
}

function normalizeOrganizations(organizations) {
  const items = Array.isArray(organizations) ? organizations : [];
  const normalized = items.map((organization, index) => ({
    id: String(organization.id || createId()),
    name: String(organization.name || (index === 0 ? "默认组织" : `组织 ${index + 1}`)).trim(),
    server: normalizeServer(organization.server || location.origin),
    token: String(organization.token || "").trim(),
    account: String(organization.account || "").trim(),
    password: "",
    signedIn: Boolean(organization.signedIn && organization.sessionToken),
    sessionToken: String(organization.sessionToken || "")
  }));

  return normalized.length ? normalized : [createOrganization("默认组织")];
}

function toStoredOrganization(organization) {
  return {
    id: organization.id,
    name: organization.name,
    server: organization.server
  };
}

function bindOrganizationList() {
  const selectedId = state.selectedOrganizationId;
  elements.organizationSelect.innerHTML = "";
  for (const organization of state.organizations) {
    const option = document.createElement("option");
    option.value = organization.id;
    option.textContent = displayOrganizationName(organization);
    elements.organizationSelect.appendChild(option);
  }

  elements.organizationSelect.value = selectedId;
}

function bindSelectedOrganization() {
  const organization = currentOrganization();
  elements.organizationNameInput.value = organization.name;
  elements.serverUrlInput.value = organization.server;
  elements.organizationTokenInput.value = organization.token;
  elements.accountInput.value = organization.account;
  elements.passwordInput.value = "";
  elements.confirmPasswordInput.value = "";
  applyOrganizationSession(organization);
}

function currentOrganization() {
  if (!state.organizations.length) {
    loadWebSettings();
  }

  return state.organizations.find((organization) => organization.id === state.selectedOrganizationId) || state.organizations[0];
}

function saveCurrentOrganization(rebind = true) {
  const current = currentOrganization();
  const server = normalizeServer(elements.serverUrlInput.value || location.origin);
  const token = elements.organizationTokenInput.value.trim();
  const changedConnection = current.server !== server || current.token !== token;
  const organization = {
    ...current,
    name: elements.organizationNameInput.value.trim() || current.name || "默认组织",
    server,
    token,
    signedIn: changedConnection ? false : current.signedIn,
    sessionToken: changedConnection ? "" : current.sessionToken
  };

  state.organizations = state.organizations.map((item) => item.id === organization.id ? organization : item);
  applyOrganizationSession(organization);
  resetWebRtcConfigCache();
  saveWebSettings();
  if (rebind) {
    bindOrganizationList();
    bindSelectedOrganization();
  }

  return organization;
}

function addOrganization() {
  stopDeviceUpdates();
  disconnectSelectedViewer();
  const organization = createOrganization(`组织 ${state.organizations.length + 1}`);
  state.organizations.push(organization);
  state.selectedOrganizationId = organization.id;
  saveWebSettings();
  bindOrganizationList();
  bindSelectedOrganization();
  renderDevices([]);
}

function deleteOrganization() {
  stopDeviceUpdates();
  disconnectSelectedViewer();
  state.organizations = state.organizations.filter((organization) => organization.id !== state.selectedOrganizationId);
  if (!state.organizations.length) {
    state.organizations = [createOrganization("默认组织")];
  }

  state.selectedOrganizationId = state.organizations[0].id;
  saveWebSettings();
  bindOrganizationList();
  bindSelectedOrganization();
  renderDevices([]);
}

function switchOrganization() {
  stopDeviceUpdates();
  disconnectSelectedViewer();
  state.selectedOrganizationId = elements.organizationSelect.value;
  resetWebRtcConfigCache();
  saveWebSettings();
  bindSelectedOrganization();
  renderAuthState();

  if (hasAuthSession()) {
    startDeviceUpdates();
    refreshDevicesAfterLogin();
    return;
  }

  renderDevices([]);
}

function persistAuthenticatedSession(session, username, password) {
  const organization = {
    ...currentOrganization(),
    account: session.username || username,
    password: "",
    signedIn: true,
    sessionToken: session.sessionToken || ""
  };
  state.organizations = state.organizations.map((item) => item.id === organization.id ? organization : item);
  applyOrganizationSession(organization);
  elements.passwordInput.value = "";
  resetWebRtcConfigCache();
  saveWebSettings();
  bindOrganizationList();
  renderAuthState();
}

function clearAuthenticatedSession() {
  const organization = {
    ...currentOrganization(),
    password: "",
    signedIn: false,
    sessionToken: ""
  };
  state.organizations = state.organizations.map((item) => item.id === organization.id ? organization : item);
  applyOrganizationSession(organization);
  resetWebRtcConfigCache();
  saveWebSettings();
  renderAuthState();
}

function applyOrganizationSession(organization) {
  state.account = organization.signedIn ? organization.account : "";
  state.token = organization.signedIn ? organization.token : "";
  state.password = "";
  state.sessionToken = organization.signedIn ? organization.sessionToken : "";
}

function renderAuthState() {
  const organization = currentOrganization();
  const loggedIn = hasAuthSession();
  elements.serverStatus.textContent = loggedIn ? "已登录" : "未登录";
  elements.memberIdentity.textContent = loggedIn
    ? `组织：${displayOrganizationName(organization)}\n成员：${organization.account}`
    : "";
  setAuthPanelVisible(!loggedIn);
}

function hasAuthSession() {
  return Boolean(state.account && state.token && state.sessionToken);
}

function apiUrl(path) {
  return `${serverBase()}${path}`;
}

function webSocketUrl(path) {
  const endpoint = new URL(apiUrl(path));
  endpoint.protocol = endpoint.protocol === "https:" ? "wss:" : "ws:";
  return endpoint.toString();
}

function serverBase() {
  return normalizeServer(currentOrganization().server || location.origin);
}

function normalizeServer(server) {
  return String(server || "").trim().replace(/\/+$/, "");
}

function displayOrganizationName(organization) {
  return organization.name || organization.server || "默认组织";
}

function bindCollapseState() {
  for (const section of document.querySelectorAll("[data-section]")) {
    const name = section.dataset.section;
    const collapsed = state.collapsedSections.has(name);
    section.classList.toggle("is-collapsed", collapsed);
    section.querySelector(".collapse-arrow").textContent = collapsed ? "▸" : "▾";
  }
}

function toggleSection(name) {
  if (state.collapsedSections.has(name)) {
    state.collapsedSections.delete(name);
  } else {
    state.collapsedSections.add(name);
  }

  saveWebSettings();
  bindCollapseState();
}

function disconnectSelectedViewer() {
  if (typeof disconnectViewer === "function") {
    disconnectViewer(false);
  }
}

function createId() {
  if (crypto.randomUUID) {
    return crypto.randomUUID();
  }

  return `org-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}
