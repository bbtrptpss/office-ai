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

// ---------- Phase 3: orchestration ----------

export type ApprovalKind = "Plan" | "TaskCompletion" | "ToolAction" | "Escalation" | "Estimate" | "Merge";
export type ApprovalStatus = "Pending" | "Approved" | "Rejected" | "Expired" | "Cancelled";
export type WorkflowStatus = "Planning" | "AwaitingPlanApproval" | "Executing" | "Completed" | "Failed" | "Cancelled";

export interface Approval {
  id: string;
  kind: ApprovalKind;
  status: ApprovalStatus;
  title: string;
  details: string | null;
  payloadJson: string;
  payloadHash: string;
  projectId: string | null;
  taskId: string | null;
  workflowExecutionId: string | null;
  runId: string | null;
  requestedByEmployeeId: string | null;
  requestedByName: string | null;
  createdAt: string;
  expiresAt: string;
  decidedByUserId: string | null;
  decidedAt: string | null;
  decisionNote: string | null;
  isSimulated: boolean;
  requiredPermission: string;
}

export interface Workflow {
  id: string;
  projectId: string;
  objective: string;
  status: WorkflowStatus;
  plannerEmployeeId: string;
  plannerName: string;
  planningTaskId: string | null;
  budgetUsd: number;
  spentUsd: number;
  maxParallelRuns: number;
  taskCount: number;
  doneCount: number;
  createdAt: string;
  completedAt: string | null;
  error: string | null;
}

export interface WorkflowEvent {
  id: number;
  taskId: string | null;
  kind: string;
  message: string;
  createdAt: string;
}

export interface Review {
  id: string;
  taskId: string;
  deliverableId: string | null;
  reviewerName: string | null;
  outcome: "Approved" | "ChangesRequested";
  comments: string;
  isSimulated: boolean;
  createdAt: string;
}

export interface PlanItem {
  key: string;
  title: string;
  description: string | null;
  acceptanceCriteria: string | null;
  requiredSkills: string[];
  dependsOn: string[];
  priority: TaskPriority;
}

// ---------- Phase 4: delivery & code ----------

export interface Requirement {
  id: string;
  code: string;
  kind: "Functional" | "NonFunctional" | "Constraint" | "Assumption" | "Risk";
  title: string;
  description: string | null;
  acceptanceCriteria: string | null;
  priority: "Must" | "Should" | "Could" | "Wont";
  status: "Draft" | "Approved" | "Rejected";
  authorName: string | null;
  createdAt: string;
  version: number;
}

export interface Decision {
  id: string;
  number: number;
  title: string;
  context: string;
  decision: string;
  consequences: string | null;
  status: "Proposed" | "Accepted" | "Rejected" | "Superseded";
  authorName: string | null;
  createdAt: string;
}

export interface Estimate {
  id: string;
  revision: number;
  status: "Draft" | "Submitted" | "Approved" | "Rejected";
  currency: string;
  assumptions: string | null;
  contingencyPercent: number;
  targetMarginPercent: number;
  aiCostUsd: number;
  usdToThb: number;
  totalHours: number;
  laborCost: number;
  aiCost: number;
  contingency: number;
  totalCost: number;
  price: number;
  grossMargin: number;
  grossMarginPercent: number;
  warnings: string[];
  authorName: string | null;
  createdAt: string;
  items: { description: string; roleTitle: string; hours: number; rateThb: number; amount: number; evidence: string | null }[];
}

export interface RoleRate {
  roleDefinitionId: string;
  code: string;
  title: string;
  hourlyRateThb: number;
}

export interface CompanySettings {
  usdToThb: number;
  defaultContingencyPercent: number;
  targetMarginPercent: number;
  version: number;
}

export interface Repository {
  id: string;
  projectId: string;
  name: string;
  defaultBranch: string;
  sandboxImage: string;
  testCommand: string;
  isDefault: boolean;
  createdAt: string;
  version: number;
}

export interface TestRun {
  id: string;
  repositoryId: string;
  taskId: string | null;
  runId: string | null;
  branch: string;
  commitSha: string | null;
  image: string;
  command: string;
  status: "Passed" | "Failed" | "TimedOut" | "Error";
  exitCode: number | null;
  output: string | null;
  durationMs: number;
  startedAt: string;
}

export interface MergeRequest {
  id: string;
  projectId: string;
  repositoryId: string;
  taskId: string | null;
  number: number;
  title: string;
  description: string | null;
  sourceBranch: string;
  targetBranch: string;
  headSha: string;
  status: "Open" | "Merged" | "Closed" | "Conflict";
  latestTestRunId: string | null;
  latestTestStatus: TestRun["status"] | null;
  authorName: string | null;
  mergedSha: string | null;
  mergedAt: string | null;
  createdAt: string;
}

export const thb = (n: number) => `฿${n.toLocaleString("th-TH", { minimumFractionDigits: 0, maximumFractionDigits: 2 })}`;
