"use client";

import { useState } from "react";
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack,
  TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import CheckCircleIcon from "@mui/icons-material/CheckCircle";
import CancelIcon from "@mui/icons-material/Cancel";
import WarningIcon from "@mui/icons-material/WarningAmber";
import DownloadIcon from "@mui/icons-material/DownloadOutlined";
import { api, DeliveryPackage, DeliveryPackageStatus, downloadFile, ProjectDocument, Release } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { Loading, StatusChip, useErrorMessage, useLoad } from "./common";

const STATUS_COLOR: Record<DeliveryPackageStatus, "default" | "info" | "success" | "warning"> = {
  Draft: "default", Delivered: "info", Accepted: "success", ChangesRequested: "warning",
};

type Form = { id?: string; version?: number; releaseId: string; title: string; documentIds: string[]; handoverNotes: string };
type Step = { pkg: DeliveryPackage; action: "delivered" | "accepted" | "changes"; text: string };

/** Downloads the package's Markdown export with the session cookie. */
const download = (pkg: DeliveryPackage) => downloadFile(`/api/delivery-packages/${pkg.id}/export`, `${pkg.code}.md`);

/** Customer delivery (Master Prompt §7 stage 8): package the deployed release, documents and test evidence; record delivery and the customer's answer. */
export function DeliveryPackagesPanel({ projectId, onOpenTask }: { projectId: string; onOpenTask: (taskId: string) => void }) {
  const { t, label } = useI18n();
  const text = (key: string) => (t as unknown as Record<string, string | undefined>)[key];
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(async () => {
    const [packages, releases, documents] = await Promise.all([
      api.get<DeliveryPackage[]>(`/api/projects/${projectId}/delivery-packages`),
      api.get<Release[]>(`/api/projects/${projectId}/releases`),
      api.get<ProjectDocument[]>(`/api/projects/${projectId}/documents`),
    ]);
    return { packages, releases, documents };
  }, projectId);
  const [form, setForm] = useState<Form | null>(null);
  const [step, setStep] = useState<Step | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const manage = can("project.manage");

  if (!data) return <Loading error={error} />;
  const act = async (fn: () => Promise<unknown>) => {
    setMessage(null);
    try { await fn(); } catch (e) { setMessage(errorMessage(e)); }
    await reload();
  };
  const releases = data.releases.filter((r) => r.status !== "Cancelled");
  const deployed = releases.find((r) => r.status === "Deployed");
  const toggleDoc = (id: string) => form && setForm({ ...form, documentIds: form.documentIds.includes(id) ? form.documentIds.filter((x) => x !== id) : [...form.documentIds, id] });
  const textRequired = step?.action === "changes";

  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>{t.packagesNote}</Typography>
      {message && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}
      {manage && (
        <Button startIcon={<AddIcon />} sx={{ mb: 2 }} disabled={releases.length === 0}
          onClick={() => setForm({
            releaseId: (deployed ?? releases[0]).id, title: "", handoverNotes: "",
            // Latest revision of each final document, as a starting point.
            documentIds: data.documents.filter((d, i, all) => d.status === "Final" && all.findIndex((x) => x.title === d.title && x.kind === d.kind) === i).map((d) => d.id),
          })}>
          {t.newPackage}
        </Button>
      )}
      {releases.length === 0 && <Alert severity="info" sx={{ mb: 2 }}>{t.packageNeedsRelease}</Alert>}

      {data.packages.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
        <Stack spacing={1.5}>
          {data.packages.map((p) => (
            <Card key={p.id} variant="outlined">
              <CardContent>
                <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 0.5, mb: 0.5 }}>
                  <Typography sx={{ fontFamily: "monospace" }} color="text.secondary">{p.code}</Typography>
                  <Typography fontWeight={600} sx={{ flex: 1, minWidth: 160 }}>{p.title}</Typography>
                  <Chip size="small" color={STATUS_COLOR[p.status]} label={t[`packageStatus_${p.status}`]} />
                </Stack>
                <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 1 }}>
                  Release <code>{p.releaseTag}</code> · {t[`releaseStatus_${p.releaseStatus}`]}
                  {p.commitSha && <> · commit <code>{p.commitSha.slice(0, 8)}</code></>}
                  {p.testRunStatus && <> · {t.packageTests} <StatusChip value={p.testRunStatus} /></>}
                  {p.deliveredAt && <> · {t.packageDeliveredAt} {new Date(p.deliveredAt).toLocaleString()}</>}
                  {p.respondedAt && <> · {t.packageRespondedAt} {new Date(p.respondedAt).toLocaleString()}</>}
                </Typography>

                <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" }, gap: 2 }}>
                  <Box>
                    <Typography variant="caption" color="text.secondary" fontWeight={600}>{t.packageDocuments} ({p.documents.length})</Typography>
                    {p.documents.length === 0 ? <Typography variant="body2" color="text.secondary">—</Typography>
                      : p.documents.map((d) => <Typography key={d.id} variant="body2">• {d.title} <Box component="span" sx={{ color: "text.secondary" }}>v{d.revision} · {label(d.status)}</Box></Typography>)}
                    {p.handoverNotes && <>
                      <Typography variant="caption" color="text.secondary" fontWeight={600} component="div" sx={{ mt: 1 }}>{t.packageHandover}</Typography>
                      <Typography variant="body2" sx={{ whiteSpace: "pre-wrap" }}>{p.handoverNotes}</Typography>
                    </>}
                  </Box>
                  <Stack spacing={0.5}>
                    {p.checks.map((c) => (
                      <Stack key={c.code} direction="row" spacing={0.75} sx={{ alignItems: "center" }}>
                        {c.met ? <CheckCircleIcon sx={{ fontSize: 18, color: "success.main" }} />
                          : c.blocking ? <CancelIcon sx={{ fontSize: 18, color: "error.main" }} /> : <WarningIcon sx={{ fontSize: 18, color: "warning.main" }} />}
                        <Typography variant="body2">
                          {text(`pc_${c.code}`) ?? c.code}
                          {c.detail && <Box component="span" sx={{ color: "text.secondary" }}> · {c.detail}</Box>}
                        </Typography>
                      </Stack>
                    ))}
                  </Stack>
                </Box>

                {p.deliveryNote && <Typography variant="body2" sx={{ mt: 1 }}>📦 {p.deliveryNote}</Typography>}
                {p.customerFeedback && <Typography variant="body2" sx={{ mt: 0.5 }}>💬 {p.customerFeedback}</Typography>}
                {p.changeTaskId && (
                  <Typography variant="caption" component="div" sx={{ mt: 0.5 }}>
                    {t.packageChangeTask}: <Box component="span" sx={{ cursor: "pointer", textDecoration: "underline" }} onClick={() => onOpenTask(p.changeTaskId!)}>{t.packageOpenTask}</Box>
                  </Typography>
                )}

                <Stack direction="row" spacing={1} sx={{ mt: 1.5, justifyContent: "flex-end", flexWrap: "wrap", rowGap: 1 }}>
                  <Button size="small" startIcon={<DownloadIcon />} onClick={() => download(p).catch((e) => setMessage(errorMessage(e)))}>{t.packageExport}</Button>
                  {manage && p.status === "Draft" && <>
                    <Button size="small" onClick={() => setForm({ id: p.id, version: p.version, releaseId: p.releaseId, title: p.title, documentIds: p.documents.map((d) => d.id), handoverNotes: p.handoverNotes ?? "" })}>{t.edit}</Button>
                    <Button size="small" variant="contained" disabled={!p.ready} onClick={() => setStep({ pkg: p, action: "delivered", text: "" })}>{t.packageRecordDelivered}</Button>
                  </>}
                  {manage && p.status === "Delivered" && <>
                    <Button size="small" color="warning" onClick={() => setStep({ pkg: p, action: "changes", text: "" })}>{t.packageChanges}</Button>
                    <Button size="small" variant="contained" color="success" onClick={() => setStep({ pkg: p, action: "accepted", text: "" })}>{t.packageAccepted}</Button>
                  </>}
                </Stack>
              </CardContent>
            </Card>
          ))}
        </Stack>
      )}

      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{form?.id ? t.edit : t.newPackage}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField select label="Release" value={form.releaseId} onChange={(e) => setForm({ ...form, releaseId: e.target.value })}>
                {releases.map((r) => <MenuItem key={r.id} value={r.id}>{r.tag} · {t[`releaseStatus_${r.status}`]}</MenuItem>)}
              </TextField>
              <TextField label={t.title} value={form.title} placeholder={t.packageTitleHint} onChange={(e) => setForm({ ...form, title: e.target.value })} slotProps={{ htmlInput: { maxLength: 200 } }} />
              <Box>
                <Typography variant="caption" color="text.secondary" fontWeight={600}>{t.packageDocuments}</Typography>
                {data.documents.length === 0 ? <Typography variant="body2" color="text.secondary">{t.empty}</Typography> : (
                  <Stack sx={{ maxHeight: 240, overflow: "auto" }}>
                    {data.documents.map((d) => (
                      <FormControlLabel key={d.id} control={<Checkbox size="small" checked={form.documentIds.includes(d.id)} onChange={() => toggleDoc(d.id)} />}
                        label={<Typography variant="body2">{d.title} <Box component="span" sx={{ color: "text.secondary" }}>v{d.version} · {label(d.status)}</Box></Typography>} />
                    ))}
                  </Stack>
                )}
              </Box>
              <TextField label={t.packageHandover} value={form.handoverNotes} onChange={(e) => setForm({ ...form, handoverNotes: e.target.value })} multiline minRows={4} helperText={t.packageHandoverHelp} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" onClick={() => act(async () => {
            const body = { releaseId: form!.releaseId, title: form!.title || null, documentIds: form!.documentIds, handoverNotes: form!.handoverNotes, version: form!.version };
            if (form!.id) await api.put(`/api/delivery-packages/${form!.id}`, body);
            else await api.post(`/api/projects/${projectId}/delivery-packages`, body);
            setForm(null);
          })}>{form?.id ? t.save : t.create}</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={!!step} onClose={() => setStep(null)} fullWidth maxWidth="xs">
        {step && <>
          <DialogTitle>{step.pkg.code} · {step.action === "delivered" ? t.packageRecordDelivered : step.action === "accepted" ? t.packageAccepted : t.packageChanges}</DialogTitle>
          <DialogContent>
            {step.action === "delivered" && <Alert severity="info" sx={{ mt: 1 }}>{t.packageDeliveredInfo}</Alert>}
            {step.action === "changes" && <Alert severity="info" sx={{ mt: 1 }}>{t.packageChangesInfo}</Alert>}
            <TextField fullWidth sx={{ mt: 2 }} multiline minRows={2} required={textRequired}
              label={step.action === "delivered" ? t.note : textRequired ? t.packageFeedbackRequired : t.packageFeedback}
              value={step.text} onChange={(e) => setStep({ ...step, text: e.target.value })} />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setStep(null)}>{t.cancel}</Button>
            <Button variant="contained" disabled={textRequired && !step.text.trim()} onClick={() => act(async () => {
              const base = `/api/delivery-packages/${step.pkg.id}`;
              if (step.action === "delivered") await api.post(`${base}/delivered`, { note: step.text || null, version: step.pkg.version });
              else await api.post(`${base}/response`, { accepted: step.action === "accepted", feedback: step.text || null, version: step.pkg.version });
              setStep(null);
            })}>{t.packageConfirm}</Button>
          </DialogActions>
        </>}
      </Dialog>
    </>
  );
}
