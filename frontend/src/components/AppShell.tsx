"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import {
  AppBar, Box, Chip, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText,
  Stack, Toolbar, Tooltip, Typography, useMediaQuery, useTheme,
} from "@mui/material";
import { useColorScheme } from "@mui/material/styles";
import MenuIcon from "@mui/icons-material/Menu";
import DashboardIcon from "@mui/icons-material/SpaceDashboardOutlined";
import AccountTreeIcon from "@mui/icons-material/AccountTreeOutlined";
import PeopleIcon from "@mui/icons-material/PeopleAltOutlined";
import ApartmentIcon from "@mui/icons-material/ApartmentOutlined";
import ViewKanbanIcon from "@mui/icons-material/ViewKanbanOutlined";
import TimelineIcon from "@mui/icons-material/TimelineOutlined";
import TaskAltIcon from "@mui/icons-material/TaskAltOutlined";
import ForumIcon from "@mui/icons-material/ForumOutlined";
import MenuBookIcon from "@mui/icons-material/MenuBookOutlined";
import PaymentsIcon from "@mui/icons-material/PaymentsOutlined";
import VerifiedUserIcon from "@mui/icons-material/VerifiedUserOutlined";
import ExtensionIcon from "@mui/icons-material/ExtensionOutlined";
import AdminPanelSettingsIcon from "@mui/icons-material/AdminPanelSettingsOutlined";
import HistoryIcon from "@mui/icons-material/HistoryOutlined";
import LogoutIcon from "@mui/icons-material/Logout";
import DarkModeIcon from "@mui/icons-material/DarkModeOutlined";
import LightModeIcon from "@mui/icons-material/LightModeOutlined";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { stopRealtime } from "@/lib/realtime";
import { Avatar, Mascot } from "@/components/cute";
import { brand } from "@/theme";

const DRAWER_WIDTH = 264;

type NavKey =
  | "navDashboard" | "navOrganization" | "navEmployees" | "navDepartments" | "navProjects" | "navActivity"
  | "navApprovals" | "navChat" | "navKnowledge" | "navFinance" | "navQuality" | "navIntegrations"
  | "navSecurity" | "navAudit";

interface NavItem {
  key: NavKey;
  href?: string;
  icon: React.ReactNode;
  permission?: string;
  /** Phase in which the page arrives; shown disabled until then. */
  phase?: number;
}

const NAV: NavItem[] = [
  { key: "navDashboard", href: "/", icon: <DashboardIcon />, permission: "project.read" },
  { key: "navOrganization", href: "/organization", icon: <AccountTreeIcon />, permission: "org.read" },
  { key: "navEmployees", href: "/employees", icon: <PeopleIcon />, permission: "org.read" },
  { key: "navDepartments", href: "/departments", icon: <ApartmentIcon />, permission: "org.read" },
  { key: "navProjects", href: "/projects", icon: <ViewKanbanIcon />, permission: "project.read" },
  { key: "navActivity", icon: <TimelineIcon />, phase: 2 },
  { key: "navApprovals", icon: <TaskAltIcon />, phase: 3 },
  { key: "navChat", icon: <ForumIcon />, phase: 5 },
  { key: "navKnowledge", icon: <MenuBookIcon />, phase: 5 },
  { key: "navFinance", icon: <PaymentsIcon />, phase: 5 },
  { key: "navQuality", icon: <VerifiedUserIcon />, phase: 4 },
  { key: "navIntegrations", icon: <ExtensionIcon />, phase: 5 },
  { key: "navSecurity", href: "/security", icon: <AdminPanelSettingsIcon />, permission: "security.manage" },
  { key: "navAudit", href: "/audit", icon: <HistoryIcon />, permission: "audit.read" },
];

export default function AppShell({ children }: { children: React.ReactNode }) {
  const { me, can, logout } = useAuth();
  const { t, lang, setLang } = useI18n();
  const { mode, systemMode, setMode } = useColorScheme();
  const pathname = usePathname();
  const router = useRouter();
  const muiTheme = useTheme();
  const desktop = useMediaQuery(muiTheme.breakpoints.up("md"));
  const [open, setOpen] = useState(false);
  const dark = (mode === "system" ? systemMode : mode) === "dark";

  const items = NAV.filter((i) => !i.permission || can(i.permission));

  const drawer = (
    <Box sx={{ display: "flex", flexDirection: "column", height: "100%" }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: "center", px: 2, pt: 2, pb: 1 }}>
        <Mascot size={52} />
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h6" sx={{ lineHeight: 1.15, fontSize: 17 }}>
            {t.appName}
          </Typography>
          <Typography variant="caption" color="text.secondary">Cute AI Office ✦</Typography>
        </Box>
      </Stack>
      <List dense sx={{ flex: 1, overflowY: "auto" }}>
        {items.map((item) => {
          const selected = item.href === "/" ? pathname === "/" : !!item.href && pathname.startsWith(item.href);
          return (
            <ListItemButton
              key={item.key}
              component={item.href ? Link : "div"}
              href={item.href ?? ""}
              selected={selected}
              disabled={!item.href}
              onClick={() => setOpen(false)}
            >
              <ListItemIcon sx={{ minWidth: 36 }}>{item.icon}</ListItemIcon>
              <ListItemText primary={t[item.key]} />
              {item.phase && <Chip size="small" variant="outlined" label={`${t.comingInPhase} ${item.phase}`} sx={{ height: 20, fontSize: 11 }} />}
            </ListItemButton>
          );
        })}
      </List>
      <Divider />
      <Stack direction="row" spacing={1.5} sx={{ p: 2, alignItems: "center" }}>
        <Avatar kind="Human" seed={me?.displayName ?? "me"} size={38} />
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="body2" fontWeight={600} noWrap>
            {me?.displayName}
          </Typography>
          <Typography variant="caption" color="text.secondary" noWrap component="div">
            {me?.roles.join(", ")}
          </Typography>
        </Box>
      </Stack>
    </Box>
  );

  return (
    <Box sx={{ display: "flex", minHeight: "100vh", bgcolor: "background.default" }}>
      <AppBar
        position="fixed"
        color="inherit"
        elevation={0}
        sx={{ borderBottom: 1, borderColor: "divider", bgcolor: (th) => `rgba(${th.vars?.palette.background.defaultChannel} / 0.72)`, backdropFilter: "blur(12px)", width: { md: `calc(100% - ${DRAWER_WIDTH}px)` }, ml: { md: `${DRAWER_WIDTH}px` } }}
      >
        <Toolbar>
          {!desktop && (
            <IconButton edge="start" onClick={() => setOpen(true)} aria-label="menu" sx={{ mr: 1 }}>
              <MenuIcon />
            </IconButton>
          )}
          <Box sx={{ flex: 1 }} />
          <Chip
            label={lang === "th" ? "EN" : "ไทย"}
            onClick={() => setLang(lang === "th" ? "en" : "th")}
            size="small"
            variant="outlined"
            sx={{ mr: 1 }}
          />
          <Tooltip title={dark ? "Light" : "Dark"}>
            <IconButton onClick={() => setMode(dark ? "light" : "dark")}>{dark ? <LightModeIcon /> : <DarkModeIcon />}</IconButton>
          </Tooltip>
          <Tooltip title={t.logout}>
            <IconButton
              onClick={async () => {
                await stopRealtime();
                await logout();
                router.replace("/login");
              }}
            >
              <LogoutIcon />
            </IconButton>
          </Tooltip>
        </Toolbar>
      </AppBar>

      <Box component="nav" sx={{ width: { md: DRAWER_WIDTH }, flexShrink: { md: 0 } }}>
        <Drawer
          variant={desktop ? "permanent" : "temporary"}
          open={desktop || open}
          onClose={() => setOpen(false)}
          ModalProps={{ keepMounted: true }}
          sx={(th) => ({ "& .MuiDrawer-paper": { width: DRAWER_WIDTH, boxSizing: "border-box", borderRight: `1.5px solid ${th.vars?.palette.divider}`, backgroundImage: `linear-gradient(180deg, ${brand.lilac} 0%, transparent 55%)`, ...th.applyStyles("dark", { backgroundImage: "linear-gradient(180deg, #2c2150 0%, transparent 55%)" }) } })}
        >
          {drawer}
        </Drawer>
      </Box>

      <Box component="main" sx={{ flex: 1, minWidth: 0, p: { xs: 2, md: 3 }, mt: 8, backgroundImage: `radial-gradient(600px 300px at 90% 0%, ${brand.pinkSoft}55, transparent 70%), radial-gradient(500px 280px at 10% 100%, ${brand.lavender}44, transparent 70%)`, backgroundAttachment: "fixed" }}>
        {children}
      </Box>
    </Box>
  );
}
