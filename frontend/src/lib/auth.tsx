"use client";

import { createContext, useCallback, useContext, useEffect, useState } from "react";
import { api, ApiError, Me } from "./api";

interface Auth {
  me: Me | null;
  loading: boolean;
  can: (permission: string) => boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<Auth | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);

  const fetchMe = useCallback(
    () =>
      api.get<Me>("/api/auth/me").catch((e) => {
        if (e instanceof ApiError && e.status === 401) return null;
        throw e;
      }),
    [],
  );

  const refresh = useCallback(async () => {
    setMe(await fetchMe());
  }, [fetchMe]);

  useEffect(() => {
    fetchMe()
      .then(setMe)
      .catch(() => setMe(null))
      .finally(() => setLoading(false));
  }, [fetchMe]);

  const login = useCallback(
    async (email: string, password: string) => {
      await api.post("/api/auth/login", { email, password });
      await refresh();
    },
    [refresh],
  );

  const logout = useCallback(async () => {
    await api.post("/api/auth/logout");
    setMe(null);
  }, []);

  // UI convenience only: the API enforces every permission server-side.
  const can = useCallback((p: string) => !!me?.permissions.includes(p), [me]);

  return <AuthContext.Provider value={{ me, loading, can, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
