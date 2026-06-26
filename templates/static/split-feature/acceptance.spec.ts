import { test, expect } from "@playwright/test";

// Known-good, render-only acceptance suite shipped WITH the template — NOT LLM-authored.
// It asserts the SHARED STRUCTURAL CONTRACT every static template emits (data-testid landmarks,
// the feature grid, legal links, and the deploy-substituted mailto), never brand copy or token
// values. Because the structure is identical across templates, this file is template-agnostic:
// the assembler ships it verbatim. Contact mailtos are deploy-substituted (__CONTACT_EMAIL__ ->
// real address), so we assert the scheme (/^mailto:/), never the exact href — see issue #190.

test.describe("static template — render-only acceptance", () => {
  test.beforeEach(async ({ page }) => { await page.goto("/"); });

  test("app shell renders", async ({ page }) => {
    await expect(page.getByTestId("app-ready")).toBeVisible();
  });

  test("hero shows an h1 and a contact CTA", async ({ page }) => {
    const hero = page.getByTestId("hero");
    await expect(hero).toBeVisible();
    await expect(hero.getByRole("heading", { level: 1 })).toBeVisible();
    const cta = page.getByTestId("hero-cta");
    await expect(cta).toBeVisible();
    await expect(cta).toHaveAttribute("href", /^mailto:/);
  });

  test("features section lists multiple cards", async ({ page }) => {
    const cards = page.locator("#features .feature-card");
    expect(await cards.count()).toBeGreaterThanOrEqual(3);
  });

  test("footer exposes a contact link and both legal pages", async ({ page }) => {
    await expect(page.getByTestId("footer-contact")).toHaveAttribute("href", /^mailto:/);
    await expect(page.locator('a[href="/privacy-policy.html"]')).toBeVisible();
    await expect(page.locator('a[href="/terms-of-service.html"]')).toBeVisible();
  });

  test("no unfilled template tokens remain", async ({ page }) => {
    const body = await page.locator("body").innerText();
    expect(body).not.toMatch(/\{\{[^}]+\}\}/); // every content slot was filled at assembly
  });
});
