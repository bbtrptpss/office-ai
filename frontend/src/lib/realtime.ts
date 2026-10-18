"use client";

import { useEffect, useRef, useState } from "react";
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { API_URL } from "./api";

let connection: HubConnection | null = null;
let starting: Promise<void> | null = null;

function getConnection(): HubConnection {
  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/events`, { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
  }
  return connection;
}

async function ensureStarted() {
  const c = getConnection();
  if (c.state === HubConnectionState.Disconnected) {
    starting ??= c.start().finally(() => (starting = null));
  }
  await starting;
}

const LOCAL_PREFIX = "officeai:";

/** Raise an event inside this tab only (e.g. "I just read this channel"), delivered to the same useRealtime subscribers. */
export function emitLocal<T>(event: string, payload?: T) {
  window.dispatchEvent(new CustomEvent(LOCAL_PREFIX + event, { detail: payload }));
}

export async function stopRealtime() {
  if (connection) await connection.stop();
}

/** Subscribe to a server event while the component is mounted. Returns whether the live connection is up. */
export function useRealtime<T = unknown>(event: string, handler: (payload: T) => void): boolean {
  const handlerRef = useRef(handler);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    handlerRef.current = handler;
  });

  useEffect(() => {
    const c = getConnection();
    const listener = (payload: T) => handlerRef.current(payload);
    c.on(event, listener);
    const update = () => setConnected(c.state === HubConnectionState.Connected);
    c.onreconnected(update);
    c.onreconnecting(update);
    c.onclose(update);
    ensureStarted().then(update).catch(update);
    const local = (e: Event) => handlerRef.current((e as CustomEvent<T>).detail);
    window.addEventListener(LOCAL_PREFIX + event, local);
    return () => {
      c.off(event, listener);
      window.removeEventListener(LOCAL_PREFIX + event, local);
    };
  }, [event]);

  return connected;
}
