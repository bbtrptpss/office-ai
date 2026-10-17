"use client";

import { Box, Card, Stack, Typography } from "@mui/material";
import { api, OrgChartNode } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { KindIcon, Loading, PageHeader, StatusChip, useLoad } from "@/components/common";
import { Rise } from "@/components/cute";

function Node({ node, depth }: { node: OrgChartNode; depth: number }) {
  return (
    <Box sx={{ pl: depth === 0 ? 0 : 3, borderLeft: depth === 0 ? 0 : 1, borderColor: "divider", ml: depth === 0 ? 0 : 1.5 }}>
      <Rise index={depth} style={{ height: "auto" }}>
      <Card sx={{ my: 1, px: 2, py: 1, display: "inline-flex", minWidth: { xs: "100%", sm: 360 }, maxWidth: "100%", "&:hover": { transform: "translateX(4px)" } }}>
        <Stack direction="row" spacing={1.5} sx={{ alignItems: "center", width: "100%" }}>
          <KindIcon kind={node.kind} seed={node.displayName} size={40} />
          <Box sx={{ flex: 1, minWidth: 0 }}>
            <Typography fontWeight={600} noWrap>{node.jobTitle}</Typography>
            <Typography variant="body2" color="text.secondary" noWrap>
              {node.displayName} · {node.departmentName}
            </Typography>
          </Box>
          <StatusChip value={node.status} />
        </Stack>
      </Card>
      </Rise>
      {node.reports.map((r) => (
        <Node key={r.id} node={r} depth={depth + 1} />
      ))}
    </Box>
  );
}

export default function OrganizationPage() {
  const { t } = useI18n();
  const { data, error, reload } = useLoad(() => api.get<OrgChartNode[]>("/api/org-chart"));
  useRealtime("organizationChanged", () => void reload());

  return (
    <>
      <PageHeader title={t.navOrganization} />
      {!data ? <Loading error={error} /> : data.map((n) => <Node key={n.id} node={n} depth={0} />)}
    </>
  );
}
