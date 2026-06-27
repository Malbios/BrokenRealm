type Culture = "en" | "de";
type Panel = "player" | "admin";

type MonacoEditor = {
  getValue(): string;
  setValue(value: string): void;
  layout(): void;
};

type MonacoApi = {
  editor: {
    create(element: HTMLElement, options: Record<string, unknown>): MonacoEditor;
    defineTheme(name: string, theme: Record<string, unknown>): void;
    setTheme(name: string): void;
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
};

type ScriptErrorResponse = {
  diagnostics?: string[];
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
let editor: MonacoEditor | null = null;
let editorReady: Promise<void> | null = null;

const gameApiTypes = `declare type ScriptEffect =
  | { type: "addInventory"; itemId: "wood"; amount: number }
  | { type: "message"; key: string; args?: Record<string, unknown> };

declare interface VerbContext {
  args: Record<string, string>;
  this: {
    id: string;
    name: string;
    descriptionKey: string;
    tags: string[];
    properties: Record<string, string>;
  };
  actor: {
    inventory: Record<string, number>;
  };
}

declare interface VerbResult {
  effects: ScriptEffect[];
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

function setDiagnostics(diagnostics: string[]): void {
  if (!scriptStatus) return;

  scriptStatus.replaceChildren();
  scriptStatus.classList.add("error-line");

  const list = document.createElement("ul");
  list.className = "diagnostics";

  diagnostics.forEach((diagnostic) => {
    const item = document.createElement("li");
    item.textContent = diagnostic;
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

  const response = await fetch("/admin/objects/forest/verbs/gather");
  if (!response.ok) {
    setStatus("Could not load script.", true);
    return;
  }

  const payload = (await response.json()) as ScriptResponse;
  setScriptSource(payload.source);
  setStatus("Loaded.");
}

async function saveCurrentScript(): Promise<void> {
  if (!scriptSource) return;
  await initializeEditor();

  saveScript?.setAttribute("disabled", "true");
  setStatus("Compiling...");

  const response = await fetch("/admin/objects/forest/verbs/gather", {
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

  setStatus("Saved and compiled. The next gather command will use this script.");
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

appendLine("BrokenRealm awaits.");
