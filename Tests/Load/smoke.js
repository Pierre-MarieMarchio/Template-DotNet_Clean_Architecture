// A load smoke test, not a benchmark.
//
// It exists because this template had never been observed under any concurrency at all: every
// timeout, pool size and rate limit in it was chosen by reasoning, and reasoning is what this
// checks. It is deliberately small — a few minutes, a laptop's worth of load — because the useful
// question is not "how fast is it" but "does anything come apart when more than one caller
// arrives", which is a question a short run answers and a long one only makes expensive.
//
// Run it against a stack you started yourself:
//
//   docker compose up -d --build
//   k6 run -e BASE_URL=http://localhost:8080 Tests/Load/smoke.js
//
// CI runs it non-blocking. A red result there is a signal to look, never a broken build: a shared
// runner's timings are not a property of this code, and a threshold tight enough to be meaningful
// on real hardware would be noise on a hosted one.

import http from 'k6/http';
import { check, group } from 'k6';

const baseUrl = __ENV.BASE_URL || 'http://localhost:8080';

// Unique per run, so a repeated run does not collide with the account the previous one registered.
const suffix = `${Date.now()}${__VU}`;

export const options = {
  scenarios: {
    // Ramp rather than a step: a cold start hitting full concurrency measures JIT and connection
    // pool growth, which is not what any of this is about.
    browsing: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '20s', target: 10 },
        { duration: '40s', target: 10 },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },

  // Thresholds that describe a broken system rather than a slow one. A p(95) in the hundreds of
  // milliseconds on a shared runner means nothing; a request that fails, or one that takes ten
  // seconds, means something regardless of the hardware.
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<10000'],
  },
};

export function setup() {
  // One account for the whole run. Registering per VU would measure ASP.NET Identity's password
  // hashing — which is deliberately expensive — rather than the endpoints under test.
  const email = `load-${suffix}@localhost`;
  const password = 'Str0ng!Passw0rd';

  http.post(
    `${baseUrl}/api/v1/auth/register`,
    JSON.stringify({ userName: `load${suffix}`, email, password }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  const login = http.post(
    `${baseUrl}/api/v1/auth/login`,
    JSON.stringify({ email, password }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  // Development composes IdentitySeed off and RequireConfirmedEmail on, so this may legitimately
  // refuse. The run then exercises the anonymous surface only, which is still worth doing: the
  // health probes and the rate limiter are where a first concurrency defect tends to show.
  const token = login.status === 200 ? login.json('tokens.accessToken') : null;

  return { token };
}

export default function (data) {
  const authenticated = data.token
    ? { headers: { Authorization: `Bearer ${data.token}` } }
    : null;

  group('liveness', () => {
    const response = http.get(`${baseUrl}/health`);

    check(response, {
      'health is 200': (r) => r.status === 200,
    });
  });

  group('readiness', () => {
    // Readiness touches the database, so this is the one probe that says anything about the
    // connection pool — the setting docs/CONFIGURATION.md warns is the scarce resource.
    const response = http.get(`${baseUrl}/health/ready`);

    check(response, {
      'ready is 200': (r) => r.status === 200,
    });
  });

  if (!authenticated) {
    return;
  }

  group('paged read', () => {
    const response = http.get(`${baseUrl}/api/v1/todo-lists?pageSize=20`, authenticated);

    // 429 is a pass, not a failure: the rate limiter doing its job under load is the correct
    // outcome, and a threshold that treated it as an error would push someone to loosen the limit
    // to make a load test go green.
    check(response, {
      'listing is 200 or 429': (r) => r.status === 200 || r.status === 429,
    });
  });
}
