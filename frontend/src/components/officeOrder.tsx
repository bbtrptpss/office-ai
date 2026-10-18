"use client";

import { useState } from "react";
import Link from "next/link";
import {
  Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Radio, RadioGroup, Stack, Switch, TextField, Typography,
} from "@mui/material";
import { api, Employee, Project, Task, TaskPriority } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useLoad } from "@/components/common";

/** Who the order is for: one person, or a department (planned by an AI there, or handed to one member). */
export type OrderTarget =
  | { kind: "person"; employee: Employee }
  | { kind: "department"; id: string; name: string; members: Employee[] };

const PRIORITIES: TaskPriority[] = ["Low", "Medium", "High", "Critical"];

/** The department's head: the member whose manager is outside the department, preferring an AI who can plan. */
export function departmentHead(members: Employee[]) {
  const ids = new Set(members.map((m) => m.id));
  const heads = members.filter((m) => !m.managerId || !ids.has(m.managerId));
  return heads.find((m) => m.kind === "AI") ?? members.find((m) => m.kind === "AI") ?? heads[0] ?? members[0];
}

/**
 * Gives work from the 3D office. A person (or one department member) gets a task in the chosen project, assigned to them,
 * and an AI starts on it at once. A department objective is planned by one of its AI employees; the plan waits for approval.
 */
export function OrderDialog({ target, onClose }: { target: OrderTarget | null; onClose: () => void }) {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const canTask = can("task.manage") && can("task.assign");
  const canRun = can("agent.run");
  const canPlan = can("project.manage");
  const { data: projects } = useLoad(async () => (await api.get<Project[]>("/api/projects")).filter((p) => p.status !== "Completed" && p.status !== "Cancelled"));

  const [projectId, setProjectId] = useState("");
  const [text, setText] = useState("");
  const [priority, setPriority] = useState<TaskPriority>("Medium");
  const [start, setStart] = useState(true);
  const [mode, setMode] = useState<"plan" | "single">("plan");
  const [assigneeId, setAssigneeId] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; text: string; projectId?: string; approvals?: boolean } | null>(null);

  const members = target?.kind === "department" ? target.members : [];
  const aiMembers = members.filter((m) => m.kind === "AI");
  const head = target?.kind === "department" ? departmentHead(members) : undefined;
  const plannerId = assigneeId && aiMembers.some((m) => m.id === assigneeId) ? assigneeId : (head?.kind === "AI" ? head.id : aiMembers[0]?.id ?? "");
  const person = target?.kind === "person" ? target.employee : members.find((m) => m.id === (assigneeId || head?.id));
  const planning = target?.kind === "department" && mode === "plan";
  const project = projectId || projects?.[0]?.id || "";
  const allowed = planning ? canPlan && !!plannerId : canTask;

  const close = () => {
    setText(""); setResult(null); setAssigneeId(""); setMode("plan"); setBusy(false);
    onClose();
  };

  const submit = async () => {
    setBusy(true);
    setResult(null);
    try {
      if (planning) {
        await api.post(`/api/projects/${project}/objectives`, { objective: text.trim(), plannerEmployeeId: plannerId, departmentId: target?.kind === "department" ? target.id : undefined });
        setResult({ ok: true, text: t.orderPlanned, projectId: project, approvals: true });
      } else if (person) {
        const lines = text.trim().split("\n");
        const title = lines[0].slice(0, 200);
        const task = await api.post<Task>(`/api/projects/${project}/tasks`, { title, description: text.trim(), priority });
        await api.post(`/api/tasks/${task.id}/assign`, { employeeId: person.id });
        if (person.kind === "AI" && start && canRun) {
          try {
            await api.post(`/api/tasks/${task.id}/runs`, {});
            setResult({ ok: true, text: t.orderStarted, projectId: project });
          } catch (e) {
            setResult({ ok: false, text: `${t.orderCreatedNotStarted} ${e instanceof Error ? e.message : String(e)}`, projectId: project });
          }
        } else {
          setResult({ ok: true, text: t.orderAssigned, projectId: project });
        }
      }
      setText("");
    } catch (e) {
      setResult({ ok: false, text: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusy(false);
    }
  };

  if (!target) return null;
  const title = target.kind === "person" ? `${t.orderTo} ${target.employee.displayName}` : `${t.orderTo} ${target.name}`;
  return (
    <Dialog open onClose={close} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {target.kind === "person" && (
            <Typography variant="body2" color="text.secondary">{target.employee.jobTitle} · {label(target.employee.status)}</Typography>
          )}
          {target.kind === "department" && (
            <RadioGroup value={mode} onChange={(e) => { setMode(e.target.value as "plan" | "single"); setAssigneeId(""); }}>
              <FormControlLabel value="plan" control={<Radio />} disabled={aiMembers.length === 0}
                label={<><b>{t.orderModePlan}</b><Typography variant="caption" color="text.secondary" component="div">{t.orderModePlanHint}</Typography></>} />
              <FormControlLabel value="single" control={<Radio />}
                label={<><b>{t.orderModeSingle}</b><Typography variant="caption" color="text.secondary" component="div">{t.orderModeSingleHint}</Typography></>} />
            </RadioGroup>
          )}
          {target.kind === "department" && (
            <TextField select label={planning ? t.orderPlanner : t.orderAssignee} value={planning ? plannerId : person?.id ?? ""} onChange={(e) => setAssigneeId(e.target.value)}>
              {(planning ? aiMembers : members).map((m) => (
                <MenuItem key={m.id} value={m.id}>{m.displayName}{m.id === head?.id ? ` · ${t.orderHead}` : ""} — {label(m.status)}</MenuItem>
              ))}
            </TextField>
          )}
          <TextField select label={t.orderProject} value={project} onChange={(e) => setProjectId(e.target.value)} helperText={projects?.length === 0 ? t.orderNoProject : undefined}>
            {(projects ?? []).map((p) => <MenuItem key={p.id} value={p.id}>{p.code} · {p.name}</MenuItem>)}
          </TextField>
          <TextField label={planning ? t.orderObjective : t.orderWhat} placeholder={planning ? t.orderObjectivePlaceholder : t.orderWhatPlaceholder}
            multiline minRows={3} maxRows={10} value={text} onChange={(e) => setText(e.target.value)} slotProps={{ htmlInput: { maxLength: 4000 } }}
            helperText={planning ? undefined : t.orderFirstLineTitle} />
          {!planning && (
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { sm: "center" } }}>
              <TextField select size="small" label={t.orderPriority} value={priority} onChange={(e) => setPriority(e.target.value as TaskPriority)} sx={{ minWidth: 160 }}>
                {PRIORITIES.map((p) => <MenuItem key={p} value={p}>{label(p)}</MenuItem>)}
              </TextField>
              {person?.kind === "AI" && canRun && (
                <FormControlLabel control={<Switch checked={start} onChange={(e) => setStart(e.target.checked)} />} label={t.orderStartNow} />
              )}
            </Stack>
          )}
          {!allowed && <Alert severity="warning">{planning ? t.orderNeedPlanPermission : t.orderNeedTaskPermission}</Alert>}
          {result && (
            <Alert severity={result.ok ? "success" : "error"} action={result.projectId && (
              <Stack direction="row" spacing={1}>
                {result.approvals && <Button size="small" component={Link} href="/approvals">{t.navApprovals}</Button>}
                <Button size="small" component={Link} href={`/projects/${result.projectId}`}>{t.orderOpenProject}</Button>
              </Stack>
            )}>{result.text}</Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>{t.close}</Button>
        <Button variant="contained" disabled={busy || !allowed || !project || text.trim().length === 0 || (!planning && !person)} onClick={submit}>
          {planning ? t.orderSendPlan : t.orderSend}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
