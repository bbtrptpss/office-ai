import type { Metadata } from "next";
import { IBM_Plex_Sans_Thai, Mali } from "next/font/google";
import { AppRouterCacheProvider } from "@mui/material-nextjs/v16-appRouter";
import InitColorSchemeScript from "@mui/material/InitColorSchemeScript";
import Providers from "@/components/Providers";
import "./globals.css";

const thaiFont = IBM_Plex_Sans_Thai({
  variable: "--font-thai",
  subsets: ["thai", "latin"],
  weight: ["400", "500", "600"],
});

const cuteFont = Mali({
  variable: "--font-cute",
  subsets: ["thai", "latin"],
  weight: ["500", "600", "700"],
});

export const metadata: Metadata = {
  title: "AI Software House",
  description: "ระบบปฏิบัติการบริษัทซอฟต์แวร์ที่มีพนักงาน AI",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="th" suppressHydrationWarning>
      <body className={`${thaiFont.variable} ${cuteFont.variable}`}>
        <InitColorSchemeScript attribute="class" />
        <AppRouterCacheProvider>
          <Providers>{children}</Providers>
        </AppRouterCacheProvider>
      </body>
    </html>
  );
}
