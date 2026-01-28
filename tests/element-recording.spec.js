// @ts-check
const { test, expect } = require('@playwright/test');

// Helper to select a tool from the dropdown
async function selectTool(page, toolValue) {
    const toolSelect = page.locator('select.tool-select').first();
    await toolSelect.selectOption(toolValue);
}

// Helper to draw an element based on tool type
async function drawElement(page, canvas, box, index, toolValue) {
    const offsetX = 50 + (index * 80);
    const offsetY = 50 + (index * 60);

    if (toolValue === '2') {
        // Pencil - draw a squiggle
        await page.mouse.move(box.x + offsetX, box.y + offsetY);
        await page.mouse.down();
        await page.mouse.move(box.x + offsetX + 30, box.y + offsetY + 20);
        await page.mouse.move(box.x + offsetX + 50, box.y + offsetY - 10);
        await page.mouse.move(box.x + offsetX + 70, box.y + offsetY + 15);
        await page.mouse.up();
    } else if (toolValue === '10') {
        // Text - just click to place
        await page.mouse.click(box.x + offsetX, box.y + offsetY);
        await page.waitForTimeout(100);
        // Type some text
        await page.keyboard.type('Test' + index);
        await page.keyboard.press('Escape');
    } else {
        // All other tools - drag to create
        await page.mouse.move(box.x + offsetX, box.y + offsetY);
        await page.mouse.down();
        await page.mouse.move(box.x + offsetX + 60, box.y + offsetY + 50);
        await page.mouse.up();
    }
    await page.waitForTimeout(150);
}

// Helper to count SVG elements
async function countElements(canvas) {
    return await canvas.locator('circle, ellipse, rect, line, path, polygon, text, image, g').count();
}

test.describe('Element Recording and Playback', () => {
    test.setTimeout(60000); // Increase timeout for recording tests

    test.beforeEach(async ({ page }) => {
        await page.goto('http://localhost:5149');
        // Wait for the canvas to be visible - this indicates the app is loaded
        await page.waitForSelector('svg.canvas', { timeout: 30000 });
        await page.waitForTimeout(1000);
    });

    test('should record and replay 5 rectangles', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        // Open recording panel and start recording
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Rectangle Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Select rectangle tool (value="3")
        await selectTool(page, '3');

        // Draw 5 rectangles
        for (let i = 0; i < 5; i++) {
            await drawElement(page, canvas, box, i, '3');
        }

        // Stop and save recording
        await page.click('button:has-text("Stop")');
        await page.waitForTimeout(200);

        // Check action count
        const actionText = await page.locator('.recording-actions').textContent();
        console.log('Rectangle recording actions:', actionText);

        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        // Count elements before clear
        const elementsBefore = await countElements(canvas);
        console.log('Elements before clear:', elementsBefore);
        expect(elementsBefore).toBeGreaterThanOrEqual(5);

        // Clear canvas
        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        // Verify canvas is cleared
        const elementsAfterClear = await countElements(canvas);
        console.log('Elements after clear:', elementsAfterClear);

        // Click play button on the saved recording
        const playBtn = page.locator('.saved-recording-item:has-text("Rectangle Test") .btn-primary');
        await playBtn.click();

        // Wait for replay to finish
        await page.waitForTimeout(5000);

        // Should have elements after replay
        const elementsAfterReplay = await countElements(canvas);
        console.log('Elements after replay:', elementsAfterReplay);
        expect(elementsAfterReplay).toBeGreaterThanOrEqual(5);
    });

    test('should record and replay 5 pencil drawings (paths)', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        // Open recording panel and start recording
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Pencil Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Select pencil tool (value="2")
        await selectTool(page, '2');

        // Draw 5 pencil strokes
        for (let i = 0; i < 5; i++) {
            await drawElement(page, canvas, box, i, '2');
        }

        // Stop and save recording
        await page.click('button:has-text("Stop")');
        await page.waitForTimeout(200);

        const actionText = await page.locator('.recording-actions').textContent();
        console.log('Pencil recording actions:', actionText);

        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        // Count elements before clear
        const elementsBefore = await canvas.locator('path').count();
        console.log('Paths before clear:', elementsBefore);
        expect(elementsBefore).toBeGreaterThanOrEqual(5);

        // Clear canvas
        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        // Click play button
        const playBtn = page.locator('.saved-recording-item:has-text("Pencil Test") .btn-primary');
        await playBtn.click();

        // Wait for replay
        await page.waitForTimeout(5000);

        // Should have paths after replay
        const pathsAfterReplay = await canvas.locator('path').count();
        console.log('Paths after replay:', pathsAfterReplay);
        expect(pathsAfterReplay).toBeGreaterThanOrEqual(5);
    });

    test('should record and replay 5 lines', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Line Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Select line tool (value="1")
        await selectTool(page, '1');

        for (let i = 0; i < 5; i++) {
            await drawElement(page, canvas, box, i, '1');
        }

        await page.click('button:has-text("Stop")');
        await page.waitForTimeout(200);
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        const linesBefore = await canvas.locator('line').count();
        console.log('Lines before clear:', linesBefore);
        expect(linesBefore).toBeGreaterThanOrEqual(5);

        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        const playBtn = page.locator('.saved-recording-item:has-text("Line Test") .btn-primary');
        await playBtn.click();
        await page.waitForTimeout(5000);

        const linesAfterReplay = await canvas.locator('line').count();
        console.log('Lines after replay:', linesAfterReplay);
        expect(linesAfterReplay).toBeGreaterThanOrEqual(5);
    });

    test('should record and replay 5 circles', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Circle Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Select circle tool (value="4")
        await selectTool(page, '4');

        for (let i = 0; i < 5; i++) {
            await drawElement(page, canvas, box, i, '4');
        }

        await page.click('button:has-text("Stop")');
        await page.waitForTimeout(200);
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        const circlesBefore = await canvas.locator('circle').count();
        console.log('Circles before clear:', circlesBefore);
        expect(circlesBefore).toBeGreaterThanOrEqual(5);

        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        const playBtn = page.locator('.saved-recording-item:has-text("Circle Test") .btn-primary');
        await playBtn.click();
        await page.waitForTimeout(5000);

        const circlesAfterReplay = await canvas.locator('circle').count();
        console.log('Circles after replay:', circlesAfterReplay);
        expect(circlesAfterReplay).toBeGreaterThanOrEqual(5);
    });

    test('should record and replay 5 ellipses', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Ellipse Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Select ellipse tool (value="5")
        await selectTool(page, '5');

        for (let i = 0; i < 5; i++) {
            await drawElement(page, canvas, box, i, '5');
        }

        await page.click('button:has-text("Stop")');
        await page.waitForTimeout(200);
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        const ellipsesBefore = await canvas.locator('ellipse').count();
        console.log('Ellipses before clear:', ellipsesBefore);
        expect(ellipsesBefore).toBeGreaterThanOrEqual(5);

        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        const playBtn = page.locator('.saved-recording-item:has-text("Ellipse Test") .btn-primary');
        await playBtn.click();
        await page.waitForTimeout(5000);

        const ellipsesAfterReplay = await canvas.locator('ellipse').count();
        console.log('Ellipses after replay:', ellipsesAfterReplay);
        expect(ellipsesAfterReplay).toBeGreaterThanOrEqual(5);
    });

    test('should record and replay 5 triangles', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Triangle Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Select triangle tool (value="6")
        await selectTool(page, '6');

        for (let i = 0; i < 5; i++) {
            await drawElement(page, canvas, box, i, '6');
        }

        await page.click('button:has-text("Stop")');
        await page.waitForTimeout(200);
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        const polygonsBefore = await canvas.locator('polygon').count();
        console.log('Triangles (polygons) before clear:', polygonsBefore);
        expect(polygonsBefore).toBeGreaterThanOrEqual(5);

        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        const playBtn = page.locator('.saved-recording-item:has-text("Triangle Test") .btn-primary');
        await playBtn.click();
        await page.waitForTimeout(5000);

        const polygonsAfterReplay = await canvas.locator('polygon').count();
        console.log('Triangles (polygons) after replay:', polygonsAfterReplay);
        expect(polygonsAfterReplay).toBeGreaterThanOrEqual(5);
    });

    test('should record and replay 5 polygons', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Polygon Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Select polygon tool (value="7")
        await selectTool(page, '7');

        for (let i = 0; i < 5; i++) {
            await drawElement(page, canvas, box, i, '7');
        }

        await page.click('button:has-text("Stop")');
        await page.waitForTimeout(200);
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        const polygonsBefore = await canvas.locator('polygon').count();
        console.log('Polygons before clear:', polygonsBefore);
        expect(polygonsBefore).toBeGreaterThanOrEqual(5);

        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        const playBtn = page.locator('.saved-recording-item:has-text("Polygon Test") .btn-primary');
        await playBtn.click();
        await page.waitForTimeout(5000);

        const polygonsAfterReplay = await canvas.locator('polygon').count();
        console.log('Polygons after replay:', polygonsAfterReplay);
        expect(polygonsAfterReplay).toBeGreaterThanOrEqual(5);
    });

    test('should record and replay 5 stars', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Star Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Select star tool (value="8")
        await selectTool(page, '8');

        for (let i = 0; i < 5; i++) {
            await drawElement(page, canvas, box, i, '8');
        }

        await page.click('button:has-text("Stop")');
        await page.waitForTimeout(200);
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        const starsBefore = await canvas.locator('polygon').count();
        console.log('Stars (polygons) before clear:', starsBefore);
        expect(starsBefore).toBeGreaterThanOrEqual(5);

        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        const playBtn = page.locator('.saved-recording-item:has-text("Star Test") .btn-primary');
        await playBtn.click();
        await page.waitForTimeout(5000);

        const starsAfterReplay = await canvas.locator('polygon').count();
        console.log('Stars (polygons) after replay:', starsAfterReplay);
        expect(starsAfterReplay).toBeGreaterThanOrEqual(5);
    });

    test('should have replay dropdown and export buttons', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        // Open recording panel
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');

        // Should have Replay section with "No saved recordings" initially
        await expect(page.locator('.replay-section h5:has-text("Replay")')).toBeVisible();
        await expect(page.locator('.replay-section .no-recordings')).toBeVisible();

        // Should have Import JSON button
        await expect(page.locator('text=Import JSON')).toBeVisible();

        // Create and save a recording
        await page.fill('.recording-name-input', 'Export Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(200);

        // Draw a rectangle
        await selectTool(page, '3');
        await page.mouse.move(box.x + 100, box.y + 100);
        await page.mouse.down();
        await page.mouse.move(box.x + 180, box.y + 180);
        await page.mouse.up();
        await page.waitForTimeout(200);

        await page.click('button:has-text("Stop")');
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        // Should have the recording in the list with export button
        const recordingItem = page.locator('.saved-recording-item:has-text("Export Test")');
        await expect(recordingItem).toBeVisible();

        // Should have export button (↓)
        const exportBtn = recordingItem.locator('button[title="Export JSON"]');
        await expect(exportBtn).toBeVisible();

        // Should now appear in replay dropdown (dropdown visible after saving a recording)
        const replaySelect = page.locator('.replay-select');
        await expect(replaySelect).toBeVisible();
        // Check that the option exists in the dropdown (options are hidden until dropdown opens)
        const optionCount = await replaySelect.locator('option:has-text("Export Test")').count();
        expect(optionCount).toBe(1);
    });

    test('should open recording editor', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        // Open recording panel
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');

        // Create and save a recording
        await page.fill('.recording-name-input', 'Editor Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(200);

        // Draw a few shapes
        await selectTool(page, '3');
        for (let i = 0; i < 3; i++) {
            await page.mouse.move(box.x + 50 + i * 100, box.y + 50);
            await page.mouse.down();
            await page.mouse.move(box.x + 100 + i * 100, box.y + 100);
            await page.mouse.up();
            await page.waitForTimeout(150);
        }

        await page.click('button:has-text("Stop")');
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        // Click edit button
        const editBtn = page.locator('.saved-recording-item:has-text("Editor Test") button[title="Edit"]');
        await expect(editBtn).toBeVisible();
        await editBtn.click();

        // Editor should open
        await expect(page.locator('.recording-editor')).toBeVisible();
        await expect(page.locator('.editor-header:has-text("Edit Recording")')).toBeVisible();

        // Should show 3 actions
        const actionRows = page.locator('.action-row');
        expect(await actionRows.count()).toBe(3);

        // Should have toolbar buttons
        await expect(page.locator('button:has-text("Normalize Gaps")')).toBeVisible();
        await expect(page.locator('button:has-text("0.5×")')).toBeVisible();
        await expect(page.locator('button:has-text("2×")')).toBeVisible();
        await expect(page.locator('button:has-text("Test")')).toBeVisible();

        // Close editor
        await page.click('.editor-footer button:has-text("Cancel")');
        await expect(page.locator('.recording-editor')).not.toBeVisible();
    });

    test('should delete action in editor', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        // Create and save a recording with 3 shapes
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Delete Action Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(200);

        await selectTool(page, '4'); // Circle
        for (let i = 0; i < 3; i++) {
            await page.mouse.move(box.x + 100 + i * 120, box.y + 100);
            await page.mouse.down();
            await page.mouse.move(box.x + 150 + i * 120, box.y + 150);
            await page.mouse.up();
            await page.waitForTimeout(150);
        }

        await page.click('button:has-text("Stop")');
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        // Open editor
        await page.click('.saved-recording-item:has-text("Delete Action Test") button[title="Edit"]');
        await expect(page.locator('.recording-editor')).toBeVisible();

        // Should have 3 actions
        expect(await page.locator('.action-row').count()).toBe(3);

        // Delete the second action
        await page.locator('.action-row').nth(1).locator('button[title="Delete"]').click();

        // Should now have 2 actions
        expect(await page.locator('.action-row').count()).toBe(2);

        // Save changes
        await page.click('button:has-text("Save Changes")');
        await expect(page.locator('.recording-editor')).not.toBeVisible();

        // Verify the recording was updated (should show 2 actions)
        await expect(page.locator('.saved-recording-item:has-text("Delete Action Test") .rec-info:has-text("2 actions")')).toBeVisible();
    });

    test('should record and replay 5 arrows', async ({ page }) => {
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        if (!box) throw new Error('Canvas not found');

        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Arrow Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Select arrow tool (value="9")
        await selectTool(page, '9');

        for (let i = 0; i < 5; i++) {
            await drawElement(page, canvas, box, i, '9');
        }

        await page.click('button:has-text("Stop")');
        await page.waitForTimeout(200);
        await page.click('button:has-text("Save")');
        await page.waitForTimeout(300);

        // Arrows are rendered as <g> containing line and polygon
        const arrowsBefore = await canvas.locator('g').count();
        console.log('Arrows (groups) before clear:', arrowsBefore);
        expect(arrowsBefore).toBeGreaterThanOrEqual(5);

        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        const playBtn = page.locator('.saved-recording-item:has-text("Arrow Test") .btn-primary');
        await playBtn.click();
        await page.waitForTimeout(5000);

        const arrowsAfterReplay = await canvas.locator('g').count();
        console.log('Arrows (groups) after replay:', arrowsAfterReplay);
        expect(arrowsAfterReplay).toBeGreaterThanOrEqual(5);
    });
});
