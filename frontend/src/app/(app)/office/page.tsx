"use client";

import { useMemo, useState } from "react";
import dynamic from "next/dynamic";
import Link from "next/link";
import { Box, Button, Card, CardContent, Chip, IconButton, Stack, Typography } from "@mui/material";
import CloseIcon from "@mui/icons-material/CloseRounded";
import { AgentRun, api, Department, Employee, EmployeeStatus, OrgLevel, RoleDefinition } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, useLoad } from "@/components/common";
import { Avatar } from "@/components/cute";
import type { OfficeRoom } from "@/components/office3d";
import { OrderDialog, OrderTarget } from "@/components/officeOrder";

// WebGL only exists in the browser.
const Office3D = dynamic(() => import("@/components/office3d"), { ssr: false, loading: () => <Loading /> });

const LEGEND: { status: EmployeeStatus; icon: string }[] = [
  { status: "Working", icon: "⌨️" },
  { status: "Available", icon: "🚶" },
  { status: "WaitingForApproval", icon: "✋" },
  { status: "Blocked", icon: "🧱" },
  { status: "Failed", icon: "⚠️" },
  { status: "Inactive", icon: "💤" },
];

const RANK: Partial<Record<OrgLevel, number>> = { Executive: 2, Head: 1 };

/**
 * Who heads each department: the most senior Head- or Executive-level people in it, and among equals the one who
 * reports outside the department (the CEO, not the other executives). A department of staff has no head.
 */
function departmentHeads(employees: Employee[], roles: RoleDefinition[]) {
  const level = new Map(roles.map((r) => [r.id, r.level]));
  const departmentOf = new Map(employees.map((e) => [e.id, e.departmentId]));
  const rank = (e: Employee) => RANK[level.get(e.roleDefinitionId) ?? "Staff"] ?? 0;
  const heads = new Set<string>();
  for (const dept of new Set(employees.map((e) => e.departmentId))) {
    const people = employees.filter((e) => e.departmentId === dept);
    const top = Math.max(0, ...people.map(rank));
    if (top === 0) continue;
    const senior = people.filter((e) => rank(e) === top);
    const outside = senior.filter((e) => !e.managerId || departmentOf.get(e.managerId) !== dept);
    for (const e of outside.length > 0 ? outside : senior) heads.add(e.id);
  }
  return heads;
}

export default function OfficePage() {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const canRuns = can("agent.read");
  const { data, error, reload } = useLoad(async () => {
    const [departments, employees, roles, runs] = await Promise.all([
      api.get<Department[]>("/api/departments"),
      api.get<Employee[]>("/api/employees"),
      api.get<RoleDefinition[]>("/api/role-definitions"),
      canRuns ? api.get<AgentRun[]>("/api/agent-runs?take=100") : Promise.resolve([] as AgentRun[]),
    ]);
    return { departments, employees, roles, runs };
  }, String(canRuns));
  // Statuses change when runs start and finish; both events refresh the scene.
  useRealtime("organizationChanged", () => void reload());
  useRealtime("agentRunChanged", () => void reload());
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [order, setOrder] = useState<OrderTarget | null>(null);
  const canOrder = can("task.manage") || can("project.manage");

  const rooms = useMemo<OfficeRoom[]>(() => {
    if (!data) return [];
    const heads = departmentHeads(data.employees, data.roles);
    const activeTask = new Map(data.runs.filter((r) => r.status === "Queued" || r.status === "Running").map((r) => [r.employeeId, r.taskTitle]));
    return [...data.departments]
      .filter((d) => d.isActive)
      .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name))
      .map((d) => ({
        id: d.id,
        name: d.name,
        people: data.employees.filter((e) => e.departmentId === d.id).map((e) => ({
          id: e.id, name: e.displayName, jobTitle: e.jobTitle, kind: e.kind, status: e.status, taskTitle: activeTask.get(e.id),
          head: heads.has(e.id),
        })),
      }))
      .filter((r) => r.people.length > 0);
  }, [data]);

  if (!data) return <Loading error={error} />;
  const counts = new Map<EmployeeStatus, number>();
  for (const e of data.employees) counts.set(e.status, (counts.get(e.status) ?? 0) + 1);
  const selected = selectedId ? data.employees.find((e) => e.id === selectedId) : undefined;
  const selectedTask = selected && rooms.flatMap((r) => r.people).find((p) => p.id === selected.id)?.taskTitle;

  return (
    <Stack spacing={2}>
      <Stack direction={{ xs: "column", md: "row" }} spacing={1.5} sx={{ alignItems: { md: "center" } }}>
        <Box sx={{ flex: 1 }}>
          <Typography variant="h5">{t.navOffice}</Typography>
          <Typography variant="body2" color="text.secondary">{t.officeHint}</Typography>
        </Box>
        <Stack direction="row" sx={{ flexWrap: "wrap", gap: 1 }}>
          {LEGEND.filter((l) => counts.get(l.status)).map((l) => (
            <Chip key={l.status} label={`${l.icon} ${label(l.status)} ${counts.get(l.status)}`} sx={{ bgcolor: "background.paper" }} />
          ))}
        </Stack>
      </Stack>

      <Card sx={{ position: "relative", height: { xs: "70vh", md: "calc(100vh - 210px)" }, minHeight: 420, overflow: "hidden", p: 0,
        background: "linear-gradient(180deg, #2B1B58 0%, #8E6BD6 50%, #F7B9D7 100%)" }}>
        <Office3D rooms={rooms} selectedId={selectedId} onSelect={setSelectedId} labels={(s) => label(s)} loungeName={t.officeLounge} roomHint={t.officeRoomHint}
          onRoomClick={canOrder ? (id) => {
            const dept = data.departments.find((d) => d.id === id);
            if (dept) setOrder({ kind: "department", id: dept.id, name: dept.name, members: data.employees.filter((e) => e.departmentId === id) });
          } : undefined} />
        {selected && (
          <Card sx={{ position: "absolute", top: 12, right: 12, width: 280, maxWidth: "calc(100% - 24px)", zIndex: 30 /* above the 3D name signs */ }}>
            <CardContent>
              <Stack direction="row" spacing={1.5} sx={{ alignItems: "center" }}>
                <Avatar kind={selected.kind} seed={selected.displayName} size={44} />
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography fontWeight={700} noWrap>{selected.displayName}</Typography>
                  <Typography variant="caption" color="text.secondary" noWrap component="div">{selected.jobTitle} · {selected.departmentName}</Typography>
                </Box>
                <IconButton size="small" onClick={() => setSelectedId(null)} aria-label={t.close}><CloseIcon fontSize="small" /></IconButton>
              </Stack>
              <Chip size="small" label={label(selected.status)} color={selected.status === "Working" ? "primary" : selected.status === "Failed" ? "error" : "default"} sx={{ mt: 1.5 }} />
              {selectedTask && <Typography variant="body2" sx={{ mt: 1 }}>⌨️ {selectedTask}</Typography>}
              <Stack direction="row" spacing={1} sx={{ mt: 1.5 }}>
                {canOrder && <Button variant="contained" size="small" onClick={() => setOrder({ kind: "person", employee: selected })}>{t.orderButton}</Button>}
                <Button component={Link} href={`/employees/${selected.id}`} size="small">{t.officeProfile}</Button>
              </Stack>
            </CardContent>
          </Card>
        )}
        <OrderDialog target={order} onClose={() => setOrder(null)} />
        <Typography variant="caption" color="text.secondary" sx={{ position: "absolute", left: 16, bottom: 10, pointerEvents: "none" }}>
          {t.officeControls}
        </Typography>
      </Card>
    </Stack>
  );
}
