"use client";

import Link from "next/link";
import { Box, Button, Typography } from "@mui/material";
import { useI18n } from "@/lib/i18n";

export default function NotFound() {
  const { t } = useI18n();
  return (
    <Box sx={{ display: "grid", placeItems: "center", minHeight: "60vh", p: 2, textAlign: "center" }}>
      <Box>
        <Typography variant="h5" gutterBottom>{t.notFoundTitle}</Typography>
        <Button component={Link} href="/" variant="contained">{t.backToOverview}</Button>
      </Box>
    </Box>
  );
}
