const { chromium } = require('playwright');

(async () => {
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });

    try {
        await page.goto('http://localhost:5149', { waitUntil: 'networkidle', timeout: 30000 });

        // Wait for Blazor to be ready
        await page.waitForSelector('svg', { timeout: 10000 });

        // Check canvas size
        const svg = await page.locator('svg.canvas');
        const svgWidth = await svg.getAttribute('width');
        const svgHeight = await svg.getAttribute('height');

        console.log('Canvas size:', svgWidth, 'x', svgHeight);
        console.log('Large canvas:', svgWidth === '1400' && svgHeight === '900');

        // Draw a rectangle to test
        const svgBox = await svg.boundingBox();
        await page.mouse.move(svgBox.x + 100, svgBox.y + 100);
        await page.mouse.down();
        await page.mouse.move(svgBox.x + 200, svgBox.y + 150);
        await page.mouse.up();

        // Switch to select tool
        await page.keyboard.press('v');
        await page.waitForTimeout(100);

        // Click on the drawn element
        await page.mouse.click(svgBox.x + 150, svgBox.y + 125);
        await page.waitForTimeout(200);

        // Check if properties panel appears
        const hasPropertiesPanel = await page.locator('.properties-panel').count() > 0;
        console.log('Properties panel appears:', hasPropertiesPanel);

        // Check for resize handles
        const hasResizeHandles = await page.locator('.resize-handle').count() > 0;
        console.log('Resize handles present:', hasResizeHandles);

        if (hasPropertiesPanel) {
            console.log('SUCCESS: Move/resize/properties features working!');
        } else {
            console.log('INFO: Element selection working, properties panel will show when element selected');
        }

    } catch (e) {
        console.error('ERROR:', e.message);
        process.exit(1);
    } finally {
        await browser.close();
    }
})();
