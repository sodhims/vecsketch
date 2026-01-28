const { test, expect } = require('@playwright/test');

test('cursor alignment - drawing at click position', async ({ page }) => {
  await page.goto('http://localhost:5149');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000); // Wait for Blazor to initialize

  // Get the SVG canvas element
  const canvas = page.locator('svg.canvas');
  await expect(canvas).toBeVisible();

  // Get canvas bounding box
  const canvasBox = await canvas.boundingBox();
  console.log('Canvas bounding box:', canvasBox);

  // Get scale info to understand the transformation
  const scaleInfo = await page.evaluate(() => {
    const svg = document.querySelector('svg.canvas');
    if (!svg) return null;
    return window.getSvgScaleInfo ? window.getSvgScaleInfo(svg) : 'getSvgScaleInfo not found';
  });
  console.log('Scale info:', scaleInfo);

  // Select Rectangle tool (index 3)
  await page.selectOption('.tool-select', '3');
  await page.waitForTimeout(500);

  // Calculate offset to click within the viewBox area (past the letterbox)
  // viewBoxOffsetX is the horizontal letterbox offset
  const viewBoxOffsetX = scaleInfo ? scaleInfo.viewBoxOffsetX : 0;
  const viewBoxOffsetY = scaleInfo ? scaleInfo.viewBoxOffsetY : 0;

  // Click at SVG coordinates (100, 100) - add letterbox offset to get screen offset
  const svgTargetX = 100;
  const svgTargetY = 100;
  const scale = scaleInfo ? scaleInfo.scaleX : 1;

  const offsetX = viewBoxOffsetX + svgTargetX * scale;
  const offsetY = viewBoxOffsetY + svgTargetY * scale;
  const dragSize = 100; // Drag 100 SVG units

  const startX = canvasBox.x + offsetX;
  const startY = canvasBox.y + offsetY;
  const endX = startX + dragSize * scale;
  const endY = startY + dragSize * scale;

  console.log(`Drawing from (${startX}, ${startY}) to (${endX}, ${endY})`);

  // Draw rectangle
  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();

  await page.waitForTimeout(500);

  // Get the created rectangle element
  const rect = page.locator('svg.canvas rect').last();

  // Check if rect was created
  const rectCount = await page.locator('svg.canvas rect').count();
  console.log('Rectangle count:', rectCount);

  if (rectCount > 1) { // More than just the grid background
    const rectX = await rect.getAttribute('x');
    const rectY = await rect.getAttribute('y');
    const rectWidth = await rect.getAttribute('width');
    const rectHeight = await rect.getAttribute('height');

    console.log(`Rectangle position: x=${rectX}, y=${rectY}, width=${rectWidth}, height=${rectHeight}`);
    console.log(`Expected: x≈${svgTargetX}, y≈${svgTargetY}, size≈${dragSize}`);

    // Get SVG viewBox info
    const viewBox = await canvas.getAttribute('viewBox');
    console.log('ViewBox:', viewBox);

    // Verify the rectangle is at approximately the expected SVG coordinates
    // Allow 10% tolerance for minor coordinate differences
    const tolerance = 15;
    expect(Math.abs(parseFloat(rectX) - svgTargetX)).toBeLessThan(tolerance);
    expect(Math.abs(parseFloat(rectY) - svgTargetY)).toBeLessThan(tolerance);
    expect(Math.abs(parseFloat(rectWidth) - dragSize)).toBeLessThan(tolerance);
    expect(Math.abs(parseFloat(rectHeight) - dragSize)).toBeLessThan(tolerance);
  }

  // Take screenshot for visual verification
  await page.screenshot({ path: 'tests/cursor-alignment-test.png', fullPage: true });
});

test('cursor alignment - line drawing', async ({ page }) => {
  await page.goto('http://localhost:5149');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000);

  const canvas = page.locator('svg.canvas');
  await expect(canvas).toBeVisible();

  const canvasBox = await canvas.boundingBox();

  // Get scale info
  const scaleInfo = await page.evaluate(() => {
    const svg = document.querySelector('svg.canvas');
    return window.getSvgScaleInfo ? window.getSvgScaleInfo(svg) : null;
  });
  console.log('Scale info:', scaleInfo);

  // Select Line tool (index 1)
  await page.selectOption('.tool-select', '1');
  await page.waitForTimeout(500);

  // Calculate offsets to draw at SVG coordinates (150, 150) to (300, 300)
  const viewBoxOffsetX = scaleInfo ? scaleInfo.viewBoxOffsetX : 0;
  const viewBoxOffsetY = scaleInfo ? scaleInfo.viewBoxOffsetY : 0;
  const scale = scaleInfo ? scaleInfo.scaleX : 1;

  const svgStartX = 150;
  const svgStartY = 150;
  const svgEndX = 300;
  const svgEndY = 300;

  const startX = canvasBox.x + viewBoxOffsetX + svgStartX * scale;
  const startY = canvasBox.y + viewBoxOffsetY + svgStartY * scale;
  const endX = canvasBox.x + viewBoxOffsetX + svgEndX * scale;
  const endY = canvasBox.y + viewBoxOffsetY + svgEndY * scale;

  console.log(`Drawing line from (${startX}, ${startY}) to (${endX}, ${endY})`);

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();

  await page.waitForTimeout(500);

  // Check line was created
  const line = page.locator('svg.canvas line').last();
  const x1 = await line.getAttribute('x1');
  const y1 = await line.getAttribute('y1');
  const x2 = await line.getAttribute('x2');
  const y2 = await line.getAttribute('y2');

  console.log(`Line: (${x1}, ${y1}) to (${x2}, ${y2})`);
  console.log(`Expected: (${svgStartX}, ${svgStartY}) to (${svgEndX}, ${svgEndY})`);

  // Verify the line is at approximately the expected SVG coordinates
  const tolerance = 15;
  expect(Math.abs(parseFloat(x1) - svgStartX)).toBeLessThan(tolerance);
  expect(Math.abs(parseFloat(y1) - svgStartY)).toBeLessThan(tolerance);
  expect(Math.abs(parseFloat(x2) - svgEndX)).toBeLessThan(tolerance);
  expect(Math.abs(parseFloat(y2) - svgEndY)).toBeLessThan(tolerance);

  await page.screenshot({ path: 'tests/line-alignment-test.png', fullPage: true });
});
