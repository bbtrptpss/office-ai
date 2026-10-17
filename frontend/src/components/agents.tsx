"use client";

import { useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import {
  Alert, Box, Button, Card, CardActionArea, Chip, Drawer, IconButton, Stack, Tooltip, Typography,
} from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import BuildIcon from "@mui/icons-material/BuildOutlined";
import ChatBubbleIcon from "@mui/icons-material/ChatBubbleOutline";
import InfoIcon from "@mui/icons-material/InfoOutlined";
import StopIcon from "@mui/icons-material/StopCircleOutlined";
import { api, AgentRun, AgentRunDetail, Deliverable, usd } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, StatusChip, useErrorMessage, useLoad } from "./common";
import { Avatar } from "./cute";
import { brand } from "@/theme";

export function SimulatedBadge() {
  const { t } = useI18n();
  return (
    <Tooltip title={t.simulatedHint}>
      <Chip size="small" label={`🧪 ${t.simulated}`} sx={{ bgcolor: brand.pinkSoft, color: brand.ink, fontWeight: 600 }} />
    </Tooltip>
  );
}

export function duration(run: AgentRun) {
  if (!run.startedAt) return "—";
  const end = run.finishedAt ? new Date(run.finishedAt).getTime() : Date.now();
  const s = Math.max(0, Math.round((end - new Date(run.startedAt).getTime()) / 1000));
  return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${s % 60}s`;
}

const isActive = (r: AgentRun) => r.status === "Queued" || r.status === "Running";

/** A list of runs as cute cards; clicking one opens its transcript. */
export function RunList({ runs, showTask = true }: { runs: AgentRun[]; showTask?: boolean }) {
  const { t } = useI18n();
  const reduce = useReducedMotion();
  const [openId, setOpenId] = useState<string | null>(null);

  if (runs.length === 0) return <Typography color="text.secondary">{t.noRuns}</Typography>;
  return (
    <>
      <Stack spacing={1}>
        <AnimatePresence initial={false}>
          {runs.map((r) => (
            <motion.div
              key={r.id}
              layout
              initial={reduce ? false : { opacity: 0, y: -8 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ type: "spring", stiffness: 400, damping: 32 }}
            >
              <Card>
                <CardActionArea onClick={() => setOpenId(r.id)} sx={{ p: 1.5 }}>
                  <Stack direction="row" spacing={1.5} sx={{ alignItems: "center" }}>
                    <Box sx={{ position: "relative" }}>
                      <Avatar kind="AI" seed={r.employeeName} size={40} />
                      {isActive(r) && !reduce && (
                        <motion.span
                          style={{ position: "absolute", inset: -4, borderRadius: "50%", border: `2px solid ${brand.pink}` }}
                          animate={{ scale: [1, 1.15, 1], opacity: [0.9, 0.3, 0.9] }}
                          transition={{ duration: 1.4, repeat: Infinity }}
                        />
                      )}
                    </Box>
                    <Box sx={{ flex: 1, minWidth: 0 }}>
                      <Typography fontWeight={600} noWrap>
                        {showTask ? r.taskTitle : r.employeeName}
                      </Typography>
                      <Typography variant="body2" color="text.secondary" noWrap>
                        {showTask ? `${r.employeeName} · ` : ""}{r.modelId} · {r.turns} {t.turns} · {r.toolCalls} tools · {duration(r)}
                      </Typography>
                    </Box>
                    <Stack spacing={0.5} sx={{ alignItems: "flex-end" }}>
                      <Stack direction="row" spacing={0.5}>
                        {r.isSimulated && <SimulatedBadge />}
                        <StatusChip value={r.status} />
                      </Stack>
                      <Typography variant="caption" color="text.secondary">{usd(r.costUsd)}</Typography>
                    </Stack>
                  </Stack>
                </CardActionArea>
              </Card>
            </motion.div>
          ))}
        </AnimatePresence>
      </Stack>
      <RunDrawer runId={openId} onClose={() => setOpenId(null)} />
    </>
  );
}

/** Side panel with a run's transcript, tool executions and stop reason. Updates live. */
export function RunDrawer({ runId, onClose }: { runId: string | null; onClose: () => void }) {
  return (
    <Drawer anchor="right" open={!!runId} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: "100%", sm: 520 }, borderRadius: { sm: "26px 0 0 26px" } } } }}>
      {runId && <RunDetailView runId={runId} onClose={onClose} />}
    </Drawer>
  );
}

function RunDetailView({ runId, onClose }: { runId: string; onClose: () => void }) {
  const { t, lang } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const reduce = useReducedMotion();
  const [message, setMessage] = useState<string | null>(null);
  const { data, error, reload } = useLoad(() => api.get<AgentRunDetail>(`/api/agent-runs/${runId}`), runId);
  useRealtime<{ runId: string }>("agentRunChanged", (e) => {
    if (e.runId === runId) void reload();
  });

  if (!data) return <Box sx={{ p: 3 }}><Loading error={error} /></Box>;
  const { run, steps, toolExecutions } = data;
  const execByCall = new Map(toolExecutions.map((x) => [x.toolCallId, x]));

  return (
    <Box sx={{ p: 2.5 }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 2 }}>
        <Avatar kind="AI" seed={run.employeeName} size={44} />
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="h6" noWrap>{run.taskTitle}</Typography>
          <Typography variant="body2" color="text.secondary" noWrap>{run.employeeName} · {run.provider}/{run.modelId}</Typography>
        </Box>
        <IconButton onClick={onClose} aria-label="close"><CloseIcon /></IconButton>
      </Stack>

      <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", rowGap: 1, mb: 2 }}>
        <StatusChip value={run.status} />
        {run.isSimulated && <SimulatedBadge />}
        <Chip size="small" variant="outlined" label={`${t.turns} ${run.turns}`} />
        <Chip size="small" variant="outlined" label={`${t.tokens} ${(run.inputTokens + run.outputTokens).toLocaleString()}`} />
        <Chip size="small" variant="outlined" label={`${t.cost} ${usd(run.costUsd)}`} />
        <Chip size="small" variant="outlined" label={`${t.duration} ${duration(run)}`} />
      </Stack>

      {run.error && <Alert severity={run.status === "Failed" ? "error" : "warning"} sx={{ mb: 2 }}>{run.error}</Alert>}
      {message && <Alert severity="warning" sx={{ mb: 2 }}>{message}</Alert>}
      {isActive(run) && can("agent.run") && (
        <Button color="error" variant="outlined" startIcon={<StopIcon />} sx={{ mb: 2 }}
          onClick={async () => {
            try {
              await api.post(`/api/agent-runs/${run.id}/cancel`);
            } catch (e) {
              setMessage(errorMessage(e));
            }
          }}>
          {t.cancelRun}
        </Button>
      )}

      <Typography variant="subtitle2" color="text.secondary" gutterBottom>{t.transcript}</Typography>
      <Stack spacing={1}>
        {steps.map((s, i) => {
          const exec = s.toolCallId ? execByCall.get(s.toolCallId) : undefined;
          const tone = s.kind === "AssistantText" ? brand.lilac : s.kind === "Notice" ? "#FFF1D6" : "transparent";
          return (
            <motion.div key={s.sequence} initial={reduce ? false : { opacity: 0, x: 12 }} animate={{ opacity: 1, x: 0 }} transition={{ delay: Math.min(i, 10) * 0.03 }}>
              <Box sx={{ p: 1.25, borderRadius: 3, border: 1, borderColor: "divider", bgcolor: tone, color: tone === "transparent" ? undefined : brand.ink }}>
                <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 0.5 }}>
                  {s.kind === "ToolCall" || s.kind === "ToolResult" ? <BuildIcon fontSize="small" /> : s.kind === "Notice" ? <InfoIcon fontSize="small" /> : <ChatBubbleIcon fontSize="small" />}
                  <Typography variant="caption" fontWeight={600}>{s.kind}</Typography>
                  {s.kind === "ToolResult" && exec && <StatusChip value={exec.status} />}
                  <Box sx={{ flex: 1 }} />
                  <Typography variant="caption" color="text.secondary">{new Date(s.createdAt).toLocaleTimeString(lang === "th" ? "th-TH" : "en-GB")}</Typography>
                </Stack>
                <Typography
                  variant="body2"
                  component="pre"
                  sx={{ m: 0, whiteSpace: "pre-wrap", wordBreak: "break-word", fontFamily: s.kind.startsWith("Tool") ? "monospace" : "inherit", maxHeight: s.kind === "Instructions" ? 160 : 320, overflow: "auto" }}
                >
                  {s.content}
                </Typography>
              </Box>
            </motion.div>
          );
        })}
        {isActive(run) && !reduce && (
          <Stack direction="row" spacing={0.75} sx={{ alignItems: "center", pl: 1 }}>
            {[0, 1, 2].map((i) => (
              <motion.span key={i} style={{ width: 8, height: 8, borderRadius: 4, background: brand.purple, display: "inline-block" }}
                animate={{ y: [0, -6, 0] }} transition={{ duration: 0.8, repeat: Infinity, delay: i * 0.15 }} />
            ))}
          </Stack>
        )}
      </Stack>
    </Box>
  );
}

/** Deliverables handed in for a task. */
export function DeliverableList({ items }: { items: Deliverable[] }) {
  const { t } = useI18n();
  if (items.length === 0) return <Typography variant="body2" color="text.secondary">—</Typography>;
  return (
    <Stack spacing={1}>
      {items.map((d) => (
        <Box key={d.id} sx={{ p: 1.5, borderRadius: 3, border: 1, borderColor: "divider" }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 0.5 }}>
            <Typography fontWeight={600} sx={{ flex: 1 }}>{d.title}</Typography>
            {d.isSimulated && <SimulatedBadge />}
            <StatusChip value={d.status} />
          </Stack>
          <Typography variant="caption" color="text.secondary">{d.employeeName} · {new Date(d.createdAt).toLocaleString()}</Typography>
          <Typography variant="body2" component="pre" sx={{ whiteSpace: "pre-wrap", wordBreak: "break-word", m: 0, mt: 1, maxHeight: 240, overflow: "auto" }}>
            {d.content}
          </Typography>
        </Box>
      ))}
      <Typography variant="caption" color="text.secondary">{t.deliverables}: {items.length}</Typography>
    </Stack>
  );
}
