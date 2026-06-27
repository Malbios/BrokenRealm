"use strict";
const form = document.querySelector("#command-form");
const input = document.querySelector("#command");
const culture = document.querySelector("#culture");
const log = document.querySelector("#log");
const playerTab = document.querySelector("#player-tab");
const adminTab = document.querySelector("#admin-tab");
const playerPanel = document.querySelector("#player-panel");
const adminPanel = document.querySelector("#admin-panel");
const scriptSource = document.querySelector("#script-source");
const saveScript = document.querySelector("#save-script");
const scriptStatus = document.querySelector("#script-status");
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
    scriptStatus.textContent = text;
    scriptStatus.classList.toggle("error-line", isError);
}
function showPanel(panel) {
    const isAdmin = panel === "admin";
    playerTab?.classList.toggle("active", !isAdmin);
    adminTab?.classList.toggle("active", isAdmin);
    playerPanel?.classList.toggle("active", !isAdmin);
    adminPanel?.classList.toggle("active", isAdmin);
    if (isAdmin) {
        void loadScript();
        scriptSource?.focus();
    }
    else {
        input?.focus();
    }
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
    const response = await fetch("/admin/objects/forest/verbs/gather");
    if (!response.ok) {
        setStatus("Could not load script.", true);
        return;
    }
    const payload = (await response.json());
    scriptSource.value = payload.source;
    setStatus("Loaded.");
}
async function saveCurrentScript() {
    if (!scriptSource)
        return;
    const response = await fetch("/admin/objects/forest/verbs/gather", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ source: scriptSource.value }),
    });
    if (!response.ok) {
        let message = "Could not save script.";
        try {
            const payload = (await response.json());
            if (payload.diagnostics && payload.diagnostics.length > 0) {
                message = payload.diagnostics.join("\n");
            }
        }
        catch {
            // Keep the generic message when the server response is not JSON.
        }
        setStatus(message, true);
        return;
    }
    setStatus("Saved and compiled. The next gather command will use this script.");
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
    void saveCurrentScript();
});
appendLine("BrokenRealm awaits.");
