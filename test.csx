using Microsoft.Playwright;

var playwright = await Playwright.CreateAsync();
var browser = await playwright.Chromium.LaunchAsync();
var page = await browser.NewPageAsync();

await page.GotoAsync("http://localhost:5199");
await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

var title = await page.TitleAsync();
Console.WriteLine($"Title: {title}");

var content = await page.ContentAsync();
Console.WriteLine(content.Contains("VecSketch") ? "SUCCESS: Page loaded correctly" : "FAIL: Page did not load");

await browser.CloseAsync();
