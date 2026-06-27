type Culture = "en" | "de";
type Panel = "player" | "admin";

type MonacoEditor = {
  getValue(): string;
  setValue(value: string): void;
  layout(): void;
  getModel(): unknown;
};

type MonacoApi = {
  editor: {
    create(element: HTMLElement, options: Record<string, unknown>): MonacoEditor;
    defineTheme(name: string, theme: Record<string, unknown>): void;
    setTheme(name: string): void;
    setModelMarkers(model: unknown, owner: string, markers: MonacoMarker[]): void;
  };
  languages: {
    typescript: {
      typescriptDefaults: {
        addExtraLib(source: string, path?: string): void;
        setCompilerOptions(options: Record<string, unknown>): void;
      };
    };
  };
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

type ScriptResponse = {
  source: string;
  affectedModules: string[];
  affectedObjects: string[];
};

type ScriptErrorResponse = {
  diagnostics?: CompilerDiagnostic[];
};

type CompilerDiagnostic = {
  message: string;
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
const log = document.querySelector<HTMLDivElement>("#log");
const playerTab = document.querySelector<HTMLButtonElement>("#player-tab");
const adminTab = document.querySelector<HTMLButtonElement>("#admin-tab");
const playerPanel = document.querySelector<HTMLDivElement>("#player-panel");
const adminPanel = document.querySelector<HTMLDivElement>("#admin-panel");
const editorHost = document.querySelector<HTMLDivElement>("#script-editor");
const scriptSource = document.querySelector<HTMLTextAreaElement>("#script-source");
const saveScript = document.querySelector<HTMLButtonElement>("#save-script");
const scriptStatus = document.querySelector<HTMLDivElement>("#script-status");
const behaviorModuleSelect = document.querySelector<HTMLSelectElement>("#behavior-module");
const verbTitle = document.querySelector<HTMLHeadingElement>("#verb-title");
let editor: MonacoEditor | null = null;
let editorReady: Promise<void> | null = null;
let behaviorModules: AdminBehaviorModule[] = [];

const gameApiTypes = `declare type ScriptEffect =
  | { type: "addInventory"; itemId: "wood"; amount: number }
  | { type: "movePlayer"; destinationId: string }
  | { type: "message"; key: string; args?: Record<string, unknown> };

declare type ObjectId = string;
declare type GameValue = null | string | number | boolean | GameValue[] | { [key: string]: GameValue };

declare interface VerbContext {
  args: Record<string, string>;
  this: {
    id: string;
    name: string;
    descriptionKey: string;
    tags: string[];
    properties: Record<string, GameValue>;
    references: Record<string, string>;
  };
  actor: {
    inventory: Record<string, number>;
  };
}

declare interface VerbResult {
  effects: ScriptEffect[];
}

declare interface Gatherable {
  gather(context: VerbContext): VerbResult;
}

declare function execute(context: VerbContext): VerbResult;
`;

function appendLine(text: string, className = "line"): void {
  if (!log) return;

  const line = document.createElement("div");
  line.className = className;
  line.textContent = text;
  log.appendChild(line);
  log.scrollTop = log.scrollHeight;
}

function setStatus(text: string, isError = false): void {
  if (!scriptStatus) return;
  scriptStatus.replaceChildren();
  scriptStatus.textContent = text;
  scriptStatus.classList.toggle("error-line", isError);
}

function setEditorMarkers(diagnostics: CompilerDiagnostic[]): void {
  const model = editor?.getModel();
  if (!model || !window.monaco) return;

  const markers = diagnostics
    .filter((diagnostic) => diagnostic.line > 0 && diagnostic.column > 0)
    .map((diagnostic) => ({
      severity: 8,
      message: diagnostic.message,
      startLineNumber: diagnostic.line,
      startColumn: diagnostic.column,
      endLineNumber: diagnostic.line,
      endColumn: diagnostic.column + 1,
    }));

  window.monaco.editor.setModelMarkers(model, "brokenrealm", markers);
}

function setDiagnostics(diagnostics: CompilerDiagnostic[]): void {
  if (!scriptStatus) return;

  setEditorMarkers(diagnostics);

  scriptStatus.replaceChildren();
  scriptStatus.classList.add("error-line");

  const list = document.createElement("ul");
  list.className = "diagnostics";

  diagnostics.forEach((diagnostic) => {
    const item = document.createElement("li");
    const location = diagnostic.line > 0 ? `Line ${diagnostic.line}, column ${diagnostic.column}: ` : "";
    item.textContent = `${location}${diagnostic.message}`;
    list.appendChild(item);
  });

  scriptStatus.appendChild(list);
}

function showPanel(panel: Panel): void {
  const isAdmin = panel === "admin";
  playerTab?.classList.toggle("active", !isAdmin);
  adminTab?.classList.toggle("active", isAdmin);
  playerPanel?.classList.toggle("active", !isAdmin);
  adminPanel?.classList.toggle("active", isAdmin);

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

  editorReady = new Promise((resolve) => {
    if (!editorHost || !scriptSource || !window.require) {
      editorHost?.setAttribute("hidden", "true");
      scriptSource?.removeAttribute("hidden");
      resolve();
      return;
    }

    window.require.config({ paths: { vs: "https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs" } });
    window.require(["vs/editor/editor.main"], () => {
      const monaco = window.monaco;
      if (!monaco) {
        editorHost.setAttribute("hidden", "true");
        scriptSource.removeAttribute("hidden");
        resolve();
        return;
      }

      monaco.languages.typescript.typescriptDefaults.addExtraLib(gameApiTypes, "game-api.d.ts");
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
        value: scriptSource.value,
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
      resolve();
    });
  });

  return editorReady;
}

async function sendCommand(command: string, selectedCulture: Culture): Promise<void> {
  appendLine(`> ${command}`, "line input-line");

  const response = await fetch("/game/command", {
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

  const response = await fetch(`/admin/behaviors/${encodeURIComponent(moduleId)}`);
  if (!response.ok) {
    setStatus("Could not load script.", true);
    return;
  }

  const payload = (await response.json()) as ScriptResponse;
  setScriptSource(payload.source);
  setEditorMarkers([]);
  if (verbTitle) verbTitle.textContent = moduleId;
  const modules = payload.affectedModules.join(", ") || moduleId;
  const objects = payload.affectedObjects.join(", ") || "none";
  setStatus(`Loaded. Saving affects modules: ${modules}; objects: ${objects}.`);
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
  setStatus("Compiling...");
  setEditorMarkers([]);

  const response = await fetch(`/admin/behaviors/${encodeURIComponent(moduleId)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ source: getScriptSource() }),
  });

  if (!response.ok) {
    let message = "Could not save script.";

    try {
      const payload = (await response.json()) as ScriptErrorResponse;
      if (payload.diagnostics && payload.diagnostics.length > 0) {
        setDiagnostics(payload.diagnostics);
        return;
      }
    } catch {
      // Keep the generic message when the server response is not JSON.
    }

    setStatus(message, true);
    return;
  }

  const payload = (await response.json()) as ScriptResponse;
  setStatus(
    `Saved and compiled atomically. Updated modules: ${payload.affectedModules.join(", ")}; objects: ${payload.affectedObjects.join(", ") || "none"}.`,
  );
  saveScript?.removeAttribute("disabled");
}

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

playerTab?.addEventListener("click", () => showPanel("player"));
adminTab?.addEventListener("click", () => showPanel("admin"));
saveScript?.addEventListener("click", () => {
  void saveCurrentScript().finally(() => saveScript?.removeAttribute("disabled"));
});
behaviorModuleSelect?.addEventListener("change", () => void loadScript());

appendLine("BrokenRealm awaits.");
