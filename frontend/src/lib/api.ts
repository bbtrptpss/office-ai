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
