import { useEffect, useState, type ReactNode } from "react";
import {
  DEVICE_COMMAND_TYPES,
  describeError,
  listDevices,
  sendDeviceCommand,
  type Device,
  type DeviceCommandType,
} from "../api";
import { EmptyRow, ErrorText, Notice, formatUtc } from "./shared";

function DeviceRow({ device }: { device: Device }): ReactNode {
  const [command, setCommand] = useState<DeviceCommandType>("refresh-configuration");
  const [busy, setBusy] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function send(): Promise<void> {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = await sendDeviceCommand(device.id, { type: command });
      setNotice(`Queued ${command} (${result.id})`);
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy(false);
    }
  }

  const selectId = `cmd-${device.id}`;
  return (
    <tr>
      <td className="mono">{device.id}</td>
      <td>{device.status}</td>
      <td>{device.appVersion}</td>
      <td>{device.cpuArchitecture}</td>
      <td>{formatUtc(device.lastSeenUtc)}</td>
      <td>{formatUtc(device.enrolledAtUtc)}</td>
      <td>{device.credentialKind}</td>
      <td>{device.configurationVersion ?? "—"}</td>
      <td>
        <div className="inline-form">
          <label htmlFor={selectId} className="visually-hidden">
            Command for device {device.id}
          </label>
          <select id={selectId} value={command} onChange={(e) => setCommand(e.target.value as DeviceCommandType)}>
            {DEVICE_COMMAND_TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
          <button type="button" onClick={() => void send()} disabled={busy}>
            {busy ? "Sending…" : "Send command"}
          </button>
        </div>
        <ErrorText error={error} />
        <Notice text={notice} />
      </td>
    </tr>
  );
}

export function DevicesPage(): ReactNode {
  const [items, setItems] = useState<Device[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState<boolean>(false);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  async function load(nextCursor: string | null, replace: boolean): Promise<void> {
    setLoading(true);
    setError(null);
    try {
      const page = await listDevices(nextCursor);
      setItems((prev) => (replace ? page.items : [...prev, ...page.items]));
      setCursor(page.nextCursor ?? null);
      setHasMore(typeof page.nextCursor === "string" && page.nextCursor.length > 0);
    } catch (err) {
      setError(describeError(err));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load(null, true);
  }, []);

  return (
    <section>
      <div className="page-header">
        <h1>Devices</h1>
        <button type="button" onClick={() => void load(null, true)} disabled={loading}>
          Refresh
        </button>
      </div>
      <ErrorText error={error} />
      <table>
        <thead>
          <tr>
            <th scope="col">ID</th>
            <th scope="col">Status</th>
            <th scope="col">App version</th>
            <th scope="col">CPU</th>
            <th scope="col">Last seen (UTC)</th>
            <th scope="col">Enrolled (UTC)</th>
            <th scope="col">Credential</th>
            <th scope="col">Config version</th>
            <th scope="col">Command</th>
          </tr>
        </thead>
        <tbody>
          {items.length === 0 ? (
            <EmptyRow colSpan={9} text={loading ? "Loading…" : "No devices"} />
          ) : (
            items.map((d) => <DeviceRow key={d.id} device={d} />)
          )}
        </tbody>
      </table>
      {hasMore ? (
        <button type="button" onClick={() => void load(cursor, false)} disabled={loading}>
          {loading ? "Loading…" : "Load more"}
        </button>
      ) : null}
    </section>
  );
}
