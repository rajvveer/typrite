import { useState, type FormEvent, type ReactNode } from "react";
import { listAudit } from "../api";
import { EmptyRow, ErrorText, formatUtc, useAsync } from "./shared";

export function AuditPage(): ReactNode {
  const [resourceInput, setResourceInput] = useState<string>("");
  const [resource, setResource] = useState<string>("");
  const state = useAsync(() => listAudit(resource), [resource]);
  const items = state.data?.items ?? [];

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    setResource(resourceInput.trim());
  }

  return (
    <section>
      <h1>Audit</h1>
      <form className="inline-form" onSubmit={onSubmit}>
        <label htmlFor="audit-resource">Resource</label>
        <input
          id="audit-resource"
          type="text"
          placeholder="e.g. campaign/…"
          value={resourceInput}
          onChange={(e) => setResourceInput(e.target.value)}
        />
        <button type="submit" disabled={state.loading}>
          {state.loading ? "Loading…" : "Filter"}
        </button>
        <button
          type="button"
          disabled={state.loading || (resource.length === 0 && resourceInput.length === 0)}
          onClick={() => {
            setResourceInput("");
            setResource("");
          }}
        >
          Clear
        </button>
      </form>
      <ErrorText error={state.error} />
      <table>
        <thead>
          <tr>
            <th scope="col">Time (UTC)</th>
            <th scope="col">Actor</th>
            <th scope="col">Action</th>
            <th scope="col">Resource</th>
            <th scope="col">Metadata</th>
          </tr>
        </thead>
        <tbody>
          {items.length === 0 ? (
            <EmptyRow colSpan={5} text={state.loading ? "Loading…" : "No audit entries"} />
          ) : (
            items.map((entry, index) => (
              <tr key={`${entry.timeUtc}-${entry.actor}-${entry.action}-${index}`}>
                <td>{formatUtc(entry.timeUtc)}</td>
                <td>{entry.actor}</td>
                <td>{entry.action}</td>
                <td className="mono">{entry.resource}</td>
                <td>
                  <code className="json">{entry.metadataJson ?? "—"}</code>
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </section>
  );
}
