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
    headers: body === undefined ? { "X-Requested-With": "officeai" } : { "Content-Type": "application/json", "X-Requested-With": "officeai" },
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
  /** "settings" (saved on the AI settings page), "environment" (server .env) or null. */
  keySource: "settings" | "environment" | null;
  keyLast4: string | null;
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

export interface RecentTask {
  id: string;
  projectId: string;
  projectName: string;
  title: string;
  status: TaskStatus;
  priority: TaskPriority;
  assigneeName: string | null;
  dueDate: string | null;
  changedAt: string;
}

export interface Assistant {
  channelId: string;
  members: { employeeId: string; displayName: string; jobTitle: string; status: EmployeeStatus; openTasks: number; currentTask: string | null }[];
  /** The host's Claude Code bridge is set up (only people with agent.manage may use it). */
  claudeAvailable: boolean;
}

export interface ClaudeReply {
  reply: string;
  continued: boolean;
  equivalentCostUsd: number;
  /** Tasks Claude handed to AI employees in this reply. */
  tasksStarted: number;
}

/** One dashboard terminal command (CEO only): PowerShell in the project folder on the host. */
export interface TerminalResult {
  output: string;
  exitCode: number;
  timedOut: boolean;
  ms: number;
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
  departmentId: string | null;
  departmentName: string | null;
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
  remoteUrl: string | null;
  remoteTokenEnvVar: string | null;
  remoteTokenPresent: boolean;
  remotePushedAt: string | null;
  remotePushedSha: string | null;
  remoteError: string | null;
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

// ---------- Phase 5: company management ----------

export type BudgetScope = "Company" | "Department" | "Project";
export type BudgetLevel = "Ok" | "Warning" | "Exceeded";

export interface Budget {
  id: string;
  scope: BudgetScope;
  scopeId: string | null;
  scopeName: string;
  month: string;
  amountUsd: number;
  spentUsd: number;
  level: BudgetLevel;
  version: number;
}

export interface FinanceReport {
  month: string;
  usdToThb: number;
  totalUsd: number;
  totalThb: number;
  simulatedUsd: number;
  projects: {
    projectId: string; code: string; name: string; estimateRevision: number | null; plannedTotalCostThb: number | null; priceThb: number | null;
    plannedAiCostThb: number | null; actualAiUsdMonth: number; actualAiUsdAllTime: number; actualAiThbAllTime: number; aiVarianceThb: number | null;
  }[];
  departments: { id: string; name: string; costUsd: number; tokens: number; runs: number }[];
  employees: { id: string; name: string; costUsd: number; tokens: number; runs: number }[];
  budgets: Budget[];
}

export interface PerformanceRow {
  employeeId: string; name: string; jobTitle: string; department: string; runs: number; succeeded: number; failed: number; limitReached: number;
  successRate: number; costUsd: number; costPerRunUsd: number; avgTurns: number; toolCalls: number; toolDenied: number; tasksDone: number;
  reviewsApproved: number; changesRequested: number; firstPassApprovalRate: number;
}

export interface KnowledgeEntry {
  id: string;
  kind: "Policy" | "Standard" | "Decision" | "Lesson" | "Template" | "Reference";
  title: string;
  body: string;
  tags: string[];
  projectId: string | null;
  projectName: string | null;
  status: "Proposed" | "Validated" | "Archived";
  source: string | null;
  authorName: string | null;
  validatedAt: string | null;
  createdAt: string;
  version: number;
}

export interface ProjectDocument {
  id: string;
  projectId: string;
  kind: string;
  title: string;
  version: number;
  status: "Draft" | "Final";
  authorName: string | null;
  isSimulated: boolean;
  createdAt: string;
  content: string | null;
}

export interface Integration {
  id: string;
  name: string;
  kind: "Webhook";
  secretEnvVar: string;
  secretPresent: boolean;
  events: string[];
  isEnabled: boolean;
  cursor: string;
  version: number;
}

export interface Delivery {
  id: number;
  event: string;
  title: string;
  status: "Sent" | "Failed";
  statusCode: number | null;
  error: string | null;
  createdAt: string;
}

export const INTEGRATION_EVENTS = ["approval.requested", "workflow.completed", "workflow.escalated", "budget.reached", "run.failed"];

export type ChannelKind = "Company" | "Department" | "Project";

export interface ChatChannel {
  id: string;
  kind: ChannelKind;
  name: string;
  description: string | null;
  departmentId: string | null;
  projectId: string | null;
  isArchived: boolean;
  unread: number;
  lastMessageAt: string | null;
}

export interface ChatMessage {
  id: string;
  channelId: string;
  authorName: string;
  authorEmployeeId: string | null;
  isAgent: boolean;
  isSimulated: boolean;
  body: string | null;
  isDeleted: boolean;
  createdAt: string;
  canDelete: boolean;
  mentions: string[];
}

export type AnnouncementStatus = "Draft" | "Published" | "Archived";
export type AnnouncementPriority = "Normal" | "Important";

export interface Announcement {
  id: string;
  title: string;
  body: string;
  priority: AnnouncementPriority;
  status: AnnouncementStatus;
  departmentId: string | null;
  departmentName: string | null;
  isPinned: boolean;
  publishedAt: string | null;
  authorName: string | null;
  isSimulated: boolean;
  isRead: boolean;
  readCount: number;
  createdAt: string;
  version: number;
}

export type MeetingStatus = "Scheduled" | "Held" | "Cancelled";
export type MinutesStatus = "None" | "Draft" | "Approved";
export type Attendance = "Invited" | "Attended" | "Absent";

export interface MeetingSummary {
  id: string;
  title: string;
  projectId: string | null;
  projectName: string | null;
  departmentId: string | null;
  scheduledAt: string;
  durationMinutes: number;
  location: string | null;
  status: MeetingStatus;
  minutesStatus: MinutesStatus;
  participantCount: number;
  openActionItems: number;
  version: number;
}

export interface AgendaItem {
  id: string;
  sortOrder: number;
  title: string;
  notes: string | null;
  ownerEmployeeId: string | null;
  ownerName: string | null;
  durationMinutes: number | null;
}

export interface MeetingParticipant {
  employeeId: string;
  displayName: string;
  kind: EmployeeKind;
  attendance: Attendance;
}

export interface ActionItem {
  id: string;
  title: string;
  ownerEmployeeId: string | null;
  ownerName: string | null;
  dueDate: string | null;
  isDone: boolean;
  taskId: string | null;
  version: number;
}

export interface Meeting {
  id: string;
  title: string;
  purpose: string | null;
  projectId: string | null;
  projectName: string | null;
  departmentId: string | null;
  departmentName: string | null;
  scheduledAt: string;
  durationMinutes: number;
  location: string | null;
  status: MeetingStatus;
  organizerName: string | null;
  agenda: AgendaItem[];
  participants: MeetingParticipant[];
  actionItems: ActionItem[];
  minutes: string | null;
  minutesStatus: MinutesStatus;
  minutesAuthorName: string | null;
  minutesIsSimulated: boolean;
  minutesApprovedAt: string | null;
  minutesDocumentId: string | null;
  version: number;
}

export interface ChannelMember {
  employeeId: string;
  displayName: string;
  kind: EmployeeKind;
  jobTitle: string;
  isExplicit: boolean;
}

export interface AppNotification {
  id: number;
  kind: "mention" | "meeting_invite" | "action_item";
  title: string;
  body: string | null;
  link: string | null;
  createdAt: string;
  isRead: boolean;
}

export interface NotificationList {
  unread: number;
  items: AppNotification[];
}
