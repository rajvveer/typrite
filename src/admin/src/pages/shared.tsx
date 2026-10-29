import { useCallback, useEffect, useState, type ReactNode } from "react";
import { describeError } from "../api";

export function formatUtc(value: string | null | undefined): string {
  if (value === null || value === undefined || value.length === 0) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toISOString().replace("T", " ").replace(/\.\d+Z$/, "Z");
}

export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined) return "—";
  return value.toLocaleString("en-US");
}

export function ErrorText({ error, id }: { error: string | null; id?: string }): ReactNode {
  if (error === null) return null;
  return (
    <p className="error" role="alert" id={id}>
      {error}
    </p>
  );
}

export function Notice({ text }: { text: string | null }): ReactNode {
  if (text === null) return null;
  return (
    <p className="notice" role="status">
      {text}
    </p>
  );
}

export interface AsyncState<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
  reload: () => void;
}

export function useAsync<T>(load: () => Promise<T>, deps: readonly unknown[]): AsyncState<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [tick, setTick] = useState<number>(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    load().then(
      (result) => {
        if (cancelled) return;
        setData(result);
        setLoading(false);
      },
      (err: unknown) => {
        if (cancelled) return;
        setError(describeError(err));
        setLoading(false);
      },
    );
    return () => {
      cancelled = true;
    };
  }, [tick, ...deps]);

  const reload = useCallback(() => setTick((t) => t + 1), []);
  return { data, error, loading, reload };
}

export function EmptyRow({ colSpan, text }: { colSpan: number; text: string }): ReactNode {
  return (
    <tr>
      <td colSpan={colSpan} className="muted">
        {text}
      </td>
    </tr>
  );
}
