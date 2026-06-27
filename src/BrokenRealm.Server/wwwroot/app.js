"use strict";
const form = document.querySelector("#command-form");
const input = document.querySelector("#command");
const culture = document.querySelector("#culture");
const log = document.querySelector("#log");
const playerTab = document.querySelector("#player-tab");
const adminTab = document.querySelector("#admin-tab");
const playerPanel = document.querySelector("#player-panel");
const adminPanel = document.querySelector("#admin-panel");
const editorHost = document.querySelector("#script-editor");
const scriptSource = document.querySelector("#script-source");
const saveScript = document.querySelector("#save-script");
const scriptStatus = document.querySelector("#script-status");
const behaviorModuleSelect = document.querySelector("#behavior-module");
const verbTitle = document.querySelector("#verb-title");
const moduleDetails = document.querySelector("#module-details");
let editor = null;
let editorReady = null;
let behaviorModules = [];
const moduleModels = new Map();
const modulePayloads = new Map();
const savedSources = new Map();
let dependencyLibraries = [];
function appendLine(text, className = "line") {
    if (!log)
        return;
    const line = document.createElement("div");
    line.className = className;
    line.textContent = text;
    log.appendChild(line);
    log.scrollTop = log.scrollHeight;
}
function setStatus(text, isError = false) {
    if (!scriptStatus)
        return;
    scriptStatus.replaceChildren();
    scriptStatus.textContent = text;
    scriptStatus.classList.toggle("error-line", isError);
}
function setEditorMarkers(diagnostics) {
    const model = editor?.getModel();
    if (!model || !window.monaco)
        return;
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
function setDiagnostics(diagnostics) {
    if (!scriptStatus)
        return;
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
function renderModuleDetails(payload) {
    if (!moduleDetails)
        return;
    moduleDetails.replaceChildren();
    const details = [
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
function updateEditorTitle(moduleId) {
    if (!verbTitle)
        return;
    const model = moduleModels.get(moduleId);
    const dirty = model && model.getValue() !== savedSources.get(moduleId);
    verbTitle.textContent = `${moduleId}${dirty ? " *" : ""}`;
}
function showPanel(panel) {
    const isAdmin = panel === "admin";
    playerTab?.classList.toggle("active", !isAdmin);
    adminTab?.classList.toggle("active", isAdmin);
    playerPanel?.classList.toggle("active", !isAdmin);
    adminPanel?.classList.toggle("active", isAdmin);
    if (isAdmin) {
        void loadScript();
        editor?.layout();
        scriptSource?.focus();
    }
    else {
        input?.focus();
    }
}
function getScriptSource() {
    return editor?.getValue() ?? scriptSource?.value ?? "";
}
function setScriptSource(value) {
    if (editor) {
        editor.setValue(value);
    }
    if (scriptSource) {
        scriptSource.value = value;
    }
}
async function fetchBehaviorModule(moduleId, refresh = false) {
    if (!refresh) {
        const cached = modulePayloads.get(moduleId);
        if (cached)
            return cached;
    }
    const response = await fetch(`/admin/behaviors/${encodeURIComponent(moduleId)}`);
    if (!response.ok)
        throw new Error(`Could not load behavior module ${moduleId}.`);
    const payload = (await response.json());
    modulePayloads.set(moduleId, payload);
    return payload;
}
async function configureDependencyLibraries(payload) {
    const monaco = window.monaco;
    if (!monaco)
        return;
    dependencyLibraries.forEach((library) => library.dispose());
    dependencyLibraries = [];
    const visited = new Set();
    const addDependencies = async (moduleId) => {
        if (visited.has(moduleId))
            return;
        visited.add(moduleId);
        const dependency = await fetchBehaviorModule(moduleId);
        for (const nestedId of dependency.dependencies)
            await addDependencies(nestedId);
        dependencyLibraries.push(monaco.languages.typescript.typescriptDefaults.addExtraLib(dependency.source, `inmemory://dependencies/${dependency.moduleId}.ts`));
    };
    for (const dependencyId of payload.dependencies)
        await addDependencies(dependencyId);
}
function selectedModuleId() {
    return behaviorModuleSelect?.value || null;
}
async function loadBehaviorModules() {
    if (!behaviorModuleSelect || behaviorModules.length > 0)
        return;
    const response = await fetch("/admin/behaviors");
    if (!response.ok)
        throw new Error("Could not load behavior modules.");
    behaviorModules = (await response.json());
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
function initializeEditor() {
    if (editorReady)
        return editorReady;
    editorReady = (async () => {
        if (!editorHost || !scriptSource || !window.require) {
            editorHost?.setAttribute("hidden", "true");
            scriptSource?.removeAttribute("hidden");
            return;
        }
        const declarationsResponse = await fetch("/admin/scripting/game-api.d.ts");
        if (!declarationsResponse.ok)
            throw new Error("Could not load scripting declarations.");
        const declarations = await declarationsResponse.text();
        window.require.config({ paths: { vs: "https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs" } });
        await new Promise((resolve) => window.require?.(["vs/editor/editor.main"], resolve));
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
async function sendCommand(command, selectedCulture) {
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
    const payload = (await response.json());
    payload.lines.forEach((line) => appendLine(line));
}
async function loadScript() {
    if (!scriptSource)
        return;
    await initializeEditor();
    try {
        await loadBehaviorModules();
    }
    catch {
        setStatus("Could not load behavior modules.", true);
        return;
    }
    const moduleId = selectedModuleId();
    if (!moduleId) {
        setStatus("No editable behavior module is available.", true);
        return;
    }
    let payload;
    try {
        payload = await fetchBehaviorModule(moduleId);
        await configureDependencyLibraries(payload);
    }
    catch {
        setStatus("Could not load behavior module or its dependencies.", true);
        return;
    }
    if (editor && window.monaco) {
        let model = moduleModels.get(moduleId);
        if (!model) {
            model = window.monaco.editor.createModel(payload.source, "typescript", window.monaco.Uri.parse(`inmemory://behaviors/${moduleId}.ts`));
            moduleModels.set(moduleId, model);
            savedSources.set(moduleId, payload.source);
            model.onDidChangeContent(() => updateEditorTitle(moduleId));
        }
        editor.setModel(model);
    }
    else {
        setScriptSource(payload.source);
    }
    setEditorMarkers([]);
    updateEditorTitle(moduleId);
    renderModuleDetails(payload);
    const modules = payload.affectedModules.join(", ") || moduleId;
    const objects = payload.affectedObjects.join(", ") || "none";
    setStatus(`Loaded. Saving affects modules: ${modules}; objects: ${objects}.`);
}
async function saveCurrentScript() {
    if (!scriptSource)
        return;
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
            const payload = (await response.json());
            if (payload.diagnostics && payload.diagnostics.length > 0) {
                setDiagnostics(payload.diagnostics);
                return;
            }
        }
        catch {
            // Keep the generic message when the server response is not JSON.
        }
        setStatus(message, true);
        return;
    }
    const payload = (await response.json());
    const source = getScriptSource();
    savedSources.set(moduleId, source);
    const existingPayload = modulePayloads.get(moduleId);
    if (existingPayload) {
        const updatedPayload = {
            ...existingPayload,
            source,
            affectedModules: payload.affectedModules,
            affectedObjects: payload.affectedObjects,
        };
        modulePayloads.set(moduleId, updatedPayload);
        renderModuleDetails(updatedPayload);
    }
    updateEditorTitle(moduleId);
    setStatus(`Saved and compiled atomically. Updated modules: ${payload.affectedModules.join(", ")}; objects: ${payload.affectedObjects.join(", ") || "none"}.`);
    saveScript?.removeAttribute("disabled");
}
form?.addEventListener("submit", async (event) => {
    event.preventDefault();
    const command = input?.value.trim() ?? "";
    const selectedCulture = (culture?.value === "de" ? "de" : "en");
    if (!command)
        return;
    if (input)
        input.value = "";
    try {
        await sendCommand(command, selectedCulture);
    }
    catch {
        appendLine("The realm is not responding.", "line error-line");
    }
    finally {
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
