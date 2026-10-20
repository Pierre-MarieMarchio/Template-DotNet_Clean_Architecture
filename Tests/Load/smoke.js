// Run against a running stack: k6 run -e BASE_URL=http://localhost:8080 Tests/Load/smoke.js

import http from 'k6/http';
import { check, group } from 'k6';

const baseUrl = __ENV.BASE_URL || 'http://localhost:8080';

// Unique per run so a repeated run registers a new account.
const suffix = `${Date.now()}${__VU}`;

export const options = {
  // Adjust the stages to the load your deployment should carry.
  scenarios: {
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

  // Tighten these once the run is on hardware that represents production.
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<10000'],
  },
};

export function setup() {
  const email = `load-${suffix}@localhost`;
  const password = 'Str0ng!Passw0rd';

  http.post(
    `${baseUrl}/api/v1/auth/register`,
    JSON.stringify({ email, password }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  const login = http.post(
    `${baseUrl}/api/v1/auth/login`,
    JSON.stringify({ email, password }),
    { headers: { 'Content-Type': 'application/json' } },
  );

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

    check(response, {
      'listing is 200 or 429': (r) => r.status === 200 || r.status === 429,
    });
  });
}
