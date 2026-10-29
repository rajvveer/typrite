import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import { getDeliveryReport, getReconciliation, type DeliveryRow } from "../api";
import { EmptyRow, ErrorText, formatNumber, formatUtc, useAsync } from "./shared";

function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function defaultRange(): { from: string; to: string } {
  const to = new Date();
  const from = new Date(to.getTime() - 6 * 24 * 60 * 60 * 1000);
  return { from: isoDate(from), to: isoDate(to) };
}

interface DayGroup {
  day: string;
  rows: DeliveryRow[];
  count: number;
  distinctDevices: number;
  visibleMilliseconds: number;
}

function groupByDay(items: readonly DeliveryRow[]): DayGroup[] {
  const map = new Map<string, DayGroup>();
  for (const row of items) {
    const existing = map.get(row.day);
    if (existing === undefined) {
      map.set(row.day, {
        day: row.day,
        rows: [row],
        count: row.count,
        distinctDevices: row.distinctDevices,
        visibleMilliseconds: row.visibleMilliseconds,
      });
    } else {
      existing.rows.push(row);
      existing.count += row.count;
      existing.distinctDevices += row.distinctDevices;
      existing.visibleMilliseconds += row.visibleMilliseconds;
    }
  }
  return [...map.values()].sort((a, b) => a.day.localeCompare(b.day));
}

function DeliverySection(): ReactNode {
  const initial = useMemo(defaultRange, []);
  const [fromInput, setFromInput] = useState<string>(initial.from);
  const [toInput, setToInput] = useState<string>(initial.to);
  const [range, setRange] = useState<{ from: string; to: string }>(initial);
  const [validation, setValidation] = useState<string | null>(null);

  const state = useAsync(() => getDeliveryReport(range.from, range.to), [range.from, range.to]);
  const groups = useMemo(() => groupByDay(state.data?.items ?? []), [state.data]);

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    if (fromInput > toInput) {
      setValidation("The start date must not be after the end date");
      return;
    }
    setValidation(null);
    setRange({ from: fromInput, to: toInput });
  }

  return (
    <section>
      <h2>Delivery</h2>
      <form className="inline-form" onSubmit={onSubmit}>
        <label htmlFor="delivery-from">From</label>
        <input id="delivery-from" type="date" required value={fromInput} onChange={(e) => setFromInput(e.target.value)} />
        <label htmlFor="delivery-to">To</label>
        <input id="delivery-to" type="date" required value={toInput} onChange={(e) => setToInput(e.target.value)} />
        <button type="submit" disabled={state.loading}>
          {state.loading ? "Loading…" : "Run report"}
        </button>
      </form>
      <ErrorText error={validation} />
      <ErrorText error={state.error} />
      <table>
        <thead>
          <tr>
            <th scope="col">Day</th>
            <th scope="col">Campaign</th>
            <th scope="col">Creative</th>
            <th scope="col">Type</th>
            <th scope="col">Count</th>
            <th scope="col">Distinct devices</th>
            <th scope="col">Visible (ms)</th>
          </tr>
        </thead>
        {groups.length === 0 ? (
          <tbody>
            <EmptyRow colSpan={7} text={state.loading ? "Loading…" : "No delivery events in this range"} />
          </tbody>
        ) : (
          groups.map((group) => (
            <tbody key={group.day}>
              <tr className="group-row">
                <th scope="rowgroup">{group.day}</th>
                <td colSpan={3} className="muted">
                  {group.rows.length} row{group.rows.length === 1 ? "" : "s"}
                </td>
                <td>{formatNumber(group.count)}</td>
                <td>{formatNumber(group.distinctDevices)}</td>
                <td>{formatNumber(group.visibleMilliseconds)}</td>
              </tr>
              {group.rows.map((row, index) => (
                <tr key={`${row.day}-${row.campaignId}-${row.creativeId}-${row.type}-${index}`}>
                  <td></td>
                  <td className="mono">{row.campaignId}</td>
                  <td className="mono">{row.creativeId}</td>
                  <td>{row.type}</td>
                  <td>{formatNumber(row.count)}</td>
                  <td>{formatNumber(row.distinctDevices)}</td>
                  <td>{formatNumber(row.visibleMilliseconds)}</td>
                </tr>
              ))}
            </tbody>
          ))
        )}
      </table>
    </section>
  );
}

function ReconciliationSection(): ReactNode {
  const state = useAsync(() => getReconciliation(), []);
  const items = state.data?.items ?? [];

  return (
    <section>
      <div className="page-header">
        <h2>Reconciliation</h2>
        <button type="button" onClick={state.reload} disabled={state.loading}>
          Refresh
        </button>
      </div>
      <ErrorText error={state.error} />
      <table>
        <thead>
          <tr>
            <th scope="col">ID</th>
            <th scope="col">Partner</th>
            <th scope="col">Period</th>
            <th scope="col">Status</th>
            <th scope="col">Our count</th>
            <th scope="col">Partner count</th>
            <th scope="col">Mismatch summary</th>
            <th scope="col">Started (UTC)</th>
            <th scope="col">Finished (UTC)</th>
          </tr>
        </thead>
        <tbody>
          {items.length === 0 ? (
            <EmptyRow colSpan={9} text={state.loading ? "Loading…" : "No reconciliation runs"} />
          ) : (
            items.map((run) => (
              <tr key={run.id}>
                <td className="mono">{run.id}</td>
                <td>{run.partnerId}</td>
                <td>
                  {run.periodStart} – {run.periodEnd}
                </td>
                <td>{run.status}</td>
                <td>{formatNumber(run.ourCount)}</td>
                <td>{formatNumber(run.partnerCount)}</td>
                <td>
                  <code className="json">{run.mismatchSummaryJson ?? "—"}</code>
                </td>
                <td>{formatUtc(run.startedAtUtc)}</td>
                <td>{formatUtc(run.finishedAtUtc)}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </section>
  );
}

export function ReportsPage(): ReactNode {
  return (
    <section>
      <h1>Reports</h1>
      <DeliverySection />
      <ReconciliationSection />
    </section>
  );
}
