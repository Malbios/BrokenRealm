type Culture = "en" | "de";
type Panel = "player" | "admin";

type MonacoEditor = {
  getValue(): string;
  setValue(value: string): void;
  layout(): void;
  getModel(): MonacoModel | null;
  setModel(model: MonacoModel): void;
  setPosition(position: { lineNumber: number; column: number }): void;
  revealLineInCenter(lineNumber: number): void;
  focus(): void;
};

type MonacoDisposable = { dispose(): void };

type MonacoModel = {
  getValue(): string;
  setValue(value: string): void;
  onDidChangeContent(listener: () => void): MonacoDisposable;
};

type MonacoApi = {
  editor: {
    create(element: HTMLElement, options: Record<string, unknown>): MonacoEditor;
    createModel(value: string, language?: string, uri?: unknown): MonacoModel;
    defineTheme(name: string, theme: Record<string, unknown>): void;
    setTheme(name: string): void;
    setModelMarkers(model: unknown, owner: string, markers: MonacoMarker[]): void;
  };
  languages: {
    typescript: {
      typescriptDefaults: {
        addExtraLib(source: string, path?: string): MonacoDisposable;
        setCompilerOptions(options: Record<string, unknown>): void;
        setEagerModelSync(value: boolean): void;
      };
    };
  };
  Uri: { parse(value: string): unknown };
};

type MonacoLoader = {
  config(options: Record<string, unknown>): void;
  (dependencies: string[], callback: () => void): void;
};

interface Window {
  monaco?: MonacoApi;
  require?: MonacoLoader;
}

type CommandResponse = {
  lines: string[];
};

type SessionCharacter = {
  id: string;
  locationId: string;
};

type GameSessionResponse = {
  accountId: string;
  selectedCharacterId: string;
  characters: SessionCharacter[];
};

type SelectCharacterResponse = {
  selectedCharacterId: string;
  characters: SessionCharacter[];
};

const gameFetchInit: RequestInit = { credentials: "include" };

type ScriptResponse = {
  moduleId: string;
  sourceRevision: number;
  dependencies: string[];
  classes: string[];
  source: string;
  affectedModules: string[];
  affectedObjects: string[];
};

type ScriptErrorResponse = {
  diagnostics?: CompilerDiagnostic[];
};

type ScriptConflictResponse = {
  moduleId: string;
  expectedSourceRevision: number;
  currentSourceRevision: number;
  message: string;
};

type CompilerDiagnostic = {
  message: string;
  file: string;
  line: number;
  column: number;
};

type MonacoMarker = {
  severity: number;
  message: string;
  startLineNumber: number;
  startColumn: number;
  endLineNumber: number;
  endColumn: number;
};

type AdminBehaviorModule = {
  moduleId: string;
  dependencies: string[];
  classes: string[];
};

const form = document.querySelector<HTMLFormElement>("#command-form");
const input = document.querySelector<HTMLInputElement>("#command");
const culture = document.querySelector<HTMLSelectElement>("#culture");
const characterLabel = document.querySelector<HTMLLabelElement>("#character-label");
const characterSelect = document.querySelector<HTMLSelectElement>("#character");
const log = document.querySelector<HTMLDivElement>("#log");
const playerTab = document.querySelector<HTMLButtonElement>("#player-tab");
const adminTab = document.querySelector<HTMLButtonElement>("#admin-tab");
const playerPanel = document.querySelector<HTMLDivElement>("#player-panel");
const adminPanel = document.querySelector<HTMLDivElement>("#admin-panel");
const editorHost = document.querySelector<HTMLDivElement>("#script-editor");
const scriptSource = document.querySelector<HTMLTextAreaElement>("#script-source");
const checkScript = document.querySelector<HTMLButtonElement>("#check-script");
const reloadScript = document.querySelector<HTMLButtonElement>("#reload-script");
const saveScript = document.querySelector<HTMLButtonElement>("#save-script");
const scriptStatus = document.querySelector<HTMLDivElement>("#script-status");
const behaviorModuleSelect = document.querySelector<HTMLSelectElement>("#behavior-module");
const verbTitle = document.querySelector<HTMLHeadingElement>("#verb-title");
const moduleDetails = document.querySelector<HTMLDivElement>("#module-details");
let editor: MonacoEditor | null = null;
let editorReady: Promise<void> | null = null;
let behaviorModules: AdminBehaviorModule[] = [];
let activeModuleId: string | null = null;
let activePanel: Panel = "player";
let currentSession: GameSessionResponse | null = null;
const moduleModels = new Map<string, MonacoModel>();
const modulePayloads = new Map<string, ScriptResponse>();
const savedSources = new Map<string, string>();

function appendLine(text: string, className = "line"): void {
  if (!log) return;

  const line = document.createElement("div");
  line.className = className;
  line.textContent = text;
  log.appendChild(line);
  log.scrollTop = log.scrollHeight;
}

type BehaviorAction = "check" | "save";

function setStatus(text: string, isError = false): void {
  if (!scriptStatus) return;
  scriptStatus.replaceChildren();
  scriptStatus.textContent = text;
  scriptStatus.classList.toggle("error-line", isError);
}

function setStatusMessage(text: string, isError = false): HTMLParagraphElement | null {
  if (!scriptStatus) return null;
  const message = document.createElement("p");
  message.className = "status-message";
  message.textContent = text;
  if (isError) message.classList.add("error-line");
  return message;
}

function setStatusWithActions(text: string, actions: HTMLElement[], isError = false): void {
  if (!scriptStatus) return;
  scriptStatus.replaceChildren();
  scriptStatus.classList.toggle("error-line", isError);
  const message = setStatusMessage(text, isError);
  if (message) scriptStatus.appendChild(message);
  if (actions.length > 0) {
    const actionRow = document.createElement("div");
    actionRow.className = "status-actions";
    actions.forEach((action) => actionRow.appendChild(action));
    scriptStatus.appendChild(actionRow);
  }
}

function createStatusButton(label: string, className: string, onClick: () => void): HTMLButtonElement {
  const button = document.createElement("button");
  button.type = "button";
  button.className = className;
  button.textContent = label;
  button.addEventListener("click", onClick);
  return button;
}

function setEditorMarkers(diagnostics: CompilerDiagnostic[], selectedId = selectedModuleId()): void {
  if (!window.monaco) return;

  moduleModels.forEach((model) => window.monaco?.editor.setModelMarkers(model, "brokenrealm", []));
  const byModule = new Map<string, CompilerDiagnostic[]>();

  diagnostics.forEach((diagnostic) => {
    const moduleId = diagnostic.file && diagnostic.file !== "behavior" ? diagnostic.file : selectedId;
    if (!moduleId) return;
    byModule.set(moduleId, [...(byModule.get(moduleId) ?? []), diagnostic]);
  });

  byModule.forEach((moduleDiagnostics, moduleId) => {
    const model = moduleModels.get(moduleId);
    if (!model) return;
    const markers = moduleDiagnostics
      .filter((diagnostic) => diagnostic.line > 0 && diagnostic.column > 0)
      .map((diagnostic) => ({
        severity: 8,
        message: diagnostic.message,
        startLineNumber: diagnostic.line,
        startColumn: diagnostic.column,
        endLineNumber: diagnostic.line,
        endColumn: diagnostic.column + 1,
      }));
    window.monaco?.editor.setModelMarkers(model, "brokenrealm", markers);
  });
}

async function setDiagnostics(diagnostics: CompilerDiagnostic[]): Promise<void> {
  if (!scriptStatus) return;

  for (const diagnostic of diagnostics) {
    if (diagnostic.file && diagnostic.file !== "behavior" && !moduleModels.has(diagnostic.file)) {
      try {
        ensureModuleModel(await fetchBehaviorModule(diagnostic.file));
      } catch {
        // Keep the textual diagnostic when its source is no longer available.
      }
    }
  }

  setEditorMarkers(diagnostics);

  scriptStatus.replaceChildren();
  scriptStatus.classList.add("error-line");

  const list = document.createElement("ul");
  list.className = "diagnostics";

  diagnostics.forEach((diagnostic) => {
    const item = document.createElement("li");
    const file = diagnostic.file || selectedModuleId() || "behavior";
    const location = diagnostic.line > 0 ? `${file}:${diagnostic.line}:${diagnostic.column}: ` : file ? `${file}: ` : "";
    item.textContent = `${location}${diagnostic.message}`;
    if (moduleModels.has(file) && behaviorModuleSelect) {
      item.tabIndex = 0;
      item.classList.add("diagnostic-link");
      const openDiagnostic = (): void => {
        if (file !== activeModuleId && !canLeaveModule(activeModuleId)) return;
        behaviorModuleSelect.value = file;
        const model = moduleModels.get(file);
        const payload = modulePayloads.get(file);
        if (model && editor) {
          editor.setModel(model);
          editor.setPosition({ lineNumber: Math.max(1, diagnostic.line), column: Math.max(1, diagnostic.column) });
          editor.revealLineInCenter(Math.max(1, diagnostic.line));
          editor.focus();
          updateEditorTitle(file);
          activeModuleId = file;
          if (payload) {
            renderModuleDetails(payload);
            void configureDependencyLibraries(payload);
          }
        }
      };
      item.addEventListener("click", openDiagnostic);
      item.addEventListener("keydown", (event) => {
        if (event.key === "Enter" || event.key === " ") openDiagnostic();
      });
    }
    list.appendChild(item);
  });

  scriptStatus.appendChild(list);
}

function renderModuleDetails(payload: ScriptResponse): void {
  if (!moduleDetails) return;
  moduleDetails.replaceChildren();

  const details = [
    ["Source revision", payload.sourceRevision.toString()],
    ["Classes", payload.classes.join(", ") || "none"],
    ["Dependencies", payload.dependencies.join(", ") || "none"],
    ["Affected modules", payload.affectedModules.join(", ") || payload.moduleId],
    ["Affected objects", payload.affectedObjects.join(", ") || "none"],
  ];

  details.forEach(([label, value]) => {
    const group = document.createElement("div");
    group.className = "detail-group";
    const labelElement = document.createElement("span");
    labelElement.className = "detail-label";
    labelElement.textContent = label;
    const valueElement = document.createElement("div");
    valueElement.className = "detail-value";
    valueElement.textContent = value;
    group.append(labelElement, valueElement);
    moduleDetails.appendChild(group);
  });
}

function updateEditorTitle(moduleId: string): void {
  if (!verbTitle) return;
  const model = moduleModels.get(moduleId);
  const dirty = model && model.getValue() !== savedSources.get(moduleId);
  verbTitle.textContent = `${moduleId}${dirty ? " *" : ""}`;
}

function isModuleDirty(moduleId: string): boolean {
  const model = moduleModels.get(moduleId);
  if (model) return model.getValue() !== savedSources.get(moduleId);
  return activeModuleId === moduleId && scriptSource
    ? scriptSource.value !== savedSources.get(moduleId)
    : false;
}

function hasUnsavedChanges(): boolean {
  return [...savedSources.keys()].some(isModuleDirty);
}

function canLeaveModule(moduleId: string | null): boolean {
  return !moduleId
    || !isModuleDirty(moduleId)
    || window.confirm(`Leave ${moduleId} with unsaved changes? They will remain in this browser until the page is reloaded.`);
}

function updateCharacterSelectorVisibility(panel: Panel): void {
  if (!characterLabel) return;

  const isPlayer = panel === "player";
  const hasCharacters = (currentSession?.characters.length ?? 0) > 0;
  characterLabel.hidden = !isPlayer || !hasCharacters;
}

function showPanel(panel: Panel): void {
  const isAdmin = panel === "admin";
  activePanel = panel;
  playerTab?.classList.toggle("active", !isAdmin);
  adminTab?.classList.toggle("active", isAdmin);
  playerPanel?.classList.toggle("active", !isAdmin);
  adminPanel?.classList.toggle("active", isAdmin);
  updateCharacterSelectorVisibility(panel);

  if (isAdmin) {
    void loadScript();
    editor?.layout();
    scriptSource?.focus();
  } else {
    input?.focus();
  }
}

function getScriptSource(): string {
  return editor?.getValue() ?? scriptSource?.value ?? "";
}

function setScriptSource(value: string): void {
  if (editor) {
    editor.setValue(value);
  }

  if (scriptSource) {
    scriptSource.value = value;
  }
}

async function fetchBehaviorModule(moduleId: string, refresh = false): Promise<ScriptResponse> {
  if (!refresh) {
    const cached = modulePayloads.get(moduleId);
    if (cached) return cached;
  }

  const response = await fetch(`/admin/behaviors/${encodeURIComponent(moduleId)}`);
  if (!response.ok) throw new Error(`Could not load behavior module ${moduleId}.`);
  const payload = (await response.json()) as ScriptResponse;
  modulePayloads.set(moduleId, payload);
  return payload;
}

async function applyLoadedModuleSource(moduleId: string, payload: ScriptResponse): Promise<void> {
  const model = moduleModels.get(moduleId);
  if (model) {
    model.setValue(payload.source);
  } else {
    setScriptSource(payload.source);
  }

  savedSources.set(moduleId, payload.source);
  renderModuleDetails(payload);
  updateEditorTitle(moduleId);
  setEditorMarkers([]);
}

async function reloadModuleFromServer(moduleId: string): Promise<void> {
  const localSource = getScriptSource();
  const isDirty = localSource !== savedSources.get(moduleId);
  if (isDirty && !window.confirm(`Reload ${moduleId} from the server? Your unsaved edits will be lost.`)) {
    return;
  }

  try {
    const payload = await fetchBehaviorModule(moduleId, true);
    await applyLoadedModuleSource(moduleId, payload);
    setStatus(`Reloaded ${moduleId} (revision ${payload.sourceRevision}).`);
  } catch {
    setStatus(`Could not reload ${moduleId} from the server.`, true);
  }
}

function showSourceConflict(conflict: ScriptConflictResponse, pendingAction: BehaviorAction): void {
  const moduleId = conflict.moduleId;
  const revisionText = `expected revision ${conflict.expectedSourceRevision}, server has ${conflict.currentSourceRevision}`;
  const actions = [
    createStatusButton("Reload from server", "secondary-button", () => {
      void reloadModuleFromServer(moduleId);
    }),
    createStatusButton("Save my version", "", () => {
      void retryBehaviorAction(moduleId, conflict.currentSourceRevision, pendingAction);
    }),
    createStatusButton("Keep editing", "secondary-button", () => {
      setStatus(`${conflict.message} Your unsaved editor contents were preserved. (${revisionText})`, true);
    }),
  ];

  setStatusWithActions(
    `${conflict.message} Your unsaved editor contents were preserved. (${revisionText})`,
    actions,
    true,
  );
}

async function retryBehaviorAction(
  moduleId: string,
  currentSourceRevision: number,
  action: BehaviorAction,
): Promise<void> {
  const existingPayload = modulePayloads.get(moduleId);
  if (!existingPayload) {
    setStatus("The selected behavior module has not finished loading.", true);
    return;
  }

  modulePayloads.set(moduleId, { ...existingPayload, sourceRevision: currentSourceRevision });
  if (action === "save") {
    await saveCurrentScript();
  } else {
    await checkCurrentScript();
  }
}

function ensureModuleModel(payload: ScriptResponse): MonacoModel | null {
  const monaco = window.monaco;
  if (!monaco) return null;
  const existing = moduleModels.get(payload.moduleId);
  if (existing) return existing;

  const model = monaco.editor.createModel(
    payload.source,
    "typescript",
    monaco.Uri.parse(`inmemory://behaviors/${payload.moduleId}.ts`),
  );
  moduleModels.set(payload.moduleId, model);
  savedSources.set(payload.moduleId, payload.source);
  model.onDidChangeContent(() => updateEditorTitle(payload.moduleId));
  return model;
}

async function configureDependencyLibraries(payload: ScriptResponse): Promise<void> {
  const visited = new Set<string>();

  const addDependencies = async (moduleId: string): Promise<void> => {
    if (visited.has(moduleId)) return;
    visited.add(moduleId);
    const dependency = await fetchBehaviorModule(moduleId);
    for (const nestedId of dependency.dependencies) await addDependencies(nestedId);
    ensureModuleModel(dependency);
  };

  for (const dependencyId of payload.dependencies) await addDependencies(dependencyId);
}

function selectedModuleId(): string | null {
  return behaviorModuleSelect?.value || null;
}

async function loadBehaviorModules(): Promise<void> {
  if (!behaviorModuleSelect || behaviorModules.length > 0) return;

  const response = await fetch("/admin/behaviors");
  if (!response.ok) throw new Error("Could not load behavior modules.");

  behaviorModules = (await response.json()) as AdminBehaviorModule[];
  behaviorModuleSelect.replaceChildren();

  behaviorModules.forEach((behaviorModule) => {
    const option = document.createElement("option");
    option.value = behaviorModule.moduleId;
    const dependencyText = behaviorModule.dependencies.length > 0
      ? ` depends on ${behaviorModule.dependencies.join(", ")}`
      : "";
    option.textContent = `${behaviorModule.moduleId}${dependencyText}`;
    behaviorModuleSelect.appendChild(option);
  });
}

function initializeEditor(): Promise<void> {
  if (editorReady) return editorReady;

  editorReady = (async () => {
    if (!editorHost || !scriptSource || !window.require) {
      editorHost?.setAttribute("hidden", "true");
      scriptSource?.removeAttribute("hidden");
      return;
    }

    const declarationsResponse = await fetch("/admin/scripting/game-api.d.ts");
    if (!declarationsResponse.ok) throw new Error("Could not load scripting declarations.");
    const declarations = await declarationsResponse.text();

    window.require.config({ paths: { vs: "https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs" } });
    await new Promise<void>((resolve) => window.require?.(["vs/editor/editor.main"], resolve));
      const monaco = window.monaco;
      if (!monaco) {
        editorHost.setAttribute("hidden", "true");
        scriptSource.removeAttribute("hidden");
        return;
      }

      monaco.languages.typescript.typescriptDefaults.addExtraLib(declarations, "inmemory://model/game-api.d.ts");
      monaco.languages.typescript.typescriptDefaults.setEagerModelSync(true);
      monaco.languages.typescript.typescriptDefaults.setCompilerOptions({
        target: 9,
        strict: true,
        noEmit: true,
        skipLibCheck: true,
      });
      monaco.editor.defineTheme("brokenrealm", {
        base: "vs-dark",
        inherit: true,
        rules: [],
        colors: {
          "editor.background": "#0b110e",
          "editor.foreground": "#e5efe8",
          "editorLineNumber.foreground": "#6f8576",
          "editorCursor.foreground": "#89d18f",
          "editor.selectionBackground": "#244b37",
          "editorGutter.background": "#0b110e",
        },
      });
      monaco.editor.setTheme("brokenrealm");
      editor = monaco.editor.create(editorHost, {
        value: "",
        language: "typescript",
        automaticLayout: true,
        minimap: { enabled: false },
        fontFamily: 'Consolas, "Cascadia Mono", "Segoe UI Mono", monospace',
        fontSize: 14,
        tabSize: 2,
        insertSpaces: true,
        scrollBeyondLastLine: false,
        wordWrap: "on",
      });
  })().catch(() => {
    editorHost?.setAttribute("hidden", "true");
    scriptSource?.removeAttribute("hidden");
  });

  return editorReady;
}

function renderCharacterSelector(session: GameSessionResponse): void {
  if (!characterSelect || !characterLabel) return;

  characterSelect.replaceChildren();

  session.characters.forEach((character) => {
    const option = document.createElement("option");
    option.value = character.id;
    option.textContent = `${character.id} @ ${character.locationId}`;
    characterSelect.append(option);
  });

  characterSelect.value = session.selectedCharacterId;
  currentSession = session;
  updateCharacterSelectorVisibility(activePanel);
}

async function loadSession(): Promise<void> {
  const response = await fetch("/game/session", gameFetchInit);
  if (!response.ok) {
    appendLine("Could not load the current session.", "line error-line");
    return;
  }

  const payload = (await response.json()) as GameSessionResponse;
  renderCharacterSelector(payload);
}

async function selectCharacter(characterId: string): Promise<void> {
  const response = await fetch("/game/session/character", {
    ...gameFetchInit,
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ characterId }),
  });

  if (!response.ok) {
    appendLine("Could not switch characters.", "line error-line");
    return;
  }

  const payload = (await response.json()) as SelectCharacterResponse;
  renderCharacterSelector({
    accountId: "",
    selectedCharacterId: payload.selectedCharacterId,
    characters: payload.characters,
  });
  appendLine(`Now playing as ${payload.selectedCharacterId}.`);
}

async function sendCommand(command: string, selectedCulture: Culture): Promise<void> {
  appendLine(`> ${command}`, "line input-line");

  const response = await fetch("/game/command", {
    ...gameFetchInit,
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ text: command, culture: selectedCulture }),
  });

  if (!response.ok) {
    appendLine("The realm is not responding.", "line error-line");
    return;
  }

  const payload = (await response.json()) as CommandResponse;
  payload.lines.forEach((line) => appendLine(line));
}

async function loadScript(): Promise<void> {
  if (!scriptSource) return;
  await initializeEditor();

  try {
    await loadBehaviorModules();
  } catch {
    setStatus("Could not load behavior modules.", true);
    return;
  }

  const moduleId = selectedModuleId();
  if (!moduleId) {
    setStatus("No editable behavior module is available.", true);
    return;
  }

  let payload: ScriptResponse;
  try {
    payload = await fetchBehaviorModule(moduleId);
    await configureDependencyLibraries(payload);
  } catch {
    setStatus("Could not load behavior module or its dependencies.", true);
    return;
  }

  if (editor && window.monaco) {
    const model = ensureModuleModel(payload);
    if (!model) return;
    editor.setModel(model);
  } else {
    setScriptSource(payload.source);
  }
  setEditorMarkers([]);
  updateEditorTitle(moduleId);
  activeModuleId = moduleId;
  renderModuleDetails(payload);
  const modules = payload.affectedModules.join(", ") || moduleId;
  const objects = payload.affectedObjects.join(", ") || "none";
  setStatus(`Loaded. Saving affects modules: ${modules}; objects: ${objects}.`);
}

async function checkCurrentScript(): Promise<void> {
  if (!scriptSource) return;
  await initializeEditor();

  const moduleId = selectedModuleId();
  if (!moduleId) {
    setStatus("No editable behavior module is selected.", true);
    return;
  }

  checkScript?.setAttribute("disabled", "true");
  saveScript?.setAttribute("disabled", "true");
  setStatus("Checking without activation...");
  setEditorMarkers([]);
  const expectedSourceRevision = modulePayloads.get(moduleId)?.sourceRevision;
  if (expectedSourceRevision === undefined) {
    setStatus("The selected behavior module has not finished loading.", true);
    return;
  }

  const response = await fetch(`/admin/behaviors/${encodeURIComponent(moduleId)}/validate`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ source: getScriptSource(), expectedSourceRevision }),
  });

  if (!response.ok) {
    try {
      const payload = (await response.json()) as ScriptErrorResponse & Partial<ScriptConflictResponse>;
      if (response.status === 409 && payload.message && payload.moduleId) {
        showSourceConflict(payload as ScriptConflictResponse, "check");
        return;
      }
      if (payload.diagnostics && payload.diagnostics.length > 0) {
        await setDiagnostics(payload.diagnostics);
        return;
      }
    } catch {
      // Use the generic status for non-JSON failures.
    }

    setStatus("Could not check script.", true);
    return;
  }

  const payload = (await response.json()) as ScriptResponse;
  setStatus(
    `Valid. Nothing was activated. Saving would update modules: ${payload.affectedModules.join(", ")}; objects: ${payload.affectedObjects.join(", ") || "none"}.`,
  );
}

async function saveCurrentScript(): Promise<void> {
  if (!scriptSource) return;
  await initializeEditor();

  const moduleId = selectedModuleId();
  if (!moduleId) {
    setStatus("No editable behavior module is selected.", true);
    return;
  }

  saveScript?.setAttribute("disabled", "true");
  checkScript?.setAttribute("disabled", "true");
  setStatus("Compiling...");
  setEditorMarkers([]);
  const expectedSourceRevision = modulePayloads.get(moduleId)?.sourceRevision;
  if (expectedSourceRevision === undefined) {
    setStatus("The selected behavior module has not finished loading.", true);
    return;
  }

  const response = await fetch(`/admin/behaviors/${encodeURIComponent(moduleId)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ source: getScriptSource(), expectedSourceRevision }),
  });

  if (!response.ok) {
    let message = "Could not save script.";

    try {
      const payload = (await response.json()) as ScriptErrorResponse & Partial<ScriptConflictResponse>;
      if (response.status === 409 && payload.message && payload.moduleId) {
        showSourceConflict(payload as ScriptConflictResponse, "save");
        return;
      }
      if (payload.diagnostics && payload.diagnostics.length > 0) {
        await setDiagnostics(payload.diagnostics);
        return;
      }
    } catch {
      // Keep the generic message when the server response is not JSON.
    }

    setStatus(message, true);
    return;
  }

  const payload = (await response.json()) as ScriptResponse;
  const source = getScriptSource();
  savedSources.set(moduleId, source);
  const existingPayload = modulePayloads.get(moduleId);
  if (existingPayload) {
    const updatedPayload = {
      ...existingPayload,
      source,
      sourceRevision: payload.sourceRevision,
      affectedModules: payload.affectedModules,
      affectedObjects: payload.affectedObjects,
    };
    modulePayloads.set(moduleId, updatedPayload);
    renderModuleDetails(updatedPayload);
  }
  updateEditorTitle(moduleId);
  setStatus(
    `Saved and compiled atomically. Updated modules: ${payload.affectedModules.join(", ")}; objects: ${payload.affectedObjects.join(", ") || "none"}.`,
  );
  saveScript?.removeAttribute("disabled");
}

characterSelect?.addEventListener("change", () => {
  const characterId = characterSelect.value;
  if (!characterId) return;
  void selectCharacter(characterId);
});

form?.addEventListener("submit", async (event) => {
  event.preventDefault();

  const command = input?.value.trim() ?? "";
  const selectedCulture = (culture?.value === "de" ? "de" : "en") as Culture;

  if (!command) return;
  if (input) input.value = "";

  try {
    await sendCommand(command, selectedCulture);
  } catch {
    appendLine("The realm is not responding.", "line error-line");
  } finally {
    input?.focus();
  }
});

playerTab?.addEventListener("click", () => {
  if (canLeaveModule(activeModuleId)) showPanel("player");
});
adminTab?.addEventListener("click", () => showPanel("admin"));
reloadScript?.addEventListener("click", () => {
  const moduleId = selectedModuleId();
  if (!moduleId) {
    setStatus("No editable behavior module is selected.", true);
    return;
  }
  void reloadModuleFromServer(moduleId);
});
checkScript?.addEventListener("click", () => {
  void checkCurrentScript().finally(() => {
    checkScript.removeAttribute("disabled");
    saveScript?.removeAttribute("disabled");
  });
});
saveScript?.addEventListener("click", () => {
  void saveCurrentScript().finally(() => {
    saveScript.removeAttribute("disabled");
    checkScript?.removeAttribute("disabled");
  });
});
behaviorModuleSelect?.addEventListener("change", () => {
  if (!canLeaveModule(activeModuleId)) {
    if (activeModuleId) behaviorModuleSelect.value = activeModuleId;
    return;
  }
  void loadScript();
});

window.addEventListener("beforeunload", (event) => {
  if (!hasUnsavedChanges()) return;
  event.preventDefault();
  event.returnValue = "";
});

appendLine("BrokenRealm awaits.");
void loadSession();
