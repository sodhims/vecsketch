const { test, expect } = require('@playwright/test');

test('page loads', async ({ page }) => {
  await page.goto('http://localhost:5199');
  await page.waitForTimeout(5000);
  const title = await page.title();
  console.log('Title:', title);
  expect(title).toContain('VecSketch');
});
