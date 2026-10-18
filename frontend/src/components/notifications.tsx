"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Badge, Box, Button, Divider, IconButton, List, ListItemButton, ListItemText, Popover, Stack, Tooltip, Typography } from "@mui/material";
import NotificationsIcon from "@mui/icons-material/NotificationsNoneOutlined";
import { api, AppNotification, NotificationList } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { useLoad } from "./common";

const KIND_EMOJI: Record<AppNotification["kind"], string> = { mention: "💬", meeting_invite: "📅", action_item: "✅" };

/** Bell in the top bar: my mentions, meeting invites and action items. */
export function NotificationBell() {
  const { t, lang } = useI18n();
  const router = useRouter();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const { data, reload } = useLoad(() => api.get<NotificationList>("/api/notifications"));
  useRealtime("notificationsChanged", () => void reload());
  const fmt = new Intl.DateTimeFormat(lang === "th" ? "th-TH" : "en-GB", { dateStyle: "short", timeStyle: "short" });
  const kindLabel: Record<AppNotification["kind"], string> = { mention: t.notifMention, meeting_invite: t.notifMeetingInvite, action_item: t.notifActionItem };

  async function open(n: AppNotification) {
    setAnchor(null);
    if (!n.isRead) {
      try { await api.post(`/api/notifications/read?id=${n.id}`); } catch { /* best effort */ }
      void reload();
    }
    if (n.link) router.push(n.link);
  }

  return (
    <>
      <Tooltip title={t.notifications}>
        <IconButton onClick={(e) => setAnchor(e.currentTarget)} aria-label={t.notifications}>
          <Badge badgeContent={data?.unread ?? 0} color="secondary" max={99}><NotificationsIcon /></Badge>
        </IconButton>
      </Tooltip>
      <Popover open={!!anchor} anchorEl={anchor} onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: "bottom", horizontal: "right" }} transformOrigin={{ vertical: "top", horizontal: "right" }}
        slotProps={{ paper: { sx: { width: { xs: "calc(100vw - 32px)", sm: 380 }, maxHeight: 480 } } }}>
        <Stack direction="row" sx={{ alignItems: "center", px: 2, py: 1 }}>
          <Typography fontWeight={700} sx={{ flex: 1 }}>{t.notifications}</Typography>
          <Button size="small" disabled={!data?.unread} onClick={async () => { await api.post("/api/notifications/read"); void reload(); }}>{t.markAllRead}</Button>
        </Stack>
        <Divider />
        {!data?.items.length ? (
          <Typography color="text.secondary" sx={{ p: 2 }}>{t.empty}</Typography>
        ) : (
          <List dense disablePadding>
            {data.items.map((n) => (
              <ListItemButton key={n.id} onClick={() => void open(n)} sx={{ alignItems: "flex-start", bgcolor: n.isRead ? undefined : "action.hover" }}>
                <Box sx={{ fontSize: 20, mr: 1.5, mt: 0.25 }} aria-hidden>{KIND_EMOJI[n.kind] ?? "🔔"}</Box>
                <ListItemText
                  primary={<><Typography variant="caption" color="text.secondary">{kindLabel[n.kind] ?? n.kind} · {fmt.format(new Date(n.createdAt))}</Typography>
                    <Typography variant="body2" fontWeight={n.isRead ? 400 : 700} sx={{ overflowWrap: "anywhere" }}>{n.title}</Typography></>}
                  secondary={n.body}
                  slotProps={{ secondary: { sx: { display: "-webkit-box", WebkitLineClamp: 2, WebkitBoxOrient: "vertical", overflow: "hidden" } } }} />
              </ListItemButton>
            ))}
          </List>
        )}
      </Popover>
    </>
  );
}
