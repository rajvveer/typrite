import { expect, test, type Page } from "@playwright/test";

const SESSION_KEY = "typrite.admin.session";

test.beforeEach(async ({ page }) => {
  await page.route("**/api/v1/auth/config", route => route.fulfill({
    json: { allowDevAuth: true },
  }));
});

test("production sign-in never offers a subject or role selector", async ({ page }) => {
  await page.route("**/api/v1/auth/config", route => route.fulfill({
    json: { allowDevAuth: false, authority: "https://identity.example", clientId: "admin", scope: "openid profile" },
  }));
  await page.goto("/#/login");
  await expect(page.getByRole("heading", { name: "Operator sign-in" })).toBeVisible();
  await expect(page.getByLabel("Subject")).toHaveCount(0);
  await expect(page.getByLabel("Role", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Sign in" })).toBeEnabled();
});

test("missing hosted auth configuration fails closed", async ({ page }) => {
  await page.route("**/api/v1/auth/config", route => route.fulfill({ json: { allowDevAuth: false } }));
  await page.goto("/#/login");
  await expect(page.getByRole("alert")).toContainText("not configured");
  await expect(page.getByRole("button", { name: "Sign in" })).toBeDisabled();
  await expect(page.getByLabel("Role", { exact: true })).toHaveCount(0);
});

test("an unsolicited OIDC callback cannot create a session", async ({ page }) => {
  await page.route("**/api/v1/auth/config", route => route.fulfill({
    json: { allowDevAuth: false, authority: "https://identity.example", clientId: "admin", scope: "openid profile" },
  }));
  await page.goto("/?code=forged&state=not-issued#/login");
  await expect(page.getByRole("alert")).toBeVisible();
  await expect(page).toHaveURL(/\/#\/login$/);
  expect(await page.evaluate(key => sessionStorage.getItem(key), SESSION_KEY)).toBeNull();
});

test("OIDC uses PKCE and the API determines the operator role", async ({ page }) => {
  const authority = "https://identity.example";
  await page.route("**/api/v1/auth/config", route => route.fulfill({
    json: { allowDevAuth: false, authority, clientId: "admin", scope: "openid profile" },
  }));
  await page.route(`${authority}/.well-known/openid-configuration`, route => route.fulfill({
    json: { issuer: authority, authorization_endpoint: `${authority}/authorize`, token_endpoint: `${authority}/token` },
    headers: { "access-control-allow-origin": "http://localhost:4173" },
  }));
  await page.route(`${authority}/authorize**`, route => route.fulfill({ contentType: "text/html", body: "Test identity provider" }));
  await page.route("**/api/v1/admin/session", async route => {
    expect(route.request().headers().authorization).toBe("Bearer test-access-token");
    await route.fulfill({ json: {
      subject: "verified-viewer", role: "Viewer", accountId: "00000000-0000-0000-0000-000000000001",
    } });
  });

  await page.goto("/#/login");
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL(`${authority}/authorize**`);
  const authorization = new URL(page.url()).searchParams;
  expect(authorization.get("response_type")).toBe("code");
  expect(authorization.get("code_challenge_method")).toBe("S256");
  expect(authorization.get("code_challenge")).toBeTruthy();
  expect(authorization.get("redirect_uri")).toBe("http://localhost:4173/");
  const state = authorization.get("state");
  expect(state).toBeTruthy();

  await page.route(`${authority}/token`, async route => {
    const request = new URLSearchParams(route.request().postData() ?? "");
    expect(request.get("code_verifier")).toBeTruthy();
    expect(request.get("client_secret")).toBeNull();
    expect(request.get("code")).toBe("test-code");
    const now = Math.floor(Date.now() / 1000);
    const payload = { iss: authority, sub: "verified-viewer", aud: "admin", iat: now, exp: now + 300,
      nonce: authorization.get("nonce"), role: "Administrator" };
    const idToken = `${Buffer.from(JSON.stringify({ alg: "RS256" })).toString("base64url")}.${Buffer.from(JSON.stringify(payload)).toString("base64url")}.test`;
    await route.fulfill({
      json: { access_token: "test-access-token", token_type: "Bearer", expires_in: 300, id_token: idToken },
      headers: { "access-control-allow-origin": "http://localhost:4173" },
    });
  });
  await page.goto(`/?code=test-code&state=${encodeURIComponent(state!)}`);
  await expect(page.getByRole("navigation", { name: "Main" })).toBeVisible();
  await expect(page.locator(".session strong")).toHaveText("Viewer");
  await expect(page).toHaveURL(/#\/devices$/);
});

async function seedFakeSession(page: Page): Promise<void> {
  await page.addInitScript(
    ([key, value]) => {
      sessionStorage.setItem(key as string, value as string);
    },
    [
      SESSION_KEY,
      JSON.stringify({
        token: "fake.jwt.token",
        role: "Administrator",
        subject: "smoke-test",
        accountId: null,
      }),
    ],
  );
}

test("login page renders", async ({ page }) => {
  await page.goto("/#/login");
  await expect(page.getByRole("heading", { name: "Development sign-in" })).toBeVisible();
  await expect(page.getByLabel("Subject")).toBeVisible();
  await expect(page.getByLabel("Role")).toBeVisible();
  await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
});

test("submitting the sign-in form without an API shows an error", async ({ page }) => {
  await page.goto("/#/login");
  await page.getByLabel("Subject").fill("smoke-test");
  await page.getByLabel("Role").selectOption("Administrator");
  await page.getByRole("button", { name: "Sign in" }).click();
  const alert = page.getByRole("alert");
  await expect(alert).toBeVisible();
  await expect(alert).not.toBeEmpty();
  await expect(page.getByRole("heading", { name: "Development sign-in" })).toBeVisible();
});

test("protected routes redirect to login without a session", async ({ page }) => {
  await page.goto("/#/devices");
  await expect(page.getByRole("heading", { name: "Development sign-in" })).toBeVisible();
  await expect(page).toHaveURL(/#\/login$/);
});

const ROUTES: ReadonlyArray<{ hash: string; heading: string }> = [
  { hash: "#/devices", heading: "Devices" },
  { hash: "#/campaigns", heading: "Campaigns" },
  { hash: "#/releases", heading: "Releases" },
  { hash: "#/reports", heading: "Reports" },
  { hash: "#/audit", heading: "Audit" },
];

for (const route of ROUTES) {
  test(`${route.hash} renders its heading and an API error with a fake token`, async ({ page }) => {
    await seedFakeSession(page);
    await page.goto(`/${route.hash}`);
    await expect(page.getByRole("heading", { level: 1, name: route.heading })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Main" })).toBeVisible();
    await expect(page.getByText("Administrator")).toBeVisible();
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
    const alerts = page.getByRole("alert");
    await expect(alerts.first()).toBeVisible();
    await expect(alerts.first()).not.toBeEmpty();
  });
}

test("sign out returns to the login page", async ({ page }) => {
  await seedFakeSession(page);
  await page.goto("/#/devices");
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByRole("heading", { name: "Development sign-in" })).toBeVisible();
});
