"use client";

import { useEffect } from "react";
import { Alert, Box, Button } from "@mui/material";
import { useI18n } from "@/lib/i18n";

/** Shown when a page fails while rendering, so the rest of the app stays usable. */
export default function ErrorPage({ error, retry }: { error: Error & { digest?: string }; retry: () => void }) {
  const { t } = useI18n();
  useEffect(() => { console.error(error); }, [error]);
  return (
    <Box sx={{ display: "grid", placeItems: "center", minHeight: "60vh", p: 2 }}>
      <Alert severity="error" action={<Button color="inherit" size="small" onClick={() => retry()}>{t.errorPageRetry}</Button>}>
        {t.errorPageTitle}
      </Alert>
    </Box>
  );
}
