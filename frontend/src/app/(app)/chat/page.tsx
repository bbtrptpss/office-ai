"use client";

import { Suspense, useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import {
  Alert, Badge, Box, Button, Card, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Divider, IconButton, List, ListItemButton,
  ListItemText, ListSubheader, Stack, TextField, Tooltip, Typography, useMediaQuery, useTheme,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import DeleteIcon from "@mui/icons-material/DeleteOutline";
import SendIcon from "@mui/icons-material/SendRounded";
import InventoryIcon from "@mui/icons-material/Inventory2Outlined";
import { api, ChannelKind, ChatChannel, ChatMessage } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { emitLocal, useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, useErrorMessage, useLoad } from "@/components/common";
import { Avatar, Mascot } from "@/components/cute";

const PAGE = 50;
const KIND_ORDER: ChannelKind[] = ["Company", "Department", "Project"];

export default function ChatPageWrapper() {
  return (
    <Suspense fallback={<Loading />}>
      <ChatPage />
    </Suspense>
  );
}

function ChatPage() {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const router = useRouter();
  const params = useSearchParams();
  const theme = useTheme();
  const desktop = useMediaQuery(theme.breakpoints.up("md"));
  const canManage = can("comms.manage");

  const { data: channels, error, reload: reloadChannels } = useLoad(() => api.get<ChatChannel[]>("/api/chat/channels"));
  const [selected, setSelected] = useState<string | null>(params.get("channel"));
  const [newChannel, setNewChannel] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  // ?project=<id> opens (and on first use creates) that project's channel.
  const projectParam = params.get("project");
  useEffect(() => {
    if (!projectParam) return;
    api.post<ChatChannel>("/api/chat/channels/open", { projectId: projectParam })
      .then((c) => { setSelected(c.id); void reloadChannels(); router.replace(`/chat?channel=${c.id}`); })
      .catch((e) => setMessage(errorMessage(e)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectParam]);

  // Desktop: fall back to the first channel (General) so the page is never empty.
  const current = channels?.find((c) => c.id === selected) ?? (desktop && !projectParam ? channels?.[0] : undefined);

  useRealtime("chatMessage", () => void reloadChannels());
  useRealtime("chatRead", () => void reloadChannels());

  const pick = (id: string) => {
    setSelected(id);
    router.replace(`/chat?channel=${id}`);
  };

  const list = channels && (
    <Card sx={{ height: "100%", overflow: "auto" }}>
      <List dense disablePadding>
        {KIND_ORDER.filter((k) => channels.some((c) => c.kind === k)).map((kind) => (
          <Box key={kind}>
            <ListSubheader sx={{ bgcolor: "background.paper", fontWeight: 700 }}>{label(kind)}</ListSubheader>
            {channels.filter((c) => c.kind === kind).map((c) => (
              <ListItemButton key={c.id} selected={current?.id === c.id} onClick={() => pick(c.id)}>
                <ListItemText primary={`# ${c.name}`} slotProps={{ primary: { noWrap: true, fontWeight: c.unread > 0 ? 700 : 400 } }} />
                {c.unread > 0 && <Chip size="small" color="secondary" label={c.unread > 99 ? "99+" : c.unread} sx={{ height: 20, fontWeight: 700 }} />}
              </ListItemButton>
            ))}
          </Box>
        ))}
      </List>
    </Card>
  );

  return (
    <>
      <PageHeader title={t.navChat}
        actions={canManage && <Button variant="outlined" startIcon={<AddIcon />} onClick={() => setNewChannel("")}>{t.newChannel}</Button>} />
      {message && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}
      {!channels ? <Loading error={error} /> : (
        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "260px 1fr" }, gap: 2, height: { md: "calc(100vh - 190px)" }, minHeight: 420 }}>
          {(desktop || !current) && list}
          {current ? (
            <Conversation key={current.id} channel={current} canManage={canManage} onBack={desktop ? undefined : () => { setSelected(null); router.replace("/chat"); }}
              onChanged={() => void reloadChannels()} />
          ) : desktop && (
            <Card sx={{ display: "grid", placeItems: "center" }}>
              <Stack sx={{ alignItems: "center" }}><Mascot size={96} /><Typography color="text.secondary">{t.noChannel}</Typography></Stack>
            </Card>
          )}
        </Box>
      )}
      <Dialog open={newChannel !== null} onClose={() => setNewChannel(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t.newChannel}</DialogTitle>
        <DialogContent>
          <TextField autoFocus fullWidth label={t.channelName} sx={{ mt: 1 }} value={newChannel ?? ""} onChange={(e) => setNewChannel(e.target.value)} slotProps={{ htmlInput: { maxLength: 100 } }} />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setNewChannel(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!newChannel?.trim()} onClick={async () => {
            try {
              const c = await api.post<ChatChannel>("/api/chat/channels", { name: newChannel!.trim() });
              setNewChannel(null);
              await reloadChannels();
              pick(c.id);
            } catch (e) { setMessage(errorMessage(e)); }
          }}>{t.create}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

function mergeById(a: ChatMessage[], b: ChatMessage[]) {
  const map = new Map(a.map((m) => [m.id, m]));
  for (const m of b) map.set(m.id, m);
  return [...map.values()].sort((x, y) => x.createdAt.localeCompare(y.createdAt) || x.id.localeCompare(y.id));
}

function Conversation({ channel, canManage, onBack, onChanged }: { channel: ChatChannel; canManage: boolean; onBack?: () => void; onChanged: () => void }) {
  const { t, lang } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const reduce = useReducedMotion();
  const [messages, setMessages] = useState<ChatMessage[] | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const scroller = useRef<HTMLDivElement>(null);
  const stickToBottom = useRef(true);
  const base = `/api/chat/channels/${channel.id}`;
  const errorRef = useRef(errorMessage);
  useEffect(() => { errorRef.current = errorMessage; });

  const markRead = useCallback(async () => {
    if (document.visibilityState !== "visible") return;
    try {
      await api.post(`${base}/read`);
      emitLocal("chatRead");
    } catch { /* best effort */ }
  }, [base]);

  const loadLatest = useCallback(async () => {
    try {
      const page = await api.get<ChatMessage[]>(`${base}/messages?limit=${PAGE}`);
      setMessages((old) => (old ? mergeById(old, page) : page));
      setHasMore((h) => h || page.length === PAGE);
      void markRead();
    } catch (e) { setProblem(errorRef.current(e)); }
  }, [base, markRead]);

  useEffect(() => {
    let cancelled = false;
    api.get<ChatMessage[]>(`${base}/messages?limit=${PAGE}`)
      .then((page) => {
        if (cancelled) return;
        setMessages(page);
        setHasMore(page.length === PAGE);
        void markRead();
      })
      .catch((e) => !cancelled && setProblem(errorRef.current(e)));
    return () => { cancelled = true; };
  }, [base, markRead]);
  const live = useRealtime<{ channelId: string }>("chatMessage", (p) => { if (p.channelId === channel.id) void loadLatest(); });

  useEffect(() => {
    const onVisible = () => void markRead();
    document.addEventListener("visibilitychange", onVisible);
    return () => document.removeEventListener("visibilitychange", onVisible);
  }, [markRead]);

  // Keep the view pinned to the newest message unless the reader scrolled up.
  useLayoutEffect(() => {
    const el = scroller.current;
    if (el && stickToBottom.current) el.scrollTop = el.scrollHeight;
  }, [messages]);

  async function loadOlder() {
    if (!messages?.length) return;
    const el = scroller.current;
    const before = el?.scrollHeight ?? 0;
    const page = await api.get<ChatMessage[]>(`${base}/messages?limit=${PAGE}&before=${encodeURIComponent(messages[0].createdAt)}`);
    setHasMore(page.length === PAGE);
    stickToBottom.current = false;
    setMessages((old) => mergeById(old ?? [], page));
    requestAnimationFrame(() => { if (el) el.scrollTop = el.scrollHeight - before; });
  }

  async function send() {
    const body = draft.trim();
    if (!body || sending) return;
    setSending(true);
    setProblem(null);
    try {
      const m = await api.post<ChatMessage>(`${base}/messages`, { body });
      stickToBottom.current = true;
      setMessages((old) => mergeById(old ?? [], [m]));
      setDraft("");
      onChanged();
    } catch (e) { setProblem(errorMessage(e)); } finally { setSending(false); }
  }

  const fmt = new Intl.DateTimeFormat(lang === "th" ? "th-TH" : "en-GB", { dateStyle: "medium", timeStyle: "short" });
  const canPost = can("comms.post") && !channel.isArchived;

  return (
    <Card sx={{ display: "flex", flexDirection: "column", height: { xs: "calc(100vh - 170px)", md: "100%" }, minHeight: 420 }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: "center", px: 2, py: 1.25 }}>
        {onBack && <IconButton onClick={onBack} aria-label="back" edge="start"><ArrowBackIcon /></IconButton>}
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography fontWeight={700} noWrap># {channel.name}</Typography>
          {channel.description && <Typography variant="caption" color="text.secondary" noWrap component="div">{channel.description}</Typography>}
        </Box>
        {canManage && channel.kind !== "Project" && channel.kind !== "Department" && (
          <Tooltip title={channel.isArchived ? t.unarchive : t.archive}>
            <IconButton onClick={async () => {
              try { await api.post(`${base}/${channel.isArchived ? "unarchive" : "archive"}`); onChanged(); } catch (e) { setProblem(errorMessage(e)); }
            }}><InventoryIcon /></IconButton>
          </Tooltip>
        )}
      </Stack>
      <Divider />
      {!live && <Alert severity="info" sx={{ borderRadius: 0, py: 0 }}>{t.liveOff}</Alert>}
      <Box ref={scroller} onScroll={(e) => {
        const el = e.currentTarget;
        stickToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
      }} sx={{ flex: 1, overflowY: "auto", px: 2, py: 1.5 }} aria-live="polite">
        {!messages ? <Loading error={problem} /> : (
          <>
            {hasMore && <Box sx={{ textAlign: "center", mb: 1 }}><Button size="small" onClick={() => void loadOlder()}>{t.olderMessages}</Button></Box>}
            {messages.length === 0 && (
              <Stack sx={{ alignItems: "center", py: 6 }}><Mascot size={88} /><Typography color="text.secondary">{t.empty}</Typography></Stack>
            )}
            <AnimatePresence initial={false}>
              {messages.map((m, i) => {
                const prev = messages[i - 1];
                const grouped = prev && prev.authorName === m.authorName && !prev.isDeleted
                  && new Date(m.createdAt).getTime() - new Date(prev.createdAt).getTime() < 5 * 60_000;
                return (
                  <motion.div key={m.id} initial={reduce ? false : { opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }}>
                    <Stack direction="row" spacing={1.5} sx={{ mt: grouped ? 0.25 : 1.5, "&:hover .msg-actions": { opacity: 1 } }}>
                      <Box sx={{ width: 36, flexShrink: 0 }}>
                        {!grouped && <Avatar kind={m.isAgent ? "AI" : "Human"} seed={m.authorName} size={36} />}
                      </Box>
                      <Box sx={{ flex: 1, minWidth: 0 }}>
                        {!grouped && (
                          <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap" }}>
                            <Typography variant="body2" fontWeight={700}>{m.authorName}</Typography>
                            {m.isAgent && <Chip size="small" color="primary" label={t.aiBadge} sx={{ height: 18, fontSize: 11 }} />}
                            {m.isSimulated && <Chip size="small" color="warning" variant="outlined" label={t.simulatedBadge} sx={{ height: 18, fontSize: 11 }} />}
                            <Typography variant="caption" color="text.secondary">{fmt.format(new Date(m.createdAt))}</Typography>
                          </Stack>
                        )}
                        {m.isDeleted ? (
                          <Typography variant="body2" color="text.disabled" fontStyle="italic">{t.messageDeleted}</Typography>
                        ) : (
                          <Typography variant="body2" sx={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{m.body}</Typography>
                        )}
                      </Box>
                      {m.canDelete && (
                        <Tooltip title={t.deleteMessage}>
                          <IconButton className="msg-actions" size="small" sx={{ opacity: { xs: 1, md: 0 }, alignSelf: "flex-start" }} onClick={async () => {
                            try {
                              await api.del(`/api/chat/messages/${m.id}`);
                              setMessages((old) => (old ?? []).map((x) => (x.id === m.id ? { ...x, isDeleted: true, body: null, canDelete: false } : x)));
                            } catch (e) { setProblem(errorMessage(e)); }
                          }}><DeleteIcon fontSize="small" /></IconButton>
                        </Tooltip>
                      )}
                    </Stack>
                  </motion.div>
                );
              })}
            </AnimatePresence>
          </>
        )}
      </Box>
      <Divider />
      {problem && messages && <Alert severity="warning" sx={{ borderRadius: 0 }} onClose={() => setProblem(null)}>{problem}</Alert>}
      {channel.isArchived ? (
        <Typography variant="body2" color="text.secondary" sx={{ p: 2 }}>{t.archivedChannel}</Typography>
      ) : canPost && (
        <Stack direction="row" spacing={1} sx={{ p: 1.5, alignItems: "flex-end" }}>
          <TextField fullWidth multiline maxRows={6} size="small" placeholder={t.messagePlaceholder} value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
                e.preventDefault();
                void send();
              }
            }}
            slotProps={{ htmlInput: { maxLength: 4000, "aria-label": t.messagePlaceholder } }} />
          <Badge invisible={draft.length < 3800} badgeContent={4000 - draft.length} color="warning" max={9999}>
            <IconButton color="primary" disabled={!draft.trim() || sending} onClick={() => void send()} aria-label={t.send}><SendIcon /></IconButton>
          </Badge>
        </Stack>
      )}
    </Card>
  );
}
