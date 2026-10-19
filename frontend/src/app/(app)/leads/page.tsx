"use client";

import { useState } from "react";
import Link from "next/link";
import {
  Alert, Box, Button, Card, CardActionArea, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Divider, FormControlLabel,
  Stack, Switch, TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import CheckCircleIcon from "@mui/icons-material/CheckCircleOutline";
import ErrorIcon from "@mui/icons-material/ErrorOutline";
import RadioButtonUncheckedIcon from "@mui/icons-material/RadioButtonUnchecked";
import { api, Lead } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, useErrorMessage, useLoad } from "@/components/common";
import { Mascot } from "@/components/cute";

const STATUS_COLOR = { New: "info", Clarifying: "warning", Qualified: "success", Converted: "primary", Lost: "default" } as const;

type Fields = {
  title: string; customerName: string; contactName: string; contactEmail: string; contactPhone: string; source: string; need: string;
  businessGoal: string; targetUsers: string; platforms: string; constraints: string; budget: string; desiredDueDate: string;
};
const EMPTY: Fields = {
  title: "", customerName: "", contactName: "", contactEmail: "", contactPhone: "", source: "", need: "",
  businessGoal: "", targetUsers: "", platforms: "", constraints: "", budget: "", desiredDueDate: "",
};
const fieldsOf = (l: Lead): Fields => ({
  title: l.title, customerName: l.customerName, contactName: l.contactName ?? "", contactEmail: l.contactEmail ?? "", contactPhone: l.contactPhone ?? "",
  source: l.source ?? "", need: l.need, businessGoal: l.businessGoal ?? "", targetUsers: l.targetUsers ?? "", platforms: l.platforms ?? "",
  constraints: l.constraints ?? "", budget: l.budget?.toString() ?? "", desiredDueDate: l.desiredDueDate ?? "",
});
const bodyOf = (f: Fields) => ({
  ...f, budget: f.budget.trim() === "" ? null : Number(f.budget), desiredDueDate: f.desiredDueDate || null,
});

/** Customer intake (Master Prompt §7 stage 1): what is missing, the questions asked, and the conversion into a project. */
export default function LeadsPage() {
  const { t, lang } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<Lead[]>("/api/leads"));
  const [showClosed, setShowClosed] = useState(false);
  const [creating, setCreating] = useState<Fields | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const canManage = can("project.manage");

  if (!data) return <Loading error={error} />;
  const shown = showClosed ? data : data.filter((l) => l.status !== "Converted" && l.status !== "Lost");
  const selected = data.find((l) => l.id === openId) ?? null;

  const create = async () => {
    setMessage(null);
    try {
      const l = await api.post<Lead>("/api/leads", bodyOf(creating!));
      setCreating(null);
      await reload();
      setOpenId(l.id);
    } catch (e) { setMessage(errorMessage(e)); }
  };

  return (
    <>
      <PageHeader title={t.navLeads} subtitle={t.leadsNote}
        actions={canManage && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating({ ...EMPTY })}>{t.newLead}</Button>} />
      <FormControlLabel sx={{ mb: 1 }} control={<Switch size="small" checked={showClosed} onChange={(e) => setShowClosed(e.target.checked)} />} label={t.leadShowClosed} />
      {message && !creating && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}

      {shown.length === 0 ? (
        <Stack sx={{ alignItems: "center", py: 6 }}><Mascot size={96} /><Typography color="text.secondary">{t.empty}</Typography></Stack>
      ) : (
        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "repeat(2, 1fr)", xl: "repeat(3, 1fr)" }, gap: 1.5 }}>
          {shown.map((l) => {
            const missing = l.checks.filter((c) => c.blocking && !c.met).length;
            const open = l.questions.filter((q) => !q.answeredAt).length;
            return (
              <Card key={l.id}>
                <CardActionArea onClick={() => setOpenId(l.id)} sx={{ height: "100%" }}>
                  <CardContent>
                    <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 0.5 }}>
                      <Typography sx={{ fontFamily: "monospace" }} color="text.secondary" variant="body2">{l.code}</Typography>
                      <Box sx={{ flex: 1 }} />
                      <Chip size="small" color={STATUS_COLOR[l.status]} label={t[`leadStatus_${l.status}`]} />
                    </Stack>
                    <Typography fontWeight={600}>{l.title}</Typography>
                    <Typography variant="body2" color="text.secondary">{l.customerName}</Typography>
                    <Stack direction="row" spacing={1} sx={{ mt: 1, flexWrap: "wrap", rowGap: 0.5 }}>
                      {l.status !== "Converted" && l.status !== "Lost" && (
                        <Chip size="small" variant="outlined" color={l.ready ? "success" : "warning"}
                          label={l.ready ? t.leadReady : t.leadNotReady.replace("{n}", String(missing))} />
                      )}
                      {open > 0 && <Chip size="small" variant="outlined" label={`❓ ${open}`} />}
                      {l.projectCode && <Chip size="small" variant="outlined" label={`📁 ${l.projectCode}`} />}
                    </Stack>
                  </CardContent>
                </CardActionArea>
              </Card>
            );
          })}
        </Box>
      )}

      <Dialog open={!!creating} onClose={() => setCreating(null)} fullWidth maxWidth="md">
        <DialogTitle>{t.newLead}</DialogTitle>
        {creating && (
          <DialogContent>
            {message && <Alert severity="warning" sx={{ mb: 2 }}>{message}</Alert>}
            <LeadFields value={creating} onChange={setCreating} />
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => { setCreating(null); setMessage(null); }}>{t.cancel}</Button>
          <Button variant="contained" disabled={!creating?.title.trim() || !creating?.customerName.trim() || !creating?.need.trim()} onClick={create}>{t.create}</Button>
        </DialogActions>
      </Dialog>

      {selected && <LeadDialog key={selected.id + selected.version} lead={selected} lang={lang} canManage={canManage} onClose={() => setOpenId(null)} onChanged={reload} />}
    </>
  );
}

function LeadFields({ value, onChange, disabled }: { value: Fields; onChange: (f: Fields) => void; disabled?: boolean }) {
  const { t } = useI18n();
  const field = (key: keyof Fields, label: string, extra: object = {}) => (
    <TextField fullWidth size="small" label={label} value={value[key]} disabled={disabled} onChange={(e) => onChange({ ...value, [key]: e.target.value })} {...extra} />
  );
  return (
    <Stack spacing={1.5} sx={{ mt: 1 }}>
      {field("title", t.title, { required: true, slotProps: { htmlInput: { maxLength: 200 } } })}
      <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5}>
        {field("customerName", t.leadCustomer, { required: true })}
        {field("source", t.leadSource)}
      </Stack>
      <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5}>
        {field("contactName", t.leadContactName)}
        {field("contactEmail", t.leadContactEmail, { type: "email" })}
        {field("contactPhone", t.leadContactPhone)}
      </Stack>
      {field("need", t.leadNeed, { required: true, multiline: true, minRows: 3 })}
      {field("businessGoal", t.leadBusinessGoal, { multiline: true })}
      {field("targetUsers", t.leadTargetUsers)}
      <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5}>
        {field("platforms", t.leadPlatforms)}
        {field("constraints", t.leadConstraints)}
      </Stack>
      <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5}>
        {field("budget", t.leadBudget, { type: "number", slotProps: { htmlInput: { min: 0 } } })}
        {field("desiredDueDate", t.leadDueDate, { type: "date", slotProps: { inputLabel: { shrink: true } } })}
      </Stack>
    </Stack>
  );
}

function LeadDialog({ lead, lang, canManage, onClose, onChanged }: { lead: Lead; lang: string; canManage: boolean; onClose: () => void; onChanged: () => Promise<unknown> }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [fields, setFields] = useState<Fields>(fieldsOf(lead));
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [question, setQuestion] = useState("");
  const [convert, setConvert] = useState<{ code: string; name: string } | null>(null);
  const [lost, setLost] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const closed = lead.status === "Converted" || lead.status === "Lost";
  const editable = canManage && !closed;
  const dirty = JSON.stringify(fields) !== JSON.stringify(fieldsOf(lead));

  const act = async (fn: () => Promise<unknown>) => {
    setMessage(null);
    try { await fn(); await onChanged(); } catch (e) { setMessage(errorMessage(e)); }
  };
  const base = `/api/leads/${lead.id}`;

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>
        <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap" }}>
          <Typography sx={{ fontFamily: "monospace" }} color="text.secondary">{lead.code}</Typography>
          <Typography variant="h6" component="span" sx={{ flex: 1 }}>{lead.title}</Typography>
          <Chip size="small" color={STATUS_COLOR[lead.status]} label={t[`leadStatus_${lead.status}`]} />
        </Stack>
      </DialogTitle>
      <DialogContent dividers>
        {message && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}
        {lead.lostReason && <Alert severity="info" sx={{ mb: 2 }}>{t.leadLost}: {lead.lostReason}</Alert>}
        {lead.projectId && (
          <Alert severity="success" sx={{ mb: 2 }} action={<Button component={Link} href={`/projects/${lead.projectId}`} size="small">{t.leadOpenProject}</Button>}>
            📁 {lead.projectCode}
          </Alert>
        )}

        <Typography variant="subtitle2" sx={{ mb: 1 }}>{t.leadMissing}</Typography>
        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "repeat(3, 1fr)" }, gap: 0.5, mb: 2 }}>
          {lead.checks.map((c) => (
            <Stack key={c.code} direction="row" spacing={0.75} sx={{ alignItems: "center" }}>
              {c.met ? <CheckCircleIcon fontSize="small" color="success" /> : c.blocking ? <ErrorIcon fontSize="small" color="warning" /> : <RadioButtonUncheckedIcon fontSize="small" color="disabled" />}
              <Typography variant="body2" color={c.met ? "text.secondary" : "text.primary"}>{t[`lc_${c.code}` as keyof typeof t] as string ?? c.code}</Typography>
            </Stack>
          ))}
        </Box>

        <LeadFields value={fields} onChange={setFields} disabled={!editable} />
        {editable && (
          <Stack direction="row" sx={{ justifyContent: "flex-end", mt: 1 }}>
            <Button size="small" variant="outlined" disabled={!dirty} onClick={() => act(() => api.put(base, { ...bodyOf(fields), version: lead.version }))}>{t.save}</Button>
          </Stack>
        )}

        <Divider sx={{ my: 2 }} />
        <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1, flexWrap: "wrap", rowGap: 1 }}>
          <Typography variant="subtitle2" sx={{ flex: 1 }}>{t.leadQuestions}</Typography>
          {editable && <Button size="small" onClick={() => act(() => api.post(`${base}/questions/suggest`, { language: lang }))}>{t.leadSuggest}</Button>}
        </Stack>
        {lead.questions.length === 0 ? <Typography variant="body2" color="text.secondary">{t.empty}</Typography> : (
          <Stack spacing={1}>
            {lead.questions.map((q) => (
              <Card key={q.id} variant="outlined">
                <CardContent sx={{ py: 1.25, "&:last-child": { pb: 1.25 } }}>
                  <Stack direction="row" spacing={1} sx={{ alignItems: "flex-start" }}>
                    <Typography variant="body2" sx={{ flex: 1, whiteSpace: "pre-wrap" }}>❓ {q.question}</Typography>
                    <Chip size="small" variant="outlined" label={t[`lc_${q.topic}` as keyof typeof t] as string ?? q.topic} />
                  </Stack>
                  {q.answer ? (
                    <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, whiteSpace: "pre-wrap" }}>💬 {q.answer}</Typography>
                  ) : canManage && lead.status !== "Lost" && (
                    <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                      <TextField fullWidth size="small" multiline placeholder={t.leadAnswerPlaceholder} value={answers[q.id] ?? ""}
                        onChange={(e) => setAnswers({ ...answers, [q.id]: e.target.value })} />
                      <Button size="small" disabled={!answers[q.id]?.trim()}
                        onClick={() => act(() => api.post(`${base}/questions/${q.id}/answer`, { answer: answers[q.id], version: q.version }))}>{t.leadAnswer}</Button>
                    </Stack>
                  )}
                </CardContent>
              </Card>
            ))}
          </Stack>
        )}
        {lead.questions.some((q) => q.answer) && editable && <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1 }}>{t.leadNoteAnswers}</Typography>}
        {canManage && lead.status !== "Lost" && (
          <Stack direction="row" spacing={1} sx={{ mt: 1.5 }}>
            <TextField fullWidth size="small" label={t.leadAsk} value={question} onChange={(e) => setQuestion(e.target.value)} slotProps={{ htmlInput: { maxLength: 2000 } }} />
            <Button size="small" disabled={!question.trim()} onClick={() => act(async () => { await api.post(`${base}/questions`, { question }); setQuestion(""); })}>{t.leadAsk}</Button>
          </Stack>
        )}
      </DialogContent>
      <DialogActions sx={{ flexWrap: "wrap", gap: 1 }}>
        {editable && <Button color="error" onClick={() => setLost("")}>{t.leadLost}</Button>}
        <Box sx={{ flex: 1 }} />
        <Button onClick={onClose}>{t.close}</Button>
        {editable && lead.status !== "Qualified" && (
          <Button variant="contained" disabled={!lead.ready || dirty} onClick={() => act(() => api.post(`${base}/qualify`, { version: lead.version }))}>{t.leadQualify}</Button>
        )}
        {editable && lead.status === "Qualified" && (
          <Button variant="contained" disabled={dirty} onClick={() => setConvert({ code: "", name: "" })}>{t.leadConvert}</Button>
        )}
      </DialogActions>

      <Dialog open={!!convert} onClose={() => setConvert(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t.leadConvert}</DialogTitle>
        {convert && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <Alert severity="info">{t.leadConvertInfo}</Alert>
              <TextField label={t.leadProjectCode} value={convert.code} required onChange={(e) => setConvert({ ...convert, code: e.target.value })} slotProps={{ htmlInput: { maxLength: 50 } }} />
              <TextField label={t.leadProjectName} value={convert.name} onChange={(e) => setConvert({ ...convert, name: e.target.value })} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setConvert(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!convert?.code.trim()}
            onClick={() => act(async () => { await api.post(`${base}/convert`, { code: convert!.code, name: convert!.name || null, version: lead.version }); setConvert(null); })}>
            {t.leadConvert}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={lost !== null} onClose={() => setLost(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t.leadLost}</DialogTitle>
        <DialogContent>
          <TextField fullWidth sx={{ mt: 1 }} label={t.leadLostReason} value={lost ?? ""} onChange={(e) => setLost(e.target.value)} multiline minRows={2} required />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setLost(null)}>{t.cancel}</Button>
          <Button variant="contained" color="error" disabled={!lost?.trim()}
            onClick={() => act(async () => { await api.post(`${base}/lost`, { reason: lost, version: lead.version }); setLost(null); })}>{t.leadLost}</Button>
        </DialogActions>
      </Dialog>
    </Dialog>
  );
}
