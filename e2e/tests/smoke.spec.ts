import { expect, test, type APIRequestContext, type Browser, type Page } from "@playwright/test";

// Runs against a throwaway stack with the simulated AI provider: it creates data and changes every AI employee's settings.
const API = "http://localhost:5000";
const CEO_EMAIL = process.env.E2E_CEO_EMAIL;
const CEO_PASSWORD = process.env.E2E_CEO_PASSWORD;
const MEMBER_PASSWORD = `Aa1-${crypto.randomUUID()}`;
const write = { "X-Requested-With": "e2e" };

test.beforeAll(() => {
  if (!CEO_EMAIL || !CEO_PASSWORD) throw new Error("Set E2E_CEO_EMAIL and E2E_CEO_PASSWORD for a throwaway stack.");
});

/** Records what must never happen on a page: uncaught errors, server errors and blocked content. */
function watch(page: Page) {
  const problems: string[] = [];
  page.on("pageerror", (e) => problems.push(`page error: ${e.message}`));
  page.on("response", (r) => { if (r.status() >= 500) problems.push(`${r.status()} ${r.request().method()} ${r.url()}`); });
  page.on("console", (m) => { if (/Content Security Policy/i.test(m.text())) problems.push(`CSP: ${m.text()}`); });
  return problems;
}

async function signIn(page: Page, email: string, password: string) {
  await page.addInitScript(() => localStorage.setItem("lang", "en"));
  await page.goto("/login");
  await page.locator('input[type="email"]').fill(email);
  await page.locator('input[type="password"]').fill(password);
  await page.locator('button[type="submit"]').click();
  await page.waitForURL((url) => url.pathname !== "/login");
}

async function json<T>(api: APIRequestContext, path: string): Promise<T> {
  const res = await api.get(API + path);
  expect(res.ok(), `GET ${path} -> ${res.status()}`).toBeTruthy();
  return (await res.json()) as T;
}

async function post<T>(api: APIRequestContext, path: string, data: unknown): Promise<T> {
  const res = await api.post(API + path, { data, headers: write });
  expect(res.ok(), `POST ${path} -> ${res.status()} ${await res.text()}`).toBeTruthy();
  return (res.status() === 204 ? undefined : await res.json()) as T;
}

async function ceoPage(browser: Browser) {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, CEO_EMAIL!, CEO_PASSWORD!);
  return { context, page, api: context.request };
}

test("the web app sends its security headers", async ({ request }) => {
  for (const path of ["/login", "/"]) {
    const res = await request.get(path);
    const headers = res.headers();
    expect(headers["x-frame-options"]).toBe("DENY");
    expect(headers["x-content-type-options"]).toBe("nosniff");
    expect(headers["content-security-policy"]).toContain("frame-ancestors 'none'");
  }
});

test("an unknown address shows the not-found page", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("lang", "en"));
  const res = await page.goto("/no-such-page");
  expect(res?.status()).toBe(404);
  await expect(page.getByText("Page not found")).toBeVisible();
});

test("a wrong password does not sign in", async ({ page }) => {
  await page.goto("/login");
  await page.locator('input[type="email"]').fill(CEO_EMAIL!);
  await page.locator('input[type="password"]').fill("not-the-password-1");
  await page.locator('button[type="submit"]').click();
  await expect(page.getByRole("alert")).toBeVisible();
  await expect(page).toHaveURL(/\/login/);
});

test("every page opens for the CEO", async ({ browser }) => {
  test.setTimeout(240_000);
  const { context, page, api } = await ceoPage(browser);
  const problems = watch(page);

  const project = await post<{ id: string }>(api, "/api/projects", { code: `E2E-${Date.now()}`, name: "Smoke test project", status: "Active" });
  const employees = await json<{ id: string }[]>(api, "/api/employees");
  const meeting = await post<{ id: string }>(api, "/api/meetings", { title: "Smoke test meeting", scheduledAt: new Date(Date.now() + 86_400_000).toISOString() });

  const pages = [
    "/", "/agents", "/ai-settings", "/announcements", "/approvals", "/audit", "/chat", "/company", "/departments", "/employees",
    `/employees/${employees[0].id}`, "/finance", "/integrations", "/knowledge", "/leads", "/meetings", `/meetings/${meeting.id}`, "/office",
    "/organization", "/projects", `/projects/${project.id}`, "/quality", "/security", "/settings", "/tasks", "/timesheet", "/updates",
  ];
  expect(pages).toHaveLength(27); // with /login, tested above: all 28 pages

  for (const path of pages) {
    await test.step(path, async () => {
      await page.goto(path);
      await page.waitForLoadState("load");
      await page.waitForTimeout(1000);
      await expect(page, `${path} sent the person back to sign in`).not.toHaveURL(/\/login/);
      await expect(page.locator("body")).not.toContainText("Application error");
      expect((await page.locator("body").innerText()).trim().length, `${path} is empty`).toBeGreaterThan(0);
    });
  }
  expect(problems).toEqual([]);
  await context.close();
});

test("an objective goes from plan to approved work with the simulated AI", async ({ browser }) => {
  test.setTimeout(300_000);
  const { context, page, api } = await ceoPage(browser);

  // Every AI employee on the simulated provider, so nothing depends on a real AI account.
  type Employee = { id: string; kind: string; jobTitle: string; displayName: string };
  const people = await json<Employee[]>(api, "/api/employees");
  const agents = people.filter((e) => e.kind === "AI");
  expect(agents.length).toBeGreaterThan(0);
  for (const agent of agents) {
    const c = await json<Record<string, number>>(api, `/api/agents/${agent.id}/config`);
    const res = await api.put(`${API}/api/agents/${agent.id}/config`, {
      headers: write,
      data: {
        provider: "simulated", modelId: "simulated-v1", effort: "Medium", maxTokensPerCall: c.maxTokensPerCall, maxTurnsPerRun: c.maxTurnsPerRun,
        maxToolCallsPerRun: c.maxToolCallsPerRun, maxTokensPerRun: 10_000_000, timeoutSeconds: c.timeoutSeconds, monthlyBudgetUsd: 1000, isEnabled: true,
      },
    });
    expect(res.ok(), `config ${agent.displayName}: ${res.status()}`).toBeTruthy();
  }

  const project = await post<{ id: string }>(api, "/api/projects", { code: `OBJ-${Date.now()}`, name: "Objective project", status: "Active" });
  const roles = await json<{ id: string; code: string }[]>(api, "/api/role-definitions");
  const coo = roles.find((r) => r.code === "COO")!;
  const employees = await json<(Employee & { roleDefinitionId: string })[]>(api, "/api/employees");
  const planner = employees.find((e) => e.kind === "AI" && e.roleDefinitionId === coo.id)!;
  const workflow = await post<{ id: string }>(api, `/api/projects/${project.id}/objectives`, { objective: "Build a meeting-room booking API", plannerEmployeeId: planner.id });

  // The real engine runs on its own timer here; a person only answers the approvals.
  type Approval = { id: string; kind: string; payloadHash: string };
  type Workflow = { status: string; taskCount: number; doneCount: number; error: string | null };
  const decided: string[] = [];
  let state: Workflow = { status: "Planning", taskCount: 0, doneCount: 0, error: null };
  const deadline = Date.now() + 270_000;
  while (Date.now() < deadline && ["Planning", "AwaitingPlanApproval", "Executing"].includes(state.status)) {
    for (const a of await json<Approval[]>(api, `/api/approvals?status=Pending&workflowId=${workflow.id}`)) {
      expect(["Plan", "TaskCompletion"], `unexpected approval ${a.kind}`).toContain(a.kind);
      await post(api, `/api/approvals/${a.id}/decision`, { approve: true, payloadHash: a.payloadHash });
      decided.push(a.kind);
    }
    state = await json<Workflow>(api, `/api/workflows/${workflow.id}`);
    await page.waitForTimeout(2000);
  }
  expect(state.error).toBeNull();
  expect(state.status).toBe("Completed");
  expect(state.taskCount).toBe(3);
  expect(state.doneCount).toBe(3);
  expect(decided.filter((k) => k === "Plan")).toHaveLength(1);
  expect(decided.filter((k) => k === "TaskCompletion")).toHaveLength(3);

  // What the backend finished, a person can see.
  await page.goto(`/projects/${project.id}`);
  await expect(page.getByText("[SIMULATED] Build the first version").first()).toBeVisible({ timeout: 20_000 });
  await context.close();
});

test("a manager can reset a password and turn an account off", async ({ browser }) => {
  const admin = await ceoPage(browser);
  const problems = watch(admin.page);
  const email = `reset-${Date.now()}@e2e.test`;
  await post(admin.api, "/api/security/users", { email, displayName: "Reset target", password: MEMBER_PASSWORD, isAgent: false, roles: ["Member"] });
  admin.page.on("dialog", (d) => void d.accept());
  await admin.page.goto("/security");
  const row = admin.page.getByRole("row").filter({ hasText: email });

  const replaced = `Aa1-${crypto.randomUUID()}`;
  await row.getByRole("button", { name: "Set a new password" }).click();
  await admin.page.locator('input[autocomplete="new-password"]').fill(replaced);
  await admin.page.getByRole("button", { name: "Save" }).click();
  await expect(admin.page.locator('input[autocomplete="new-password"]')).toHaveCount(0);

  const login = async (password: string) => (await fetch(`${API}/api/auth/login`, {
    method: "POST", headers: { "Content-Type": "application/json", ...write }, body: JSON.stringify({ email, password }),
  })).status;
  expect(await login(MEMBER_PASSWORD)).toBe(401);
  expect(await login(replaced)).toBe(204);

  await row.getByRole("button", { name: "Turn off" }).click();
  await expect(row.getByText("Turned off")).toBeVisible();
  expect(await login(replaced)).toBe(401);
  expect(problems).toEqual([]);
  await admin.context.close();
});

test("a repository installs dependencies through the proxy and its tests have no network", async ({ browser }) => {
  test.setTimeout(300_000);
  const { context, api } = await ceoPage(browser);
  const project = await post<{ id: string }>(api, "/api/projects", { code: `DEP-${Date.now()}`, name: "Dependencies project", status: "Active" });
  type Run = { status: string; output: string };

  const runWith = async (name: string, setupCommand: string, testCommand: string) => {
    const repo = await post<{ id: string }>(api, `/api/projects/${project.id}/repositories`, { name, sandboxImage: "node:22-alpine", testCommand, setupCommand });
    return post<Run>(api, `/api/repositories/${repo.id}/test-runs`, {});
  };

  // A real package from the real registry, used by tests that cannot reach anything.
  const installed = await runWith("deps",
    `echo '{"name":"x","version":"1.0.0","dependencies":{"left-pad":"1.3.0"}}' > package.json && npm install --no-audit --no-fund`,
    `node -e "if (require('left-pad')('1', 3, '0') !== '001') process.exit(9)" && if wget -q -T 5 -O /dev/null https://registry.npmjs.org; then echo TESTS_HAVE_NETWORK; exit 1; fi; echo DONE`);
  expect(installed.status, installed.output).toBe("Passed");
  expect(installed.output).toContain("DONE");

  // Anywhere else is refused, through the proxy and around it.
  const blocked = await runWith("blocked", "wget -q -T 10 -O /dev/null https://example.com && echo PROXY_""ALLOWED", "echo TESTS_RAN");
  expect(blocked.status, blocked.output).toBe("Failed");
  expect(blocked.output).not.toContain("PROXY_ALLOWED");
  expect(blocked.output).not.toContain("TESTS_RAN");

  const around = await runWith("around", "unset http_proxy https_proxy HTTP_PROXY HTTPS_PROXY; wget -q -T 10 -O /dev/null https://registry.npmjs.org && echo WENT_""AROUND", "echo TESTS_RAN");
  expect(around.status, around.output).toBe("Failed");
  expect(around.output).not.toContain("WENT_AROUND");
  await context.close();
});

test("two-factor setup asks for the password first", async ({ browser }) => {
  const admin = await ceoPage(browser);
  const email = `member-${Date.now()}@e2e.test`;
  await post(admin.api, "/api/security/users", { email, displayName: "Smoke member", password: MEMBER_PASSWORD, isAgent: false, roles: ["Member"] });
  await admin.context.close();

  const context = await browser.newContext();
  const page = await context.newPage();
  const problems = watch(page);
  await signIn(page, email, MEMBER_PASSWORD);
  await page.goto("/settings");

  const start = page.getByRole("button", { name: "Turn on two-factor sign-in" });
  const password = page.locator('input[type="password"][autocomplete="current-password"]');
  await expect(start).toBeDisabled();

  await password.fill("not-the-password-1");
  await start.click();
  await expect(page.getByRole("alert").first()).toBeVisible();
  await expect(page.getByAltText("QR")).toHaveCount(0);

  await password.fill(MEMBER_PASSWORD);
  await start.click();
  await expect(page.getByAltText("QR")).toBeVisible();
  expect(problems).toEqual([]);
  await context.close();
});
