// k6 fleet model from the architecture guide, section 12. Not an approved readiness envelope.
// Run against a dev API with Auth:AllowDevAuth=true:
//   k6 run -e API=https://localhost:7210 -e DEVICES=200 -e INSECURE=1 tools/load/fleet.js
import http from 'k6/http';
import { check, sleep } from 'k6';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const API = __ENV.API || 'https://localhost:7210';
const DEVICES = Number(__ENV.DEVICES || 100);
const DISPLAYS_PER_DEVICE = 1.3;
const DECISION_INTERVAL_S = 30;
const HEARTBEAT_INTERVAL_S = 300;
const EVENTS_PER_AD = 5;

export const options = {
  insecureSkipTLSVerify: __ENV.INSECURE === '1',
  scenarios: {
    fleet: { executor: 'per-vu-iterations', vus: DEVICES, iterations: 1, maxDuration: '15m' },
  },
  thresholds: {
    'http_req_duration{name:decision}': ['p(95)<300'],
    'http_req_duration{name:events}': ['p(95)<500'],
    'http_req_duration{name:heartbeat}': ['p(95)<300'],
    http_req_failed: ['rate<0.01'],
  },
};

const json = { headers: { 'Content-Type': 'application/json' } };

export default function () {
  const deviceId = uuidv4();
  const secret = uuidv4();
  const reg = http.post(`${API}/api/v1/devices/dev-register`, JSON.stringify({ deviceId, secret, appVersion: 'k6' }), { ...json, tags: { name: 'register' } });
  check(reg, { 'registered': (r) => r.status === 200 });
  const auth = { headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${secret}` } };

  const cycles = Number(__ENV.CYCLES || 10);
  const jitter = Math.random() * DECISION_INTERVAL_S; // spread the fleet so it never ticks in lockstep
  sleep(jitter);

  for (let i = 0; i < cycles; i++) {
    if (i % Math.round(HEARTBEAT_INTERVAL_S / DECISION_INTERVAL_S) === 0) {
      const hb = http.post(`${API}/api/v1/devices/${deviceId}/heartbeat`, JSON.stringify({ appVersion: 'k6', cpuArchitecture: 'x64', health: { cpu: 1 } }), { ...auth, tags: { name: 'heartbeat' } });
      check(hb, { 'heartbeat ok': (r) => r.status === 200 });
    }

    const displays = Math.random() < DISPLAYS_PER_DEVICE - 1 ? 2 : 1;
    const events = [];
    for (let d = 0; d < displays; d++) {
      const displayKey = `disp-${d}`;
      const body = JSON.stringify({ placementId: 'bar', idempotencyKey: uuidv4(), configurationVersion: 1, supportedFormats: ['400x225', '384x216', '300x250'] });
      const dec = http.post(`${API}/api/v1/devices/${deviceId}/displays/${displayKey}/decisions`, body, { ...auth, tags: { name: 'decision' } });
      check(dec, { 'decision 200/204': (r) => r.status === 200 || r.status === 204 });
      const decisionId = dec.status === 200 ? dec.json('decisionId') : null;
      const creativeId = dec.status === 200 ? dec.json('creative.creativeId') : null;
      const types = ['AdRequested', dec.status === 200 ? 'AdReturned' : 'NoFill', 'RenderStarted', 'VisibilityObserved', 'VisibilityLost'];
      for (let e = 0; e < EVENTS_PER_AD; e++) {
        events.push({
          clientEventId: uuidv4(), deviceId, sessionId: 'k6', displayKey, decisionId, creativeId,
          type: types[e], deviceTimeUtc: new Date().toISOString(), reportedDurationMs: types[e] === 'VisibilityLost' ? 12000 : null,
          configurationVersion: 1, schemaVersion: 1,
        });
      }
    }

    const ev = http.post(`${API}/api/v1/events/batches`, JSON.stringify({ events }), { ...auth, tags: { name: 'events' } });
    check(ev, { 'events accepted': (r) => r.status === 200 && r.json('rejected').length === 0 });
    sleep(DECISION_INTERVAL_S * (Number(__ENV.SPEEDUP) ? 1 / Number(__ENV.SPEEDUP) : 1));
  }
}
