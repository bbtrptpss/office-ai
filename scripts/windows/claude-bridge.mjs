// Lets the Office AI API (in Docker) ask the Claude Code CLI that is signed in on this Windows machine.
// Talk only: Claude runs with every tool and MCP server disabled, in an empty folder, one question at a time.
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

function ask({ prompt, system, sessionId }) {
  return new Promise((resolve, reject) => {
    const systemFile = join(work, `system-${randomUUID()}.txt`);
    writeFileSync(systemFile, system ?? "", "utf8");
    const args = ["-p", "--output-format", "json", "--tools", '""', "--strict-mcp-config", "--system-prompt-file", systemFile];
    if (sessionId) args.push("--resume", sessionId);
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
        resolve({ reply: json.result ?? "", sessionId: json.session_id, costUsd: json.total_cost_usd ?? 0 });
      } catch {
        reject(new Error((err || out || `claude exited with ${code}`).slice(0, 500)));
      }
    });
    child.stdin.end(prompt, "utf8");
  });
}

const server = createServer((req, res) => {
  const send = (status, body) => { res.writeHead(status, { "content-type": "application/json" }); res.end(JSON.stringify(body)); };
  if (req.method === "GET" && req.url === "/health") return send(200, { ok: true });
  if (req.method !== "POST" || req.url !== "/ask") return send(404, { error: "not found" });
  if (!sameToken(req.headers["x-bridge-token"])) return send(401, { error: "unauthorized" });
  let body = "";
  req.on("data", (d) => { body += d; if (body.length > 200_000) req.destroy(); });
  req.on("end", async () => {
    let input;
    try { input = JSON.parse(body); } catch { return send(400, { error: "invalid json" }); }
    if (typeof input.prompt !== "string" || !input.prompt.trim()) return send(400, { error: "prompt is required" });
    if (input.sessionId && !/^[0-9a-f-]{36}$/i.test(input.sessionId)) return send(400, { error: "invalid sessionId" });
    try {
      send(200, await serialized(() => ask(input)));
    } catch (e) {
      send(502, { error: e instanceof Error ? e.message : String(e) });
    }
  });
});

// Docker Desktop forwards host.docker.internal to this machine's loopback, so nothing outside the PC can reach the bridge.
server.listen(PORT, "127.0.0.1", () => console.log(`Claude bridge on http://127.0.0.1:${PORT}`));
