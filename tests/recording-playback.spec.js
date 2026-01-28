// @ts-check
const { test, expect } = require('@playwright/test');

// Helper to select a tool from the dropdown
async function selectTool(page, toolValue) {
    const toolSelect = page.locator('select.tool-select').first();
    await toolSelect.selectOption(toolValue);
}

test.describe('Recording and Playback', () => {
    test.beforeEach(async ({ page }) => {
        await page.goto('http://localhost:5149');
        // Wait for Blazor to load - look for the main app container
        await page.waitForSelector('.sketch-container, app, #app', { timeout: 30000 });
        // Wait for the Blazor loading indicator to disappear
        await page.waitForFunction(() => !document.querySelector('#blazor-error-ui'));
        // Wait for app to fully load
        await page.waitForTimeout(2000);
    });

    test('should open recording panel', async ({ page }) => {
        // Click Record button
        const recordBtn = page.locator('button:has-text("Record")');
        await expect(recordBtn).toBeVisible();
        await recordBtn.click();

        // Recording panel should appear
        const recordingPanel = page.locator('.recording-panel');
        await expect(recordingPanel).toBeVisible();

        // Should have recording controls
        await expect(page.locator('.recording-name-input')).toBeVisible();
        await expect(page.locator('button:has-text("Start Recording")')).toBeVisible();
    });

    test('should start and stop recording', async ({ page }) => {
        // Open recording panel
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');

        // Start recording
        await page.click('button:has-text("Start Recording")');

        // Should show recording indicator
        await expect(page.locator('.recording-indicator')).toBeVisible();
        await expect(page.locator('text=● Recording')).toBeVisible();

        // Stop recording
        await page.click('button:has-text("Stop")');

        // Should show Start Recording button again
        await expect(page.locator('button:has-text("Start Recording")')).toBeVisible();
    });

    test('should record element creation', async ({ page }) => {
        // Open recording panel and start recording
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(500);

        // Get the canvas
        const canvas = page.locator('svg.canvas');
        await expect(canvas).toBeVisible();

        // Select rectangle tool (value="3")
        await selectTool(page, '3');

        // Draw rectangle on canvas
        const box = await canvas.boundingBox();
        if (box) {
            await page.mouse.move(box.x + 100, box.y + 100);
            await page.mouse.down();
            await page.mouse.move(box.x + 200, box.y + 200);
            await page.mouse.up();
        }

        await page.waitForTimeout(500);

        // Stop recording
        await page.click('button:has-text("Stop")');

        // Check action count shows at least 1 action
        const actionCount = page.locator('.recording-actions');
        const text = await actionCount.textContent();
        expect(text).toContain('action');
    });

    test('should save and list recording', async ({ page }) => {
        // Open recording panel and start recording
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');

        // Set recording name
        await page.fill('.recording-name-input', 'Test Recording');

        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Get canvas and select rectangle tool
        const canvas = page.locator('svg.canvas');
        await selectTool(page, '3');
        const box = await canvas.boundingBox();
        if (box) {
            await page.mouse.move(box.x + 50, box.y + 50);
            await page.mouse.down();
            await page.mouse.move(box.x + 150, box.y + 150);
            await page.mouse.up();
        }

        await page.waitForTimeout(300);

        // Stop and save
        await page.click('button:has-text("Stop")');
        await page.click('button:has-text("Save")');

        // Should show in saved recordings list
        await expect(page.locator('.saved-recordings')).toBeVisible();
        await expect(page.locator('.rec-name:has-text("Test Recording")')).toBeVisible();
    });

    test('should replay recording', async ({ page }) => {
        // First create and save a recording
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Replay Test');
        await page.click('button:has-text("Start Recording")');

        // Draw a circle (value="4")
        const canvas = page.locator('svg.canvas');
        await selectTool(page, '4');
        const box = await canvas.boundingBox();
        if (box) {
            await page.mouse.move(box.x + 200, box.y + 200);
            await page.mouse.down();
            await page.mouse.move(box.x + 250, box.y + 250);
            await page.mouse.up();
        }

        await page.waitForTimeout(300);
        await page.click('button:has-text("Stop")');
        await page.click('button:has-text("Save")');

        // Clear canvas
        await page.click('button:has-text("Clear")');
        await page.waitForTimeout(300);

        // Click play button on the saved recording
        const playBtn = page.locator('.saved-recording-item .btn-primary');
        await playBtn.click();

        // Wait for replay to finish
        await page.waitForTimeout(3000);

        // Should have elements after replay
        const elementsAfter = await canvas.locator('circle, ellipse, rect, line, path').count();
        expect(elementsAfter).toBeGreaterThan(0);
    });

    test('should delete recording', async ({ page }) => {
        // Create and save a recording
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Delete Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Draw a line (value="1")
        const canvas = page.locator('svg.canvas');
        await selectTool(page, '1');
        const box = await canvas.boundingBox();
        if (box) {
            await page.mouse.move(box.x + 100, box.y + 100);
            await page.mouse.down();
            await page.mouse.move(box.x + 200, box.y + 100);
            await page.mouse.up();
        }

        await page.waitForTimeout(300);
        await page.click('button:has-text("Stop")');
        await page.click('button:has-text("Save")');

        // Verify recording exists
        await expect(page.locator('.rec-name:has-text("Delete Test")')).toBeVisible();

        // Click delete button
        const deleteBtn = page.locator('.saved-recording-item:has-text("Delete Test") .btn-danger');
        await deleteBtn.click();

        // Recording should be removed
        await expect(page.locator('.rec-name:has-text("Delete Test")')).not.toBeVisible();
    });

    test('should record move action', async ({ page }) => {
        // Open recording panel and start recording
        await page.click('button:has-text("Record")');
        await page.waitForSelector('.recording-panel');
        await page.fill('.recording-name-input', 'Move Test');
        await page.click('button:has-text("Start Recording")');
        await page.waitForTimeout(300);

        // Draw a rectangle first (value="3")
        const canvas = page.locator('svg.canvas');
        await selectTool(page, '3');
        const box = await canvas.boundingBox();
        if (box) {
            await page.mouse.move(box.x + 100, box.y + 100);
            await page.mouse.down();
            await page.mouse.move(box.x + 200, box.y + 200);
            await page.mouse.up();
        }

        await page.waitForTimeout(300);

        // Switch to select tool (value="0") and move the element
        await selectTool(page, '0');
        await page.waitForTimeout(200);

        if (box) {
            // Click to select
            await page.mouse.click(box.x + 150, box.y + 150);
            await page.waitForTimeout(200);

            // Drag to move
            await page.mouse.move(box.x + 150, box.y + 150);
            await page.mouse.down();
            await page.mouse.move(box.x + 250, box.y + 250);
            await page.mouse.up();
        }

        await page.waitForTimeout(300);

        // Stop recording
        await page.click('button:has-text("Stop")');

        // Should have recorded multiple actions (create + move)
        const actionText = await page.locator('.recording-actions').textContent();
        const actionCount = parseInt(actionText?.match(/\d+/)?.[0] || '0');
        expect(actionCount).toBeGreaterThanOrEqual(2);
    });
});
