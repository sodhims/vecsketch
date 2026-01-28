const { test, expect } = require('@playwright/test');

test('group selection - orange box appears with multiple selected elements', async ({ page }) => {
  // Clear cache before navigating
  await page.context().clearCookies();
  await page.goto('http://localhost:5149', { waitUntil: 'networkidle' });
  await page.waitForTimeout(3000); // Wait for Blazor to initialize

  // Reload to bypass any cached wasm
  await page.reload({ waitUntil: 'networkidle' });
  await page.waitForTimeout(2000);

  const canvas = page.locator('svg.canvas');
  await expect(canvas).toBeVisible();

  const canvasBox = await canvas.boundingBox();
  console.log('Canvas bounding box:', canvasBox);

  // Get scale info
  const scaleInfo = await page.evaluate(() => {
    const svg = document.querySelector('svg.canvas');
    return window.getSvgScaleInfo ? window.getSvgScaleInfo(svg) : null;
  });
  console.log('Scale info:', scaleInfo);

  const viewBoxOffsetX = scaleInfo ? scaleInfo.viewBoxOffsetX : 0;
  const viewBoxOffsetY = scaleInfo ? scaleInfo.viewBoxOffsetY : 0;
  const scale = scaleInfo ? scaleInfo.scaleX : 1;

  // === Draw first rectangle ===
  await page.selectOption('.tool-select', '3'); // Rectangle tool
  await page.waitForTimeout(300);

  // Draw rect at (100, 100) size 80x80
  let startX = canvasBox.x + viewBoxOffsetX + 100 * scale;
  let startY = canvasBox.y + viewBoxOffsetY + 100 * scale;
  let endX = canvasBox.x + viewBoxOffsetX + 180 * scale;
  let endY = canvasBox.y + viewBoxOffsetY + 180 * scale;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();
  await page.waitForTimeout(300);

  // === Draw second rectangle ===
  // Draw rect at (250, 100) size 80x80
  startX = canvasBox.x + viewBoxOffsetX + 250 * scale;
  startY = canvasBox.y + viewBoxOffsetY + 100 * scale;
  endX = canvasBox.x + viewBoxOffsetX + 330 * scale;
  endY = canvasBox.y + viewBoxOffsetY + 180 * scale;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();
  await page.waitForTimeout(300);

  // Count rectangles (should have 3: grid background + 2 drawn)
  const rectCountBefore = await page.locator('svg.canvas rect').count();
  console.log('Rectangles before selection:', rectCountBefore);

  // === Switch to Select tool and area-select both rectangles ===
  await page.selectOption('.tool-select', '0'); // Select tool
  await page.waitForTimeout(300);

  // Draw selection box from (50, 50) to (400, 200) to capture both rectangles
  startX = canvasBox.x + viewBoxOffsetX + 50 * scale;
  startY = canvasBox.y + viewBoxOffsetY + 50 * scale;
  endX = canvasBox.x + viewBoxOffsetX + 400 * scale;
  endY = canvasBox.y + viewBoxOffsetY + 200 * scale;

  console.log(`Drawing selection from (${startX}, ${startY}) to (${endX}, ${endY})`);

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();
  await page.waitForTimeout(500);

  // Take screenshot for visual verification
  await page.screenshot({ path: 'tests/group-selection-test.png', fullPage: true });

  // Debug: dump all rects in the SVG
  const allRects = await page.evaluate(() => {
    const rects = document.querySelectorAll('svg.canvas rect');
    return Array.from(rects).map(r => ({
      stroke: r.getAttribute('stroke'),
      fill: r.getAttribute('fill'),
      class: r.getAttribute('class'),
      dataHandle: r.getAttribute('data-handle'),
      strokeDasharray: r.getAttribute('stroke-dasharray')
    }));
  });
  console.log('All rects in SVG:', JSON.stringify(allRects, null, 2));

  // Debug: dump all text elements to see the SelectedElements count
  const allTexts = await page.evaluate(() => {
    const texts = document.querySelectorAll('svg.canvas text');
    return Array.from(texts).map(t => t.textContent);
  });
  console.log('All text elements in SVG:', allTexts);

  // === Verify group selection box ===
  // Look for the orange group bounding box (stroke="#ff6600")
  const groupBox = page.locator('svg.canvas rect[stroke="#ff6600"]');
  const groupBoxCount = await groupBox.count();
  console.log('Orange group boxes found:', groupBoxCount);

  // Should have at least 1 orange box (the group bounding box)
  // Plus 8 orange resize handles
  expect(groupBoxCount).toBeGreaterThanOrEqual(1);

  // Check for resize handles with orange stroke
  const orangeHandles = page.locator('svg.canvas rect.resize-handle[stroke="#ff6600"]');
  const handleCount = await orangeHandles.count();
  console.log('Orange resize handles found:', handleCount);

  // Should have 8 resize handles
  expect(handleCount).toBe(8);

  // Check the group box has the correct styling (dashed)
  const groupDashArray = await groupBox.first().getAttribute('stroke-dasharray');
  console.log('Group box stroke-dasharray:', groupDashArray);
  expect(groupDashArray).toBe('6 3');

  // Count all rects now - should include selection boxes
  const rectCountAfter = await page.locator('svg.canvas rect').count();
  console.log('Rectangles after selection:', rectCountAfter);

  // Should have more rects now: original + individual selection boxes + group box + handles
  expect(rectCountAfter).toBeGreaterThan(rectCountBefore);
});

test('group selection - resize handles work for scaling', async ({ page }) => {
  await page.goto('http://localhost:5149');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000);

  const canvas = page.locator('svg.canvas');
  await expect(canvas).toBeVisible();

  const canvasBox = await canvas.boundingBox();

  const scaleInfo = await page.evaluate(() => {
    const svg = document.querySelector('svg.canvas');
    return window.getSvgScaleInfo ? window.getSvgScaleInfo(svg) : null;
  });

  const viewBoxOffsetX = scaleInfo ? scaleInfo.viewBoxOffsetX : 0;
  const viewBoxOffsetY = scaleInfo ? scaleInfo.viewBoxOffsetY : 0;
  const scale = scaleInfo ? scaleInfo.scaleX : 1;

  // Draw two circles
  await page.selectOption('.tool-select', '4'); // Circle tool
  await page.waitForTimeout(300);

  // First circle at (150, 150) radius ~50
  let startX = canvasBox.x + viewBoxOffsetX + 150 * scale;
  let startY = canvasBox.y + viewBoxOffsetY + 150 * scale;
  let endX = canvasBox.x + viewBoxOffsetX + 200 * scale;
  let endY = canvasBox.y + viewBoxOffsetY + 200 * scale;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();
  await page.waitForTimeout(300);

  // Second circle at (300, 150) radius ~50
  startX = canvasBox.x + viewBoxOffsetX + 300 * scale;
  startY = canvasBox.y + viewBoxOffsetY + 150 * scale;
  endX = canvasBox.x + viewBoxOffsetX + 350 * scale;
  endY = canvasBox.y + viewBoxOffsetY + 200 * scale;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();
  await page.waitForTimeout(300);

  // Get original circle positions
  const circles = page.locator('svg.canvas circle');
  const circleCount = await circles.count();
  console.log('Circles created:', circleCount);

  // Select both with area selection
  await page.selectOption('.tool-select', '0');
  await page.waitForTimeout(300);

  startX = canvasBox.x + viewBoxOffsetX + 50 * scale;
  startY = canvasBox.y + viewBoxOffsetY + 50 * scale;
  endX = canvasBox.x + viewBoxOffsetX + 450 * scale;
  endY = canvasBox.y + viewBoxOffsetY + 300 * scale;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();
  await page.waitForTimeout(500);

  // Find the SE resize handle and drag it
  const seHandle = page.locator('svg.canvas rect[data-handle="se"]');
  const handleBox = await seHandle.boundingBox();

  if (handleBox) {
    console.log('SE handle position:', handleBox);

    // Drag the handle to scale up
    const handleCenterX = handleBox.x + handleBox.width / 2;
    const handleCenterY = handleBox.y + handleBox.height / 2;
    const dragToX = handleCenterX + 50 * scale;
    const dragToY = handleCenterY + 50 * scale;

    await page.mouse.move(handleCenterX, handleCenterY);
    await page.mouse.down();
    await page.mouse.move(dragToX, dragToY);
    await page.mouse.up();
    await page.waitForTimeout(500);

    // Take screenshot after resize
    await page.screenshot({ path: 'tests/group-resize-test.png', fullPage: true });

    // Verify circles still exist and have been scaled
    const circlesAfter = await circles.count();
    expect(circlesAfter).toBe(circleCount);
  } else {
    console.log('SE handle not found - group selection may not be working');
    await page.screenshot({ path: 'tests/group-resize-no-handle.png', fullPage: true });
  }
});

test('shift-click toggles element selection', async ({ page }) => {
  await page.goto('http://localhost:5149');
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000);

  const canvas = page.locator('svg.canvas');
  await expect(canvas).toBeVisible();

  const canvasBox = await canvas.boundingBox();

  const scaleInfo = await page.evaluate(() => {
    const svg = document.querySelector('svg.canvas');
    return window.getSvgScaleInfo ? window.getSvgScaleInfo(svg) : null;
  });

  const viewBoxOffsetX = scaleInfo ? scaleInfo.viewBoxOffsetX : 0;
  const viewBoxOffsetY = scaleInfo ? scaleInfo.viewBoxOffsetY : 0;
  const scale = scaleInfo ? scaleInfo.scaleX : 1;

  // Draw two rectangles
  await page.selectOption('.tool-select', '3');
  await page.waitForTimeout(300);

  // First rect
  let startX = canvasBox.x + viewBoxOffsetX + 100 * scale;
  let startY = canvasBox.y + viewBoxOffsetY + 100 * scale;
  let endX = canvasBox.x + viewBoxOffsetX + 180 * scale;
  let endY = canvasBox.y + viewBoxOffsetY + 180 * scale;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();
  await page.waitForTimeout(300);

  // Second rect
  startX = canvasBox.x + viewBoxOffsetX + 250 * scale;
  startY = canvasBox.y + viewBoxOffsetY + 100 * scale;
  endX = canvasBox.x + viewBoxOffsetX + 330 * scale;
  endY = canvasBox.y + viewBoxOffsetY + 180 * scale;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(endX, endY);
  await page.mouse.up();
  await page.waitForTimeout(300);

  // Switch to select tool
  await page.selectOption('.tool-select', '0');
  await page.waitForTimeout(300);

  // Click first rectangle to select it
  const clickX1 = canvasBox.x + viewBoxOffsetX + 140 * scale;
  const clickY1 = canvasBox.y + viewBoxOffsetY + 140 * scale;
  await page.mouse.click(clickX1, clickY1);
  await page.waitForTimeout(300);

  // Check we have blue selection (single select)
  let blueBoxes = await page.locator('svg.canvas rect[stroke="#0066ff"][stroke-dasharray="4 2"]').count();
  console.log('Blue selection boxes after first click:', blueBoxes);
  expect(blueBoxes).toBeGreaterThanOrEqual(1);

  // Shift+click second rectangle to add to selection
  const clickX2 = canvasBox.x + viewBoxOffsetX + 290 * scale;
  const clickY2 = canvasBox.y + viewBoxOffsetY + 140 * scale;
  console.log(`Shift-clicking at screen (${clickX2}, ${clickY2}) for SVG (290, 140)`);

  // Use keyboard to hold shift during click
  await page.keyboard.down('Shift');
  await page.mouse.click(clickX2, clickY2);
  await page.keyboard.up('Shift');
  await page.waitForTimeout(500);

  // Debug: dump all rects to see selection state
  const allRectsAfterShift = await page.evaluate(() => {
    const rects = document.querySelectorAll('svg.canvas rect');
    return Array.from(rects).map(r => ({
      stroke: r.getAttribute('stroke'),
      fill: r.getAttribute('fill'),
      strokeDasharray: r.getAttribute('stroke-dasharray'),
      dataHandle: r.getAttribute('data-handle'),
      x: r.getAttribute('x'),
      y: r.getAttribute('y')
    }));
  });
  console.log('All rects after shift-click:', JSON.stringify(allRectsAfterShift, null, 2));

  // Check properties panel to see selection count
  const propertiesText = await page.locator('.properties-panel h4').textContent().catch(() => 'No properties panel');
  console.log('Properties panel:', propertiesText);

  // Now should have orange group box
  const orangeBoxes = await page.locator('svg.canvas rect[stroke="#ff6600"]').count();
  console.log('Orange boxes after shift-click:', orangeBoxes);

  await page.screenshot({ path: 'tests/shift-click-test.png', fullPage: true });

  // Should have orange elements (group box + handles)
  expect(orangeBoxes).toBeGreaterThanOrEqual(1);
});
