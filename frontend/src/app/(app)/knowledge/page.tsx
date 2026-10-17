"use client";

import { useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import {
  Alert, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, Tab, Tabs, TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import { api, KnowledgeEntry } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";
import { Mascot } from "@/components/cute";

const KINDS = ["Policy", "Standard", "Decision", "Lesson", "Template", "Reference"] as const;

export default function KnowledgePage() {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const reduce = useReducedMotion();
  const [status, setStatus] = useState<KnowledgeEntry["status"]>("Validated");
  const [query, setQuery] = useState("");
  const [applied, setApplied] = useState("");
  const { data, error, reload } = useLoad(
    () => api.get<KnowledgeEntry[]>(`/api/knowledge?status=${status}${applied ? `&q=${encodeURIComponent(applied)}` : ""}`),
    `${status}:${applied}`,
  );
  const [form, setForm] = useState<{ kind: KnowledgeEntry["kind"]; title: string; body: string; tags: string } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const canManage = can("knowledge.manage");

  async function act(fn: () => Promise<unknown>) {
    setMessage(null);
    try { await fn(); } catch (e) { setMessage(errorMessage(e)); }
    await reload();
  }

  return (
    <>
      <PageHeader title={t.knowledge}
        actions={canManage && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setForm({ kind: "Standard", title: "", body: "", tags: "" })}>{t.newKnowledge}</Button>} />
      <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ mb: 2, alignItems: { sm: "center" } }}>
        <Tabs value={status} onChange={(_, v) => setStatus(v)} sx={{ flex: 1 }}>
          {(["Validated", "Proposed", "Archived"] as const).map((s) => <Tab key={s} value={s} label={label(s)} />)}
        </Tabs>
        <TextField size="small" label={t.search} value={query} onChange={(e) => setQuery(e.target.value)} onKeyDown={(e) => e.key === "Enter" && setApplied(query.trim())} />
      </Stack>
      {message && <Alert severity="warning" sx={{ mb: 2 }}>{message}</Alert>}
      {!data ? <Loading error={error} /> : data.length === 0 ? (
        <Stack sx={{ alignItems: "center", py: 6 }}><Mascot size={96} /><Typography color="text.secondary">{t.empty}</Typography></Stack>
      ) : (
        <Stack spacing={1.5}>
          <AnimatePresence initial={false}>
            {data.map((k) => (
              <motion.div key={k.id} layout initial={reduce ? false : { opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} exit={reduce ? undefined : { opacity: 0 }}>
                <Card>
                  <CardContent>
                    <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1, mb: 1 }}>
                      <Chip size="small" label={label(k.kind)} />
                      <Typography fontWeight={600} sx={{ flex: 1 }}>{k.title}</Typography>
                      {k.projectName && <Chip size="small" variant="outlined" label={`📁 ${k.projectName}`} />}
                      <StatusChip value={k.status} />
                    </Stack>
                    <Typography variant="body2" sx={{ whiteSpace: "pre-wrap", maxHeight: 200, overflow: "auto" }}>{k.body}</Typography>
                    <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1 }}>
                      {k.authorName ? `${k.authorName} · ` : ""}{k.source ? `${t.source}: ${k.source} · ` : ""}{k.tags.map((x) => `#${x}`).join(" ")}
                    </Typography>
                    {canManage && (
                      <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                        {k.status !== "Validated" && <Button size="small" variant="contained" onClick={() => act(() => api.post(`/api/knowledge/${k.id}/status`, { status: "Validated" }))}>{t.validate}</Button>}
                        {k.status !== "Archived" && <Button size="small" onClick={() => act(() => api.post(`/api/knowledge/${k.id}/status`, { status: "Archived" }))}>{t.archive}</Button>}
                      </Stack>
                    )}
                  </CardContent>
                </Card>
              </motion.div>
            ))}
          </AnimatePresence>
        </Stack>
      )}
      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="md">
        <DialogTitle>{t.newKnowledge}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField select label={t.kindLabel} value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value as KnowledgeEntry["kind"] })}>
                {KINDS.map((k) => <MenuItem key={k} value={k}>{label(k)}</MenuItem>)}
              </TextField>
              <TextField label={t.title} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
              <TextField label={t.description} value={form.body} onChange={(e) => setForm({ ...form, body: e.target.value })} multiline minRows={6} />
              <TextField label={t.tags} helperText="comma, separated" value={form.tags} onChange={(e) => setForm({ ...form, tags: e.target.value })} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!form?.title || !form?.body} onClick={() => act(async () => {
            await api.post("/api/knowledge", { kind: form!.kind, title: form!.title, body: form!.body, tags: form!.tags.split(",").map((x) => x.trim()).filter(Boolean) });
            setForm(null);
          })}>{t.create}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
