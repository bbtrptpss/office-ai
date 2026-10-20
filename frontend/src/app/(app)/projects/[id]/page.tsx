"use client";

import { use, useMemo, useState } from "react";
import {
  Alert, Box, Button, Card, CardActionArea, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  Divider, FormControlLabel, MenuItem, Stack, Switch, Tab, Tabs, TextField, Typography,
} from "@mui/material";
import { AnimatePresence, LayoutGroup, motion, useReducedMotion } from "motion/react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import AddIcon from "@mui/icons-material/Add";
import ForumIcon from "@mui/icons-material/ForumOutlined";
import PeopleIcon from "@mui/icons-material/PeopleAltOutlined";
import EditIcon from "@mui/icons-material/EditOutlined";
import FiberManualRecordIcon from "@mui/icons-material/FiberManualRecord";
import { AgentRun, api, Deliverable, Employee, Milestone, Project, Review, Task, TaskComplexity, TaskPriority, TaskStatus } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { KindIcon, Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";
import { DeliverableList, RetryRunButton, RunList } from "@/components/agents";
import { ObjectivesPanel } from "@/components/objectives";
import { ProjectMembersDialog } from "@/components/members";
import { ProjectEditDialog } from "@/components/projectEdit";
import { StagesPanel } from "@/components/stages";
import { DefectsPanel } from "@/components/defects";
import { ReleasesPanel } from "@/components/releases";
import { DeliveryPackagesPanel } from "@/components/deliveryPackages";
import { CloseoutPanel, DecisionsPanel, DocumentsPanel, EstimatesPanel, RepositoryPanel, RequirementsPanel } from "@/components/workspace";

const COLUMNS: TaskStatus[] = ["Todo", "Assigned", "InProgress", "Blocked", "InReview", "WaitingApproval", "Done", "Failed"];
const PRIORITIES: TaskPriority[] = ["Low", "Medium", "High", "Critical"];
const COMPLEXITIES: TaskComplexity[] = ["Routine", "Standard", "Complex"];
const COLUMN_COLORS: Record<TaskStatus, string> = {
  Todo: "#CDB8F5", Assigned: "#A9C7FF", InProgress: "#7B4FC9", Blocked: "#FF8A8A", InReview: "#FF7EB9",
  WaitingApproval: "#FFC56E", Done: "#7ED6A8", Failed: "#E06060", Cancelled: "#BDB6CC",
};
const COLUMN_EMOJI: Record<TaskStatus, string> = {
  Todo: "🌙", Assigned: "📌", InProgress: "💜", Blocked: "🧱", InReview: "🔍",
  WaitingApproval: "📝", Done: "🎀", Failed: "💥", Cancelled: "🗑️",
};

interface TaskForm {
  title: string;
  description: string;
  acceptanceCriteria: string;
  priority: TaskPriority;
  complexity: TaskComplexity;
  dueDate: string;
  milestoneId: string;
}

const emptyForm: TaskForm = { title: "", description: "", acceptanceCriteria: "", priority: "Medium", complexity: "Standard", dueDate: "", milestoneId: "" };

export default function ProjectBoardPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const reduce = useReducedMotion();

  const { data, error, reload } = useLoad(async () => {
    const [project, tasks, milestones, employees] = await Promise.all([
      api.get<Project>(`/api/projects/${id}`),
      api.get<Task[]>(`/api/projects/${id}/tasks`),
      api.get<Milestone[]>(`/api/projects/${id}/milestones`),
      api.get<Employee[]>("/api/employees"),
    ]);
    return { project, tasks, milestones, employees };
  }, id);

  const live = useRealtime<{ projectId: string }>("taskChanged", (e) => {
    if (e.projectId === id) void reload();
  });

  const [openTaskId, setOpenTaskId] = useState<string | null>(null);
  const [form, setForm] = useState<TaskForm | null>(null);
  const [newTask, setNewTask] = useState<TaskForm | null>(null);
  const [milestoneName, setMilestoneName] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [showCancelled, setShowCancelled] = useState(false);
  const [membersOpen, setMembersOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [tab, setTab] = useState<"board" | "stages" | "defects" | "releases" | "delivery" | "requirements" | "decisions" | "estimates" | "repository" | "documents" | "closeout">("board");
  // ?task=<id> (from the company task board) opens that task once the board has loaded.
  const linkedTaskId = useSearchParams().get("task");
  const [linkHandled, setLinkHandled] = useState(false);

  const titles = useMemo(() => new Map(data?.tasks.map((x) => [x.id, x.title])), [data]);
  if (!data) return <Loading error={error} />;

  if (linkedTaskId && !linkHandled) {
    setLinkHandled(true);
    const linked = data.tasks.find((x) => x.id === linkedTaskId);
    if (linked) openTask(linked);
  }

  const current = data.tasks.find((x) => x.id === openTaskId) ?? null;
  const columns = showCancelled ? [...COLUMNS, "Cancelled" as TaskStatus] : COLUMNS;

  async function act(action: () => Promise<unknown>, onOk?: () => void) {
    setMessage(null);
    try {
      await action();
      onOk?.();
    } catch (e) {
      setMessage(errorMessage(e));
    }
    await reload();
  }

  function openTask(task: Task) {
    setMessage(null);
    setOpenTaskId(task.id);
    setForm({
      title: task.title, description: task.description ?? "", acceptanceCriteria: task.acceptanceCriteria ?? "",
      priority: task.priority, complexity: task.complexity, dueDate: task.dueDate ?? "", milestoneId: task.milestoneId ?? "",
    });
  }

  const toBody = (f: TaskForm) => ({ ...f, dueDate: f.dueDate || null, milestoneId: f.milestoneId || null });

  return (
    <>
      <PageHeader
        title={data.project.name}
        subtitle={
          <Stack direction="row" spacing={1} component="span" sx={{ alignItems: "center" }}>
            <span>{data.project.code}</span>
            <StatusChip value={data.project.status} />
            <Chip
              size="small"
              icon={<FiberManualRecordIcon sx={{ fontSize: 10 }} color={live ? "success" : "disabled"} />}
              label={t.live}
              variant="outlined"
            />
          </Stack>
        }
        actions={
          <>
            {can("org.read") && <Button component={Link} href={`/chat?project=${id}`} startIcon={<ForumIcon />}>{t.openProjectChat}</Button>}
            {can("project.manage") && <Button startIcon={<EditIcon />} onClick={() => setEditOpen(true)}>{t.editProject}</Button>}
            {can("project.manage") && <Button startIcon={<PeopleIcon />} onClick={() => setMembersOpen(true)}>{t.members}</Button>}
            {can("project.manage") && <Button onClick={() => setMilestoneName("")}>{t.newMilestone}</Button>}
            {can("task.manage") && (
              <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setMessage(null); setNewTask(emptyForm); }}>
                {t.newTask}
              </Button>
            )}
          </>
        }
      />

      {membersOpen && <ProjectMembersDialog projectId={id} title={data.project.name} onClose={() => setMembersOpen(false)} />}
      {editOpen && (
        <ProjectEditDialog project={data.project} openTasks={data.tasks.filter((x) => x.status !== "Done" && x.status !== "Cancelled").length}
          onClose={() => setEditOpen(false)} onSaved={() => { setEditOpen(false); void reload(); }} />
      )}
      <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable" allowScrollButtonsMobile sx={{ mb: 2 }}>
        <Tab value="board" label={t.board} />
        <Tab value="stages" label={t.stages} />
        <Tab value="defects" label={t.defects} />
        <Tab value="releases" label={t.releases} />
        <Tab value="delivery" label={t.packagesTab} />
        <Tab value="requirements" label={t.requirements} />
        <Tab value="decisions" label={t.decisionsTab} />
        <Tab value="estimates" label={t.estimates} />
        <Tab value="repository" label={t.repository} />
        <Tab value="documents" label={t.documents} />
        {can("finance.read") && <Tab value="closeout" label={t.closeout} />}
      </Tabs>
      {tab === "stages" && <StagesPanel projectId={id} />}
      {tab === "defects" && <DefectsPanel projectId={id} tasks={data.tasks} onOpenTask={(taskId) => { setTab("board"); setOpenTaskId(taskId); }} />}
      {tab === "releases" && <ReleasesPanel projectId={id} />}
      {tab === "delivery" && <DeliveryPackagesPanel projectId={id} onOpenTask={(taskId) => { setTab("board"); setOpenTaskId(taskId); }} />}
      {tab === "requirements" && <RequirementsPanel projectId={id} />}
      {tab === "decisions" && <DecisionsPanel projectId={id} />}
      {tab === "estimates" && <EstimatesPanel projectId={id} />}
      {tab === "repository" && <RepositoryPanel projectId={id} />}
      {tab === "documents" && <DocumentsPanel projectId={id} />}
      {tab === "closeout" && <CloseoutPanel projectId={id} onOpenDocuments={() => setTab("documents")} />}

      {tab === "board" && <>
      {data.milestones.length > 0 && (
        <Stack direction="row" spacing={1} sx={{ mb: 2, flexWrap: "wrap", rowGap: 1 }}>
          <Typography variant="body2" color="text.secondary" sx={{ alignSelf: "center" }}>{t.milestones}:</Typography>
          {data.milestones.map((m) => <Chip key={m.id} size="small" label={`${m.name}${m.dueDate ? ` · ${m.dueDate}` : ""}`} />)}
        </Stack>
      )}
      <ObjectivesPanel projectId={id} employees={data.employees} />
      <FormControlLabel control={<Switch size="small" checked={showCancelled} onChange={(e) => setShowCancelled(e.target.checked)} />} label={t.showCancelled} sx={{ mb: 1 }} />
      {message && !openTaskId && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}

      <LayoutGroup>
      <Box sx={{ display: "flex", gap: 2, overflowX: "auto", pb: 2, alignItems: "flex-start" }}>
        {columns.map((status) => {
          const items = data.tasks.filter((x) => x.status === status);
          return (
            <Box
              key={status}
              sx={{
                minWidth: 256, width: 256, flexShrink: 0, p: 1.25, borderRadius: "22px", minHeight: 160,
                bgcolor: (th) => `rgba(${th.vars?.palette.primary.mainChannel} / 0.06)`,
                borderTop: `4px solid ${COLUMN_COLORS[status]}`,
              }}
            >
              <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1, px: 0.5 }}>
                <Box component="span" aria-hidden>{COLUMN_EMOJI[status]}</Box>
                <Typography fontWeight={600} sx={{ fontFamily: "var(--font-cute)" }}>{label(status)}</Typography>
                <Chip size="small" label={items.length} />
              </Stack>
              <Stack spacing={1}>
                <AnimatePresence initial={false}>
                {items.map((task) => (
                  <motion.div
                    key={task.id}
                    layoutId={task.id}
                    layout
                    initial={reduce ? false : { opacity: 0, scale: 0.9 }}
                    animate={{ opacity: 1, scale: 1 }}
                    exit={reduce ? undefined : { opacity: 0, scale: 0.9 }}
                    transition={{ type: "spring", stiffness: 420, damping: 34 }}
                    whileHover={reduce ? undefined : { y: -3 }}
                  >
                  <Card>
                    <CardActionArea onClick={() => openTask(task)}>
                      <CardContent sx={{ p: 1.5, "&:last-child": { pb: 1.5 } }}>
                        <Typography fontWeight={500} sx={{ mb: 0.5 }}>{task.title}</Typography>
                        <Stack direction="row" spacing={0.5} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 0.5 }}>
                          <StatusChip value={task.priority} />
                          {task.dependsOn.length > 0 && <Chip size="small" variant="outlined" label={`⧗ ${task.dependsOn.length}`} />}
                        </Stack>
                        {task.assignees.map((a) => (
                          <Stack key={a.employeeId} direction="row" spacing={0.5} sx={{ alignItems: "center", mt: 0.5 }}>
                            <KindIcon kind={a.kind} seed={a.displayName} size={22} />
                            <Typography variant="body2" color="text.secondary" noWrap>{a.displayName}</Typography>
                          </Stack>
                        ))}
                      </CardContent>
                    </CardActionArea>
                  </Card>
                  </motion.div>
                ))}
                </AnimatePresence>
              </Stack>
            </Box>
          );
        })}
      </Box>
      </LayoutGroup>
      </>}

      {/* Task detail */}
      <Dialog open={!!current} onClose={() => setOpenTaskId(null)} fullWidth maxWidth="md">
        {current && form && (
          <>
            <DialogTitle>
              <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                <span>{current.title}</span>
                <StatusChip value={current.status} />
              </Stack>
            </DialogTitle>
            <DialogContent>
              <Stack spacing={2} sx={{ mt: 1 }}>
                {message && <Alert severity="warning">{message}</Alert>}

                {current.nextStates.length > 0 && can("task.update_status") && (
                  <Box>
                    <Typography variant="body2" color="text.secondary" gutterBottom>{t.moveTo}</Typography>
                    <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", rowGap: 1 }}>
                      {current.nextStates
                        .filter((s) => s !== "Done" || can("task.approve"))
                        .map((s) => (
                          <Button key={s} size="small" variant="outlined" color={s === "Cancelled" || s === "Failed" ? "error" : "primary"}
                            onClick={() => act(() => api.post(`/api/tasks/${current.id}/status`, { status: s, version: current.version }))}>
                            {label(s)}
                          </Button>
                        ))}
                    </Stack>
                  </Box>
                )}

                {can("task.assign") && !["Done", "Cancelled"].includes(current.status) && (
                  <TextField
                    select size="small" label={t.assignee}
                    value={current.assignees[0]?.employeeId ?? ""}
                    onChange={(e) => act(() => api.post(`/api/tasks/${current.id}/assign`, { employeeId: e.target.value, version: current.version }))}
                  >
                    {data.employees.filter((e) => e.status !== "Inactive").map((e) => (
                      <MenuItem key={e.id} value={e.id}>{e.kind === "AI" ? "🤖 " : ""}{e.displayName} — {e.jobTitle}</MenuItem>
                    ))}
                  </TextField>
                )}

                <Divider />
                <TaskFields form={form} setForm={setForm} milestones={data.milestones} disabled={!can("task.manage")} />

                <Box>
                  <Typography variant="body2" color="text.secondary" gutterBottom>{t.dependsOn}</Typography>
                  <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", rowGap: 1 }}>
                    {current.dependsOn.map((d) => (
                      <Chip key={d} label={titles.get(d) ?? d}
                        onDelete={can("task.manage") ? () => act(() => api.del(`/api/tasks/${current.id}/dependencies/${d}`)) : undefined} />
                    ))}
                    {current.dependsOn.length === 0 && <Typography variant="body2">—</Typography>}
                  </Stack>
                  {can("task.manage") && (
                    <TextField select size="small" label={t.addDependency} value="" sx={{ mt: 1, minWidth: 260 }}
                      onChange={(e) => act(() => api.post(`/api/tasks/${current.id}/dependencies`, { dependsOnTaskId: e.target.value }))}>
                      {data.tasks.filter((x) => x.id !== current.id && !current.dependsOn.includes(x.id)).map((x) => (
                        <MenuItem key={x.id} value={x.id}>{x.title}</MenuItem>
                      ))}
                    </TextField>
                  )}
                </Box>

                {can("agent.read") && <TaskAiPanel task={current} onError={setMessage} />}
              </Stack>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setOpenTaskId(null)}>{t.close}</Button>
              {can("task.manage") && (
                <Button variant="contained" disabled={!form.title}
                  onClick={() => act(() => api.put(`/api/tasks/${current.id}`, { ...toBody(form), version: current.version }))}>
                  {t.save}
                </Button>
              )}
            </DialogActions>
          </>
        )}
      </Dialog>

      {/* New task */}
      <Dialog open={!!newTask} onClose={() => setNewTask(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.newTask}</DialogTitle>
        {newTask && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              {message && <Alert severity="error">{message}</Alert>}
              <TaskFields form={newTask} setForm={setNewTask} milestones={data.milestones} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setNewTask(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!newTask?.title}
            onClick={() => act(() => api.post(`/api/projects/${id}/tasks`, toBody(newTask!)), () => setNewTask(null))}>
            {t.create}
          </Button>
        </DialogActions>
      </Dialog>

      {/* New milestone */}
      <Dialog open={milestoneName !== null} onClose={() => setMilestoneName(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t.newMilestone}</DialogTitle>
        <DialogContent>
          <TextField label={t.name} value={milestoneName ?? ""} onChange={(e) => setMilestoneName(e.target.value)} fullWidth sx={{ mt: 1 }} />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setMilestoneName(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!milestoneName}
            onClick={() => act(() => api.post(`/api/projects/${id}/milestones`, { name: milestoneName, sortOrder: data.milestones.length }), () => setMilestoneName(null))}>
            {t.create}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

function TaskFields({ form, setForm, milestones, disabled }: {
  form: TaskForm;
  setForm: (f: TaskForm) => void;
  milestones: Milestone[];
  disabled?: boolean;
}) {
  const { t, label } = useI18n();
  return (
    <>
      <TextField label={t.title} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} required disabled={disabled} />
      <TextField label={t.description} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} multiline minRows={2} disabled={disabled} />
      <TextField label={t.acceptanceCriteria} value={form.acceptanceCriteria} onChange={(e) => setForm({ ...form, acceptanceCriteria: e.target.value })} multiline minRows={2} disabled={disabled} />
      <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
        <TextField select label={t.priority} value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value as TaskPriority })} fullWidth disabled={disabled}>
          {PRIORITIES.map((p) => <MenuItem key={p} value={p}>{label(p)}</MenuItem>)}
        </TextField>
        <TextField select label={t.complexity} value={form.complexity} onChange={(e) => setForm({ ...form, complexity: e.target.value as TaskComplexity })} fullWidth disabled={disabled}
          helperText={form.complexity === "Standard" ? undefined : t.complexityHint}>
          {COMPLEXITIES.map((c) => <MenuItem key={c} value={c}>{label(c)}</MenuItem>)}
        </TextField>
        <TextField label={t.dueDate} type="date" value={form.dueDate} onChange={(e) => setForm({ ...form, dueDate: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} fullWidth disabled={disabled} />
        <TextField select label="Milestone" value={form.milestoneId} onChange={(e) => setForm({ ...form, milestoneId: e.target.value })} fullWidth disabled={disabled}>
          <MenuItem value="">{t.none}</MenuItem>
          {milestones.map((m) => <MenuItem key={m.id} value={m.id}>{m.name}</MenuItem>)}
        </TextField>
      </Stack>
    </>
  );
}

/** AI section of the task dialog: start a run, follow runs live, read deliverables. */
function TaskAiPanel({ task, onError }: { task: Task; onError: (m: string) => void }) {
  const { t } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const [starting, setStarting] = useState(false);
  const [info, setInfo] = useState<string | null>(null);
  const { data, reload } = useLoad(async () => {
    const [runs, deliverables] = await Promise.all([
      api.get<AgentRun[]>(`/api/agent-runs?taskId=${task.id}&take=20`),
      api.get<Deliverable[]>(`/api/tasks/${task.id}/deliverables`),
    ]);
    const reviews = await api.get<Review[]>(`/api/tasks/${task.id}/reviews`);
    return { runs, deliverables, reviews };
  }, task.id);
  useRealtime<{ taskId: string }>("agentRunChanged", (e) => {
    if (e.taskId === task.id) void reload();
  });

  const aiAssignee = task.assignees.find((a) => a.kind === "AI");
  const active = data?.runs.some((r) => r.status === "Queued" || r.status === "Running");
  const canStart = can("agent.run") && !!aiAssignee && ["Assigned", "InProgress"].includes(task.status) && !active;
  const latest = data?.runs.reduce<AgentRun | undefined>((a, r) => (!a || r.createdAt > a.createdAt ? r : a), undefined);

  return (
    <Box sx={{ p: 2, borderRadius: "18px", bgcolor: (th) => `rgba(${th.vars?.palette.secondary.mainChannel} / 0.06)` }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1.5 }}>
        <Typography variant="h6" sx={{ flex: 1 }}>🤖 {t.runs}</Typography>
        {can("agent.run") && (
          <Button
            variant="contained"
            size="small"
            disabled={!canStart || starting}
            onClick={async () => {
              setStarting(true);
              try {
                await api.post(`/api/tasks/${task.id}/runs`, {});
                await reload();
              } catch (e) {
                onError(errorMessage(e));
              } finally {
                setStarting(false);
              }
            }}
          >
            {t.runWithAi}
          </Button>
        )}
        {latest && !active && (
          <RetryRunButton run={latest} onError={onError}
            onDone={(r) => {
              setInfo(r.handedToWorkflow ? t.retryRunHanded : null);
              void reload();
            }} />
        )}
      </Stack>
      {info && <Alert severity="info" sx={{ mb: 1.5 }} onClose={() => setInfo(null)}>{info}</Alert>}
      {!data ? <Loading /> : <RunList runs={data.runs} showTask={false} />}
      <Typography variant="subtitle2" color="text.secondary" sx={{ mt: 2, mb: 1 }}>{t.deliverables}</Typography>
      {data && <DeliverableList items={data.deliverables} />}
      {data && data.reviews.length > 0 && (
        <>
          <Typography variant="subtitle2" color="text.secondary" sx={{ mt: 2, mb: 1 }}>{t.reviews}</Typography>
          <Stack spacing={1}>
            {data.reviews.map((r) => (
              <Box key={r.id} sx={{ p: 1.25, borderRadius: "14px", border: 1, borderColor: "divider" }}>
                <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 0.5 }}>
                  <StatusChip value={r.outcome} />
                  <Typography variant="caption" color="text.secondary">{r.reviewerName} · {new Date(r.createdAt).toLocaleString()}</Typography>
                </Stack>
                <Typography variant="body2" sx={{ whiteSpace: "pre-wrap" }}>{r.comments}</Typography>
              </Box>
            ))}
          </Stack>
        </>
      )}
    </Box>
  );
}
