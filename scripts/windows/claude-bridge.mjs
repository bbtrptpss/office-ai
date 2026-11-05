// Lets the Office AI API (in Docker) ask the Claude Code CLI that is signed in on this Windows machine.
// Claude Code runs with every tool and MCP server disabled, in an empty folder, one question at a time; the API carries out the <action> blocks
// (dashboard chat) or <tool_call> blocks (AI employees on the "claude-code" provider) it writes.
// POST /run is the dashboard terminal: one PowerShell command in the project folder (the API sends it for the CEO only).
// It answers 403 unless CLAUDE_BRIDGE_TERMINAL=true is in .env when the bridge starts.
//   node scripts/windows/claude-bridge.mjs        (start-office-ai.ps1 starts it; token from .env CLAUDE_BRIDGE_TOKEN)
import { createServer } from "node:http";
import { spawn } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { randomUUID, timingSafeEqual } from "node:crypto";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const env = Object.fromEntries(
  readFileSync(join(root, ".env"), "utf8").split(/\r?\n/).filter((l) => /^[A-Z0-9_]+=/.test(l)).map((l) => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1)]),
);
const TOKEN = env.CLAUDE_BRIDGE_TOKEN ?? "";
const PORT = Number(env.CLAUDE_BRIDGE_PORT ?? 8765);
// The terminal is a full shell on this PC, so it stays off unless .env on this PC says otherwise. The API cannot turn it on.
const TERMINAL_ENABLED = env.CLAUDE_BRIDGE_TERMINAL === "true";
const TIMEOUT_MS = 180_000;
if (TOKEN.length < 24) {
  console.error("CLAUDE_BRIDGE_TOKEN in .env is missing or too short.");
  process.exit(1);
}

// An empty working folder, so Claude Code sees no project files, CLAUDE.md or settings.
const work = join(tmpdir(), "officeai-claude-chat");
mkdirSync(work, { recursive: true });

const sameToken = (given) => {
  const a = Buffer.from(String(given ?? "")), b = Buffer.from(TOKEN);
  return a.length === b.length && timingSafeEqual(a, b);
};

let queue = Promise.resolve();
const serialized = (job) => (queue = queue.then(job, job));

// Claude Code model aliases or ids the API may ask for (AI employees on the "claude-code" provider); validated before it reaches the shell.
const MODEL = /^[a-z0-9][a-z0-9.-]{0,60}$/i;

function ask({ prompt, system, sessionId, model }) {
  return new Promise((resolve, reject) => {
    const systemFile = join(work, `system-${randomUUID()}.txt`);
    writeFileSync(systemFile, system ?? "", "utf8");
    const args = ["-p", "--output-format", "json", "--tools", '""', "--strict-mcp-config", "--system-prompt-file", systemFile];
    if (sessionId) args.push("--resume", sessionId);
    if (model) args.push("--model", model);
    // claude is an npm shim (.cmd) on Windows, so it needs a shell; every argument here is fixed or validated.
    const child = spawn("claude", args, { cwd: work, shell: true, windowsHide: true });
    let out = "", err = "";
    const timer = setTimeout(() => child.kill(), TIMEOUT_MS);
    child.stdout.on("data", (d) => (out += d));
    child.stderr.on("data", (d) => (err += d));
    child.on("error", reject);
    child.on("close", (code) => {
      clearTimeout(timer);
      rmSync(systemFile, { force: true });
      try {
        const json = JSON.parse(out.trim().split(/\r?\n/).pop());
        if (json.is_error) return reject(new Error(json.result || "Claude returned an error."));
        const u = json.usage ?? {};
        resolve({
          reply: json.result ?? "", sessionId: json.session_id, costUsd: json.total_cost_usd ?? 0,
          inputTokens: u.input_tokens ?? 0, cacheReadTokens: u.cache_read_input_tokens ?? 0, cacheWriteTokens: u.cache_creation_input_tokens ?? 0,
          outputTokens: u.output_tokens ?? 0,
        });
      } catch {
        reject(new Error((err || out || `claude exited with ${code}`).slice(0, 500)));
      }
    });
    child.stdin.end(prompt, "utf8");
  });
}

const RUN_TIMEOUT_MS = 10 * 60_000;
const MAX_OUTPUT = 20_000;

const CLAUDE_USAGE = [
  "claude <ข้อความ>   ถาม Claude Code ในโฟลเดอร์โปรเจกต์ (คุยต่อเนื่องจนกว่าจะพิมพ์ claude /new) แก้ไฟล์ได้เลยโดยไม่ต้องอนุมัติ",
  "claude /new        เริ่มบทสนทนาใหม่",
  "claude -<flag> ... ส่งต่อให้ claude.cmd ตามที่พิมพ์ เช่น claude --version",
  "(ในหน้าเว็บใช้ claude แบบโต้ตอบเต็มจอไม่ได้ จึงใช้แบบถามทีละข้อความแทน)",
].join("\n");

// The terminal's own Claude Code conversation (the CEO is the only terminal user), kept until "claude /new" or a bridge restart.
let terminalSession = null;

// "claude" typed in the terminal: the interactive TUI cannot work here, and PowerShell picks claude.ps1, which the execution policy blocks.
function runClaude(command) {
  const rest = command.slice("claude".length).trim();
  if (!rest) return Promise.resolve({ output: CLAUDE_USAGE, exitCode: 0, timedOut: false, ms: 0 });
  if (rest === "/new") {
    terminalSession = null;
    return Promise.resolve({ output: "เริ่มบทสนทนา Claude ใหม่แล้ว", exitCode: 0, timedOut: false, ms: 0 });
  }
  if (rest.startsWith("-")) return run({ command: "claude.cmd " + rest });
  return new Promise((resolve) => {
    // acceptEdits: file edits in the project go through without a prompt (-p has no one to approve them); other tools still need approval, so they are denied.
    const args = ["-p", "--output-format", "json", "--permission-mode", "acceptEdits"];
    if (terminalSession) args.push("--resume", terminalSession);
    // claude is an npm shim (.cmd) on Windows, so it needs a shell; the arguments are fixed and the message goes on stdin.
    const child = spawn("claude", args, { cwd: root, shell: true, windowsHide: true });
    let out = "", err = "";
    const started = Date.now();
    let timedOut = false;
    const timer = setTimeout(() => { timedOut = true; child.kill(); }, RUN_TIMEOUT_MS);
    child.stdout.on("data", (d) => (out += d));
    child.stderr.on("data", (d) => (err += d));
    child.on("error", (e) => { clearTimeout(timer); resolve({ output: e.message, exitCode: -1, timedOut: false, ms: Date.now() - started }); });
    child.on("close", (code) => {
      clearTimeout(timer);
      const ms = Date.now() - started;
      try {
        const json = JSON.parse(out.trim().split(/\r?\n/).pop());
        if (json.session_id) terminalSession = json.session_id;
        resolve({ output: String(json.result ?? "").slice(-MAX_OUTPUT), exitCode: json.is_error ? 1 : 0, timedOut, ms });
      } catch {
        terminalSession = null; // a broken or vanished conversation: the next message starts a new one
        resolve({ output: (err || out || `claude exited with ${code}`).slice(-MAX_OUTPUT).replace(/\s+$/, ""), exitCode: code ?? -1, timedOut, ms });
      }
    });
    child.stdin.end(rest, "utf8");
  });
}

// The dashboard terminal (the API allows it for the CEO only): one PowerShell command in the project folder, output and exit code back.
function run({ command }) {
  if (/^claude(\s|$)/i.test(command.trim())) return runClaude(command.trim());
  return new Promise((resolve) => {
    // -EncodedCommand (UTF-16LE base64): needs no quoting, and keeps Thai text intact, which PowerShell reading stdin does not.
    // Errors are merged into the output as plain text (otherwise PowerShell writes them as CLIXML); the exit code is the last program's.
    // Lines are written to stdout as UTF-8 bytes directly: with a (hidden) console attached, PowerShell's own writer uses the ANSI code page.
    const script = [
      "[Console]::OutputEncoding = [Text.Encoding]::UTF8", "$ProgressPreference = 'SilentlyContinue'", "$global:LASTEXITCODE = 0",
      "$__out = [Console]::OpenStandardOutput(); $__utf8 = New-Object Text.UTF8Encoding $false",
      "& {", command, "} 2>&1 | ForEach-Object { $__b = $__utf8.GetBytes(\"$_`n\"); $__out.Write($__b, 0, $__b.Length) }",
      "$__out.Flush()", "exit $global:LASTEXITCODE",
    ].join("\n");
    const encoded = Buffer.from(script, "utf16le").toString("base64");
    const child = spawn("powershell.exe", ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], { cwd: root, windowsHide: true });
    let out = "";
    const add = (d) => { if (out.length < MAX_OUTPUT * 2) out += d; };
    const started = Date.now();
    let timedOut = false;
    const timer = setTimeout(() => { timedOut = true; child.kill(); }, RUN_TIMEOUT_MS);
    child.stdout.on("data", add);
    child.stderr.on("data", add);
    child.on("error", (e) => { clearTimeout(timer); resolve({ output: e.message, exitCode: -1, timedOut: false, ms: Date.now() - started }); });
    child.on("close", (code) => {
      clearTimeout(timer);
      const clipped = out.length > MAX_OUTPUT ? "…" + out.slice(-MAX_OUTPUT) : out;
      resolve({ output: clipped.replace(/\s+$/, ""), exitCode: code ?? -1, timedOut, ms: Date.now() - started });
    });
    child.stdin.end();
  });
}

const server = createServer((req, res) => {
  const send = (status, body) => { res.writeHead(status, { "content-type": "application/json" }); res.end(JSON.stringify(body)); };
  if (req.method === "GET" && req.url === "/health") return send(200, { ok: true });
  if (req.method === "POST" && req.url === "/run") {
    if (!sameToken(req.headers["x-bridge-token"])) return send(401, { error: "unauthorized" });
    if (!TERMINAL_ENABLED) return send(403, { error: "terminal_disabled" });
    let body = "";
    req.on("data", (d) => { body += d; if (body.length > 50_000) req.destroy(); });
    req.on("end", async () => {
      let input;
      try { input = JSON.parse(body); } catch { return send(400, { error: "invalid json" }); }
      if (typeof input.command !== "string" || !input.command.trim()) return send(400, { error: "command is required" });
      send(200, await serialized(() => run(input)));
    });
    return;
  }
  if (req.method !== "POST" || req.url !== "/ask") return send(404, { error: "not found" });
  if (!sameToken(req.headers["x-bridge-token"])) return send(401, { error: "unauthorized" });
  let body = "";
  req.on("data", (d) => { body += d; if (body.length > 200_000) req.destroy(); });
  req.on("end", async () => {
    let input;
    try { input = JSON.parse(body); } catch { return send(400, { error: "invalid json" }); }
    if (typeof input.prompt !== "string" || !input.prompt.trim()) return send(400, { error: "prompt is required" });
    if (input.sessionId && !/^[0-9a-f-]{36}$/i.test(input.sessionId)) return send(400, { error: "invalid sessionId" });
    if (input.model != null && (typeof input.model !== "string" || !MODEL.test(input.model))) return send(400, { error: "invalid model" });
    try {
      send(200, await serialized(() => ask(input)));
    } catch (e) {
      send(502, { error: e instanceof Error ? e.message : String(e) });
    }
  });
});

// Docker Desktop forwards host.docker.internal to this machine's loopback, so nothing outside the PC can reach the bridge.
server.listen(PORT, "127.0.0.1", () => console.log(`Claude bridge on http://127.0.0.1:${PORT}`));
