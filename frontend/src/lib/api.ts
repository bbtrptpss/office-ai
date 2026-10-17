export const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public code?: string,
  ) {
    super(message);
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${API_URL}${path}`, {
    method,
    credentials: "include",
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    let title = res.statusText;
    let code: string | undefined;
    try {
      const problem = await res.json();
      title = problem.title ?? title;
      code = problem.code;
    } catch {
      // body was not JSON
    }
    throw new ApiError(res.status, title, code);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export const api = {
  get: <T>(path: string) => request<T>("GET", path),
  post: <T>(path: string, body?: unknown) => request<T>("POST", path, body ?? {}),
  put: <T>(path: string, body: unknown) => request<T>("PUT", path, body),
  del: <T>(path: string) => request<T>("DELETE", path),
};

// ---------- Types mirrored from the API ----------

export type EmployeeKind = "Human" | "AI";
export type EmployeeStatus = "Available" | "Working" | "WaitingForApproval" | "Blocked" | "Failed" | "Inactive";
export type OrgLevel = "Executive" | "Head" | "Senior" | "Staff";
export type ProjectStatus = "Draft" | "Active" | "OnHold" | "Completed" | "Cancelled";
export type TaskStatus =
  | "Todo" | "Assigned" | "InProgress" | "Blocked" | "InReview" | "WaitingApproval" | "Done" | "Failed" | "Cancelled";
export type TaskPriority = "Low" | "Medium" | "High" | "Critical";

export interface Me {
  userId: string;
  email: string;
  displayName: string;
  isAgent: boolean;
  roles: string[];
  permissions: string[];
  employeeId: string | null;
}

export interface Department {
  id: string;
  code: string;
  name: string;
  description: string | null;
  parentId: string | null;
  sortOrder: number;
  isActive: boolean;
  version: number;
}

export interface RoleDefinition {
  id: string;
  code: string;
  title: string;
  departmentId: string;
  level: OrgLevel;
  description: string | null;
  responsibilities: string[];
  skills: string[];
  taskTypes: string[];
  systemInstructions: string | null;
  requiresHuman: boolean;
  isActive: boolean;
  version: number;
}

export interface Employee {
  id: string;
  displayName: string;
  kind: EmployeeKind;
  userId: string | null;
  departmentId: string;
  departmentName: string;
  roleDefinitionId: string;
  jobTitle: string;
  managerId: string | null;
  status: EmployeeStatus;
  version: number;
}

export interface OrgChartNode {
  id: string;
  displayName: string;
  kind: EmployeeKind;
  jobTitle: string;
  departmentName: string;
  status: EmployeeStatus;
  reports: OrgChartNode[];
}

export interface Project {
  id: string;
  code: string;
  name: string;
  description: string | null;
  status: ProjectStatus;
  ownerEmployeeId: string | null;
  estimatedBudget: number | null;
  startDate: string | null;
  dueDate: string | null;
  taskCount: number;
  doneCount: number;
  version: number;
}

export interface Milestone {
  id: string;
  projectId: string;
  name: string;
  dueDate: string | null;
  sortOrder: number;
  version: number;
}

export interface Task {
  id: string;
  projectId: string;
  milestoneId: string | null;
  title: string;
  description: string | null;
  acceptanceCriteria: string | null;
  status: TaskStatus;
  priority: TaskPriority;
  dueDate: string | null;
  assignees: { employeeId: string; displayName: string; kind: EmployeeKind }[];
  dependsOn: string[];
  nextStates: TaskStatus[];
  version: number;
}

export interface Dashboard {
  activeProjects: number;
  totalTasks: number;
  completedTasks: number;
  blockedTasks: number;
  overdueTasks: number;
  waitingApproval: number;
  humanEmployees: number;
  aiEmployees: number;
  aiAvailable: number;
  aiWorking: number;
  workload: { employeeId: string; displayName: string; kind: EmployeeKind; openTasks: number }[];
}

export interface AuditLog {
  id: number;
  occurredAt: string;
  actorUserId: string | null;
  actorName: string | null;
  action: string;
  entityType: string;
  entityId: string | null;
  oldValues: string | null;
  newValues: string | null;
  correlationId: string | null;
}

export interface Role {
  id: string;
  name: string;
  permissions: string[];
}

export interface User {
  id: string;
  email: string;
  displayName: string;
  isAgent: boolean;
  isActive: boolean;
  roles: string[];
}

// ---------- Phase 2: AI employees ----------

export type Effort = "Low" | "Medium" | "High" | "XHigh" | "Max";
export type AgentRunStatus = "Queued" | "Running" | "Succeeded" | "Failed" | "Cancelled" | "LimitReached";
export type RunStepKind = "Instructions" | "Input" | "AssistantText" | "ToolCall" | "ToolResult" | "Notice";
export type ToolExecutionStatus = "Succeeded" | "Failed" | "Denied" | "TimedOut" | "InvalidInput";

export interface AiProvider {
  name: string;
  isAvailable: boolean;
  isSimulated: boolean;
}

export interface AiModel {
  id: string;
  provider: string;
  modelId: string;
  displayName: string;
  tier: "Routine" | "Standard" | "Advanced";
  inputUsdPerMTok: number;
  outputUsdPerMTok: number;
  cacheReadUsdPerMTok: number;
  cacheWriteUsdPerMTok: number;
  isEnabled: boolean;
  version: number;
}

export interface AgentTool {
  id: string;
  version: string;
  description: string;
  requiresApproval: boolean;
  hasSideEffects: boolean;
  timeoutSeconds: number;
}

export interface AgentConfig {
  employeeId: string;
  provider: string;
  modelId: string;
  effort: Effort;
  maxTokensPerCall: number;
  maxTurnsPerRun: number;
  maxToolCallsPerRun: number;
  maxTokensPerRun: number;
  timeoutSeconds: number;
  monthlyBudgetUsd: number;
  isEnabled: boolean;
  tools: string[];
  spentThisMonthUsd: number;
  version: number;
}

export interface AgentRun {
  id: string;
  taskId: string;
  taskTitle: string;
  projectId: string;
  employeeId: string;
  employeeName: string;
  status: AgentRunStatus;
  provider: string;
  modelId: string;
  isSimulated: boolean;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  turns: number;
  toolCalls: number;
  inputTokens: number;
  outputTokens: number;
  costUsd: number;
  stopReason: string | null;
  error: string | null;
}

export interface AgentRunDetail {
  run: AgentRun;
  steps: { sequence: number; kind: RunStepKind; content: string; toolCallId: string | null; createdAt: string }[];
  toolExecutions: {
    id: string;
    toolId: string;
    toolVersion: string;
    toolCallId: string;
    status: ToolExecutionStatus;
    inputJson: string;
    output: string | null;
    error: string | null;
    durationMs: number;
    startedAt: string;
  }[];
}

export interface Deliverable {
  id: string;
  taskId: string;
  employeeId: string;
  employeeName: string;
  runId: string | null;
  title: string;
  format: string;
  content: string;
  status: "Submitted" | "Accepted" | "Rejected";
  isSimulated: boolean;
  createdAt: string;
}

export interface UsageSummary {
  thisMonthUsd: number;
  thisMonthSimulatedUsd: number;
  thisMonthTokens: number;
  runsThisMonth: number;
  activeRuns: number;
  byEmployee: { employeeId: string; displayName: string; costUsd: number; tokens: number; runs: number; budgetUsd: number }[];
}

export const usd = (n: number) =>
  `$${n.toLocaleString("en-US", { minimumFractionDigits: n > 0 && n < 0.01 ? 4 : 2, maximumFractionDigits: n > 0 && n < 0.01 ? 6 : 2 })}`;
