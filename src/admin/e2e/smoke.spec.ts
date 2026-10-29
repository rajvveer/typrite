import { expect, test, type Page } from "@playwright/test";

const SESSION_KEY = "typrite.admin.session";

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
