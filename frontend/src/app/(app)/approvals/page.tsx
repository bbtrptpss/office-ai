"use client";

import { useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import {
  Alert, Box, Button, Card, CardContent, Chip, Collapse, Paper, Stack, Tab, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, Tabs, TextField, Typography,
} from "@mui/material";
import { api, Approval, Deliverable, PlanItem, thb } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";
import { Avatar, Mascot } from "@/components/cute";
import { DeliverableList, SimulatedBadge } from "@/components/agents";

const KIND_EMOJI: Record<string, string> = { Plan: "🗺️", TaskCompletion: "🎀", ToolAction: "🔧", Escalation: "🚨", Estimate: "💰", Merge: "🔀" };

export default function ApprovalsPage() {
  const { t } = useI18n();
  const [tab, setTab] = useState<"pending" | "all">("pending");
  const { data, error, reload } = useLoad(
    () => api.get<Approval[]>(`/api/approvals${tab === "pending" ? "?status=Pending" : ""}`),
    tab,
  );
  useRealtime("approvalsChanged", () => void reload());
  const reduce = useReducedMotion();

  return (
    <>
      <PageHeader title={t.approvals} />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
        <Tab value="pending" label={t.pending} />
        <Tab value="all" label={t.allItems} />
      </Tabs>
      {!data ? (
        <Loading error={error} />
      ) : data.length === 0 ? (
        <Stack sx={{ alignItems: "center", py: 6 }}>
          <Mascot size={110} />
          <Typography color="text.secondary" sx={{ mt: 1 }}>{t.noApprovals}</Typography>
        </Stack>
      ) : (
        <Stack spacing={2}>
          <AnimatePresence initial={false}>
            {data.map((a) => (
              <motion.div key={a.id} layout initial={reduce ? false : { opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }}
                exit={reduce ? undefined : { opacity: 0, x: 60 }} transition={{ type: "spring", stiffness: 380, damping: 32 }}>
                <ApprovalCard approval={a} onDone={reload} />
              </motion.div>
            ))}
          </AnimatePresence>
        </Stack>
      )}
    </>
  );
}

function ApprovalCard({ approval: a, onDone }: { approval: Approval; onDone: () => void }) {
  const { t, lang } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const [open, setOpen] = useState(a.status === "Pending");
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const kindLabel = { Plan: t.kindPlan, TaskCompletion: t.kindTaskCompletion, ToolAction: t.kindToolAction, Escalation: t.kindEscalation, Estimate: t.kindEstimate, Merge: t.kindMerge }[a.kind];
  const allowed = can(a.requiredPermission);
  // A workflow-level escalation (no task) is the budget cap: approving raises the budget.
  const budgetEscalation = a.kind === "Escalation" && !a.taskId && !!a.workflowExecutionId;
  const [newBudget, setNewBudget] = useState(() => {
    try { return String((JSON.parse(a.payloadJson).budget ?? 0) * 2); } catch { return ""; }
  });

  async function decide(approve: boolean) {
    setBusy(true);
    setMessage(null);
    try {
      await api.post(`/api/approvals/${a.id}/decision`, { approve, payloadHash: a.payloadHash, note: note || null, newBudgetUsd: budgetEscalation && newBudget ? Number(newBudget) : null });
      onDone();
    } catch (e) {
      setMessage(errorMessage(e));
      onDone();
    } finally {
      setBusy(false);
    }
  }

  const when = (s: string) => new Date(s).toLocaleString(lang === "th" ? "th-TH" : "en-GB");

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1.5} sx={{ alignItems: "center", cursor: "pointer" }} onClick={() => setOpen(!open)}>
          <Box sx={{ fontSize: 28 }} aria-hidden>{KIND_EMOJI[a.kind]}</Box>
          <Box sx={{ flex: 1, minWidth: 0 }}>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 0.5 }}>
              <Chip size="small" label={kindLabel} />
              <StatusChip value={a.status} />
              {a.isSimulated && <SimulatedBadge />}
            </Stack>
            <Typography fontWeight={600} sx={{ mt: 0.5 }}>{a.title}</Typography>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center", color: "text.secondary" }}>
              {a.requestedByName && <Avatar kind="AI" seed={a.requestedByName} size={20} />}
              <Typography variant="caption">
                {a.requestedByName ? `${t.requestedBy} ${a.requestedByName} · ` : ""}{when(a.createdAt)}
                {a.status === "Pending" ? ` · ${t.expires} ${when(a.expiresAt)}` : a.decidedAt ? ` · ${when(a.decidedAt)}` : ""}
              </Typography>
            </Stack>
          </Box>
        </Stack>

        <Collapse in={open}>
          <Box sx={{ mt: 2 }}>
            {a.details && <Typography variant="body2" sx={{ mb: 2, whiteSpace: "pre-wrap" }}>{a.details}</Typography>}
            <Payload approval={a} />
            {a.decisionNote && <Alert severity="info" sx={{ mt: 2 }}>{a.decisionNote}</Alert>}
            {a.kind === "Escalation" && a.status === "Pending" && (
              <Typography variant="caption" color="text.secondary">{budgetEscalation ? t.budgetEscalationHint : t.escalationHint}</Typography>
            )}
            {message && <Alert severity="warning" sx={{ mt: 2 }}>{message}</Alert>}
            {a.status === "Pending" && (
              <Stack direction={{ xs: "column", sm: "row" }} spacing={1} sx={{ mt: 2, alignItems: { sm: "center" } }}>
                {budgetEscalation && (
                  <TextField size="small" type="number" label={t.newBudgetUsd} value={newBudget} onChange={(e) => setNewBudget(e.target.value)}
                    sx={{ width: 170 }} disabled={!allowed} slotProps={{ htmlInput: { min: 0, step: 1 } }} />
                )}
                <TextField size="small" label={t.note} value={note} onChange={(e) => setNote(e.target.value)} sx={{ flex: 1 }} disabled={!allowed} />
                <Button color="error" variant="outlined" disabled={busy || !allowed} onClick={() => decide(false)}>{t.reject}</Button>
                <Button variant="contained" disabled={busy || !allowed} onClick={() => decide(true)}>{t.approve}</Button>
              </Stack>
            )}
            {a.status === "Pending" && !allowed && (
              <Typography variant="caption" color="text.secondary">{t.needsPermission}: {a.requiredPermission}</Typography>
            )}
          </Box>
        </Collapse>
      </CardContent>
    </Card>
  );
}

function Payload({ approval: a }: { approval: Approval }) {
  const { t, label } = useI18n();
  const payload = JSON.parse(a.payloadJson);

  if (a.kind === "Plan") {
    // Older requests stored PascalCase keys; accept both.
    const items: PlanItem[] = (payload.tasks ?? payload.Tasks ?? []).map((x: Record<string, unknown>) => ({
      key: x.key ?? x.Key, title: x.title ?? x.Title, description: x.description ?? x.Description,
      requiredSkills: x.requiredSkills ?? x.RequiredSkills ?? [], dependsOn: x.dependsOn ?? x.DependsOn ?? [], priority: x.priority ?? x.Priority,
    }) as PlanItem);
    return (
      <TableContainer component={Paper} variant="outlined">
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Key</TableCell>
              <TableCell>{t.title}</TableCell>
              <TableCell>{t.skills}</TableCell>
              <TableCell>{t.dependsOn}</TableCell>
              <TableCell>{t.priority}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {items.map((i) => (
              <TableRow key={i.key}>
                <TableCell sx={{ fontFamily: "monospace" }}>{i.key}</TableCell>
                <TableCell>
                  <Typography variant="body2" fontWeight={600}>{i.title}</Typography>
                  {i.description && <Typography variant="caption" color="text.secondary">{i.description}</Typography>}
                </TableCell>
                <TableCell>{i.requiredSkills.join(", ")}</TableCell>
                <TableCell sx={{ fontFamily: "monospace" }}>{i.dependsOn.join(", ") || "—"}</TableCell>
                <TableCell>{label(i.priority)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    );
  }
  if (a.kind === "TaskCompletion" && a.taskId) return <TaskDeliverables taskId={a.taskId} />;
  if (a.kind === "Estimate") {
    const rows: [string, string][] = [[t.hours, String(payload.totalHours)], [t.totalCost, thb(payload.totalCost)], [t.price, thb(payload.price)], [t.grossMargin, `${thb(payload.grossMargin)} (${payload.grossMarginPercent}%)`]];
    return (
      <Stack direction="row" spacing={3} sx={{ flexWrap: "wrap", rowGap: 1 }}>
        {rows.map(([k, v]) => <Box key={k}><Typography variant="caption" color="text.secondary">{k}</Typography><Typography fontWeight={600}>{v}</Typography></Box>)}
      </Stack>
    );
  }
  if (a.kind === "Merge") return <MergeDiff id={payload.mergeRequestId} branch={`${payload.sourceBranch} → ${payload.targetBranch} @ ${String(payload.headSha).slice(0, 8)}`} />;
  if (a.kind === "ToolAction") {
    return (
      <Box>
        <Typography variant="subtitle2" color="text.secondary">{t.requestedAction}: <code>{payload.toolId}</code></Typography>
        <Typography component="pre" variant="body2" sx={{ m: 0, mt: 1, p: 1.5, borderRadius: "10px", bgcolor: "action.hover", whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
          {JSON.stringify(payload.input, null, 2)}
        </Typography>
      </Box>
    );
  }
  return null;
}

function MergeDiff({ id, branch }: { id: string; branch: string }) {
  const { data } = useLoad(async () => (await api.get<{ diff: string }>(`/api/merge-requests/${id}`)).diff, id);
  return (
    <Box>
      <Typography variant="caption" sx={{ fontFamily: "monospace" }}>{branch}</Typography>
      <Typography component="pre" variant="body2" sx={{ m: 0, mt: 1, p: 1.5, borderRadius: "10px", bgcolor: "action.hover", whiteSpace: "pre-wrap", fontFamily: "monospace", fontSize: 13, maxHeight: 400, overflow: "auto" }}>
        {data ?? "…"}
      </Typography>
    </Box>
  );
}

function TaskDeliverables({ taskId }: { taskId: string }) {
  const { data } = useLoad(() => api.get<Deliverable[]>(`/api/tasks/${taskId}/deliverables`), taskId);
  return data ? <DeliverableList items={data.slice(0, 1)} /> : <Loading />;
}
