"use client";

import { useState } from "react";
import { Box, Card, CardActionArea, CardContent, Chip, FormControlLabel, MenuItem, Stack, Switch, TextField, Typography } from "@mui/material";
import Link from "next/link";
import { api, BoardTask, Employee, Project, TaskPriority, TaskStatus } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { KindIcon, Loading, PageHeader, StatusChip, useLoad } from "@/components/common";

const COLUMNS: TaskStatus[] = ["Todo", "Assigned", "InProgress", "Blocked", "InReview", "WaitingApproval", "Done", "Failed"];
const PRIORITIES: TaskPriority[] = ["Low", "Medium", "High", "Critical"];
const COLUMN_COLORS: Record<TaskStatus, string> = {
  Todo: "#CDB8F5", Assigned: "#A9C7FF", InProgress: "#7B4FC9", Blocked: "#FF8A8A", InReview: "#FF7EB9",
  WaitingApproval: "#FFC56E", Done: "#7ED6A8", Failed: "#E06060", Cancelled: "#BDB6CC",
};

/** Company-wide task board: every open project's tasks in one place, filtered by project, person, priority or overdue. */
export default function TaskBoardPage() {
  const { t, label } = useI18n();
  const [projectId, setProjectId] = useState("");
  const [employeeId, setEmployeeId] = useState("");
  const [priority, setPriority] = useState<TaskPriority | "">("");
  const [overdue, setOverdue] = useState(false);

  const query = new URLSearchParams();
  if (projectId) query.set("projectId", projectId);
  if (employeeId) query.set("employeeId", employeeId);
  if (priority) query.set("priority", priority);
  if (overdue) query.set("overdue", "true");
  const qs = query.toString();

  const { data, error, reload } = useLoad(() => api.get<BoardTask[]>(`/api/tasks/board${qs ? `?${qs}` : ""}`), qs);
  const { data: lookups } = useLoad(async () => {
    const [projects, employees] = await Promise.all([api.get<Project[]>("/api/projects"), api.get<Employee[]>("/api/employees")]);
    return { projects: projects.filter((p) => p.status !== "Cancelled" && p.status !== "Completed"), employees };
  });
  useRealtime("taskChanged", () => void reload());

  return (
    <>
      <PageHeader title={t.navTaskBoard} subtitle={t.taskBoardSubtitle} />
      <Stack direction={{ xs: "column", md: "row" }} spacing={1.5} sx={{ mb: 2, alignItems: { md: "center" } }}>
        <TextField select size="small" label={t.projects} value={projectId} onChange={(e) => setProjectId(e.target.value)} sx={{ minWidth: 200 }}>
          <MenuItem value="">{t.allItems}</MenuItem>
          {lookups?.projects.map((p) => <MenuItem key={p.id} value={p.id}>{p.code} {p.name}</MenuItem>)}
        </TextField>
        <TextField select size="small" label={t.assignee} value={employeeId} onChange={(e) => setEmployeeId(e.target.value)} sx={{ minWidth: 200 }}>
          <MenuItem value="">{t.allItems}</MenuItem>
          {lookups?.employees.map((e) => <MenuItem key={e.id} value={e.id}>{e.displayName}</MenuItem>)}
        </TextField>
        <TextField select size="small" label={t.priority} value={priority} onChange={(e) => setPriority(e.target.value as TaskPriority | "")} sx={{ minWidth: 150 }}>
          <MenuItem value="">{t.allItems}</MenuItem>
          {PRIORITIES.map((p) => <MenuItem key={p} value={p}>{label(p)}</MenuItem>)}
        </TextField>
        <FormControlLabel control={<Switch checked={overdue} onChange={(e) => setOverdue(e.target.checked)} />} label={t.overdueOnly} />
      </Stack>

      {!data ? <Loading error={error} /> : (
        <>
          {data.length >= 500 && <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{t.boardLimitNote}</Typography>}
          <Box sx={{ display: "flex", gap: 1.5, overflowX: "auto", pb: 2 }}>
            {COLUMNS.map((status) => {
              const items = data.filter((x) => x.task.status === status);
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
                    <Typography fontWeight={600} sx={{ fontFamily: "var(--font-cute)" }}>{label(status)}</Typography>
                    <Chip size="small" label={items.length} />
                  </Stack>
                  <Stack spacing={1}>
                    {items.map(({ task, projectCode, isOverdue }) => (
                      <Card key={task.id}>
                        <CardActionArea component={Link} href={`/projects/${task.projectId}?task=${task.id}`}>
                          <CardContent sx={{ p: 1.5, "&:last-child": { pb: 1.5 } }}>
                            <Typography variant="caption" color="text.secondary">{projectCode}</Typography>
                            <Typography fontWeight={500} sx={{ mb: 0.5 }}>{task.title}</Typography>
                            <Stack direction="row" spacing={0.5} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 0.5 }}>
                              <StatusChip value={task.priority} />
                              {task.complexity !== "Standard" && <Chip size="small" variant="outlined" label={label(task.complexity)} />}
                              {isOverdue && <Chip size="small" color="error" label={`${t.overdue} · ${task.dueDate}`} />}
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
                    ))}
                  </Stack>
                </Box>
              );
            })}
          </Box>
        </>
      )}
    </>
  );
}
