const { test, expect } = require('@playwright/test');

test('VecSketch app loads correctly', async ({ page }) => {
  // Navigate to the app
  await page.goto('http://localhost:5149');

  // Wait for Blazor to load (look for the canvas or toolbar)
  await page.waitForSelector('.sketch-container', { timeout: 30000 });

  // Check that the toolbar is visible
  const toolbar = await page.locator('.toolbar');
  await expect(toolbar).toBeVisible();

  // Check that the SVG canvas is visible
  const svg = await page.locator('svg');
  await expect(svg).toBeVisible();

  // Check that the AI panel header is visible
  const aiPanel = await page.locator('.ai-panel');
  await expect(aiPanel).toBeVisible();

  console.log('App loaded successfully!');
});
