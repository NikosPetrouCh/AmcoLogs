using System.Text.Json;
using System.Text.RegularExpressions;
using AmcoLogs.Core.Persistence;
using AmcoLogs.Core.ThingsBoard;
using Microsoft.Playwright;

namespace AmcoLogs.Infrastructure.ThingsBoard;

public sealed class PlaywrightThingsBoardLookup : IThingsBoardLookup
{
    private const string BaseUrl = "http://78.108.40.198:8080";
    private const string DeviceDashboardUrl = BaseUrl + "/dashboards/59b50050-db47-11ef-b881-29f5eaa9ebdb";
    private const string VehicleDashboardUrl = BaseUrl + "/dashboards/1de10db0-484f-11f0-a9dc-b357dadcba2d?state=W3siaWQiOiJkZWZhdWx0IiwicGFyYW1zIjp7fX0seyJpZCI6ImFsbF92ZWhpY2xlcyIsInBhcmFtcyI6e319XQ%253D%253D";

    private readonly IAmcoLogsStore _store;
    private readonly ThingsBoardSettings _settings;
    private readonly object _gate = new();
    private readonly List<string> _log = [];
    private readonly List<ThingsBoardLookupResult> _results = [];
    private bool _running;
    private string _phase = "Idle";

    public PlaywrightThingsBoardLookup(IAmcoLogsStore store, ThingsBoardSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    public ThingsBoardJobStatus GetStatus()
    {
        lock (_gate)
        {
            return new ThingsBoardJobStatus
            {
                Running = _running,
                Phase = _phase,
                Log = _log.ToArray(),
                Results = _results.ToArray()
            };
        }
    }

    public Task<bool> StartAsync(IReadOnlyList<ThingsBoardDeviceRequest> devices, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_running)
            {
                return Task.FromResult(false);
            }

            _running = true;
            _phase = "Starting";
            _log.Clear();
            _results.Clear();
        }

        _ = Task.Run(() => RunAsync(devices), CancellationToken.None);
        return Task.FromResult(true);
    }

    private async Task RunAsync(IReadOnlyList<ThingsBoardDeviceRequest> devices)
    {
        try
        {
            Log("Opening ThingsBoard and signing in.");
            SetPhase("Login");

            var profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AmcoLogs",
                "thingsboard-profile");
            Directory.CreateDirectory(profile);

            using var playwright = await Playwright.CreateAsync();
            await using var context = await LaunchAsync(playwright, profile);
            var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

            await page.GotoAsync(DeviceDashboardUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
            await WaitForLoginIfNeededAsync(page);

            Log("Skipping /home and the dashboards list. Going to the device dashboard.");
            if (!page.Url.Contains("59b50050-db47-11ef-b881-29f5eaa9ebdb", StringComparison.OrdinalIgnoreCase))
            {
                await page.GotoAsync(DeviceDashboardUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
            }

            var token = await ReadJwtAsync(page);

            foreach (var device in devices)
            {
                SetPhase($"Lookup {device.ServiceAccountId}");
                var result = await LookupOneAsync(page, token, device);
                lock (_gate)
                {
                    _results.Add(result);
                }

                if (!string.IsNullOrWhiteSpace(result.IpAddress))
                {
                    try
                    {
                        await _store.UpsertDeviceIpAsync(new DeviceIpMapping
                        {
                            ServiceAccountId = device.ServiceAccountId,
                            IpAddress = result.IpAddress,
                            Mobility = device.Mobility,
                            Source = "thingsboard",
                            UpdatedAt = DateTime.UtcNow,
                            BusNo = result.BusNo
                        });
                    }
                    catch (Exception exception)
                    {
                        Log($"IP found but not saved to MySQL: {exception.Message}");
                    }
                }
            }

            SetPhase("Done");
            Log("Finished ThingsBoard lookup.");
        }
        catch (Exception exception)
        {
            var message = exception.Message;
            if (message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Browser was not found", StringComparison.OrdinalIgnoreCase))
            {
                Log("Chromium/Chrome is missing for the ThingsBoard window. Install Playwright browsers and try again.");
            }

            Log("Lookup failed: " + message);
            SetPhase("Error");
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
            }
        }
    }

    private static async Task<IBrowserContext> LaunchAsync(IPlaywright playwright, string profile)
    {
        var options = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = false,
            ViewportSize = null,
            IgnoreHTTPSErrors = true,
            Args = ["--disable-blink-features=AutomationControlled"]
        };

        try
        {
            options.Channel = "chrome";
            return await playwright.Chromium.LaunchPersistentContextAsync(profile, options);
        }
        catch
        {
            options.Channel = null;
            return await playwright.Chromium.LaunchPersistentContextAsync(profile, options);
        }
    }

    private async Task WaitForLoginIfNeededAsync(IPage page)
    {
        if (!page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase))
        {
            await page.WaitForTimeoutAsync(800);
            return;
        }

        try
        {
            await page.WaitForSelectorAsync(
                "input[type='password'], input[formcontrolname='password'], input[name='password']",
                new PageWaitForSelectorOptions { Timeout = 20_000 });
        }
        catch
        {
            Log("Login form did not appear immediately.");
        }

        if (!string.IsNullOrWhiteSpace(_settings.Username) && !string.IsNullOrWhiteSpace(_settings.Password))
        {
            Log("Filling ThingsBoard username and password.");
            await FillLoginFormAsync(page);
        }
        else
        {
            Log("Waiting for you to sign in on the ThingsBoard window…");
        }

        var deadline = DateTime.UtcNow.AddMinutes(8);
        var lastPrompt = DateTime.MinValue;
        while (DateTime.UtcNow < deadline)
        {
            if (!page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                Log("Signed in. Skipping Home and the dashboards list.");
                await page.WaitForTimeoutAsync(1500);
                return;
            }

            if (DateTime.UtcNow - lastPrompt > TimeSpan.FromSeconds(8))
            {
                var pageText = "";
                try
                {
                    pageText = await page.InnerTextAsync("body");
                }
                catch
                {
                    // Page may be navigating.
                }

                if (pageText.Contains("Invalid username or password", StringComparison.OrdinalIgnoreCase)
                    || pageText.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("ThingsBoard rejected the username or password.");
                }

                Log("Still on the login page…");
                lastPrompt = DateTime.UtcNow;
            }

            await page.WaitForTimeoutAsync(1000);
        }

        throw new TimeoutException("Still on the login page. Check the ThingsBoard username and password.");
    }

    private async Task FillLoginFormAsync(IPage page)
    {
        var userBox = await FirstVisibleAsync(page,
            "input[formcontrolname='username']",
            "input[name='username']",
            "input#username-input",
            "input[type='email']",
            "input[placeholder*='mail' i]");
        var passBox = await FirstVisibleAsync(page,
            "input[formcontrolname='password']",
            "input[name='password']",
            "input#password-input",
            "input[type='password']");

        if (userBox is null || passBox is null)
        {
            Log("Login fields were not found yet. Retrying…");
            await page.WaitForTimeoutAsync(2000);
            userBox ??= await FirstVisibleAsync(page, "input[type='email']", "input[formcontrolname='username']", "input[name='username']");
            passBox ??= await FirstVisibleAsync(page, "input[type='password']", "input[formcontrolname='password']");
        }

        if (userBox is null || passBox is null)
        {
            Log("Could not find the ThingsBoard login form.");
            return;
        }

        await userBox.ClickAsync();
        await userBox.FillAsync(_settings.Username!);
        await passBox.ClickAsync();
        await passBox.FillAsync(_settings.Password!);

        var submit = await FirstVisibleAsync(page,
            "button[type='submit']",
            "button:has-text('Log in')",
            "button:has-text('Login')",
            "button:has-text('Sign in')");
        if (submit is not null)
        {
            await submit.ClickAsync();
        }
        else
        {
            await passBox.PressAsync("Enter");
        }
    }

    private static async Task<ILocator?> FirstVisibleAsync(IPage page, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var locator = page.Locator(selector).First;
            try
            {
                if (await locator.CountAsync() == 0 || !await locator.IsVisibleAsync())
                {
                    continue;
                }

                return locator;
            }
            catch
            {
                // Selector not applicable on this screen.
            }
        }

        return null;
    }

    private async Task<ThingsBoardLookupResult> LookupOneAsync(IPage page, string? token, ThingsBoardDeviceRequest device)
    {
        Log($"{device.ServiceAccountId}: searching device dashboard for BUS no.");
        string? busNo = null;
        string? ip = null;

        if (!string.IsNullOrWhiteSpace(token))
        {
            (busNo, ip) = await TryApiAsync(token, device.ServiceAccountId);
            if (!string.IsNullOrWhiteSpace(ip))
            {
                Log($"{device.ServiceAccountId}: IP from ThingsBoard API {ip}.");
                return Ok(device, busNo, ip, "Found via API after laptop login.");
            }
        }

        busNo ??= await SearchDashboardAsync(page, DeviceDashboardUrl, device.ServiceAccountId, FindBus);
        if (string.IsNullOrWhiteSpace(busNo))
        {
            Log($"{device.ServiceAccountId}: BUS no not found.");
            return new ThingsBoardLookupResult
            {
                ServiceAccountId = device.ServiceAccountId,
                Status = "not-found",
                Message = "BUS no not found on the device dashboard."
            };
        }

        Log($"{device.ServiceAccountId}: BUS no {busNo}. Searching vehicles dashboard for WAN IP.");
        ip = await SearchDashboardAsync(page, VehicleDashboardUrl, busNo, FindIp);
        if (string.IsNullOrWhiteSpace(ip))
        {
            return new ThingsBoardLookupResult
            {
                ServiceAccountId = device.ServiceAccountId,
                BusNo = busNo,
                Status = "not-found",
                Message = "WAN IP not found on the vehicles dashboard."
            };
        }

        Log($"{device.ServiceAccountId}: WAN IP {ip}.");
        return Ok(device, busNo, ip, "Found via the two dashboards.");
    }

    private async Task<(string? BusNo, string? Ip)> TryApiAsync(string token, string serviceAccountId)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Authorization", "Bearer " + token);
            var search = await client.GetAsync($"/api/tenant/devices?pageSize=20&page=0&textSearch={Uri.EscapeDataString(serviceAccountId)}");
            if (!search.IsSuccessStatusCode)
            {
                return (null, null);
            }

            using var doc = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            {
                return (null, null);
            }

            var device = data[0];
            var id = device.GetProperty("id").GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return (null, null);
            }

            var attributes = await client.GetAsync($"/api/plugins/telemetry/DEVICE/{id}/values/attributes");
            if (!attributes.IsSuccessStatusCode)
            {
                return (null, null);
            }

            using var attrDoc = JsonDocument.Parse(await attributes.Content.ReadAsStringAsync());
            string? bus = null;
            string? ip = null;
            foreach (var item in attrDoc.RootElement.EnumerateArray())
            {
                var key = item.GetProperty("key").GetString() ?? "";
                var value = item.TryGetProperty("value", out var raw) ? raw.ToString() : "";
                if (LooksLikeBus(key) && string.IsNullOrWhiteSpace(bus))
                {
                    bus = value;
                }

                if (LooksLikeIp(key, value) && string.IsNullOrWhiteSpace(ip))
                {
                    ip = value;
                }
            }

            return (bus, ip);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<string?> SearchDashboardAsync(IPage page, string url, string query, Func<string, string?> extract)
    {
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(2500);
        await FillSearchAsync(page, query);
        await page.WaitForTimeoutAsync(2500);
        var text = await page.InnerTextAsync("body");
        return extract(text);
    }

    private static async Task FillSearchAsync(IPage page, string query)
    {
        var selectors = new[]
        {
            "input[placeholder*='earch' i]",
            "input[type='search']",
            "input[aria-label*='earch' i]",
            ".tb-search-input input",
            "input.mat-mdc-input-element",
            "input.mat-input-element"
        };

        foreach (var selector in selectors)
        {
            var input = page.Locator(selector).First;
            if (await input.CountAsync() == 0 || !await input.IsVisibleAsync())
            {
                continue;
            }

            await input.ClickAsync();
            await input.FillAsync("");
            await input.FillAsync(query);
            await input.PressAsync("Enter");
            return;
        }

        throw new InvalidOperationException("No search box found on the dashboard. Leave the ThingsBoard window open and try again.");
    }

    private static async Task<string?> ReadJwtAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<string?>(
                """
                () => localStorage.getItem('jwt_token')
                    || localStorage.getItem('jwtToken')
                    || localStorage.getItem('access_token')
                """);
        }
        catch
        {
            return null;
        }
    }

    private static ThingsBoardLookupResult Ok(ThingsBoardDeviceRequest device, string? bus, string ip, string message) =>
        new()
        {
            ServiceAccountId = device.ServiceAccountId,
            BusNo = bus,
            IpAddress = ip,
            Status = "ok",
            Message = message
        };

    private static string? FindBus(string pageText)
    {
        var match = Regex.Match(pageText, @"(?i)(?:bus\s*(?:no|number|#)?|λεωφορε[ίι]ο)\s*[:#]?\s*([A-Za-z0-9\-]{2,20})");
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        match = Regex.Match(pageText, @"\b([0-9]{3,6})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? FindIp(string pageText)
    {
        var match = Regex.Match(pageText, @"\b(?:WAN\s*IP|IP)\s*[:#]?\s*((?:\d{1,3}\.){3}\d{1,3})\b", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        match = Regex.Match(pageText, @"\b((?:\d{1,3}\.){3}\d{1,3})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool LooksLikeBus(string key) =>
        key.Contains("bus", StringComparison.OrdinalIgnoreCase)
        || key.Contains("vehicle", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeIp(string key, string value) =>
        Regex.IsMatch(value, @"^(?:\d{1,3}\.){3}\d{1,3}$")
        && (key.Contains("ip", StringComparison.OrdinalIgnoreCase) || key.Contains("wan", StringComparison.OrdinalIgnoreCase));

    private void Log(string message)
    {
        lock (_gate)
        {
            _log.Add($"{DateTime.Now:HH:mm:ss} {message}");
            if (_log.Count > 80)
            {
                _log.RemoveAt(0);
            }
        }
    }

    private void SetPhase(string phase)
    {
        lock (_gate)
        {
            _phase = phase;
        }
    }
}
