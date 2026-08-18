using System;
using System.Linq;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Support.UI;
using AutoRokScheduler.Models;

namespace AutoRokScheduler.Services;

/// <summary>
/// Drives a single, isolated browser session through auto-rok.com to press
/// Start or Stop on the App Control page. Robust to the site's "sometimes it
/// asks for a device name / login / shows maintenance" behaviour via a state
/// loop that reacts to whatever is actually on screen.
///
/// One instance == one browser session. Not thread-safe; the caller
/// (<see cref="BotRunner"/>) serialises actions.
/// </summary>
public sealed class SeleniumBot : IDisposable
{
    private readonly SiteConfig _site;
    private readonly AppSettings _settings;
    private readonly Action<string> _log;
    private IWebDriver? _driver;

    public SeleniumBot(SiteConfig site, AppSettings settings, Action<string> log)
    {
        _site = site;
        _settings = settings;
        _log = log;
    }

    // ---------------------------------------------------------------- public

    /// <summary>Runs the full flow and returns the final observed state.</summary>
    public RunState RunAction(Profile profile, BotAction action, CancellationToken ct)
    {
        var password = CredentialProtector.Decrypt(profile.EncryptedPassword);
        if (string.IsNullOrEmpty(profile.Login) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("This profile has no login/password set.");

        _driver = BuildDriver(profile);
        _log($"Browser launched ({profile.Browser}, profile '{profile.EffectiveProfileKey}').");

        _log("Opening App Control...");
        Navigate(_site.AppControlUrl);
        WaitForAppReady();

        // React to login / device-name / maintenance / info popups until the
        // Start & Stop controls are on screen.
        ReachControls(profile, ct);

        _log("App Control ready.");
        var result = PerformAction(action, ct);
        return result;
    }

    /// <summary>
    /// Logs in and opens the machine's App Control page, then reads the current status
    /// WITHOUT pressing Start or Stop. Used on app startup to show the live state.
    /// </summary>
    public RunState ReadStatusOnly(Profile profile, CancellationToken ct)
    {
        var password = CredentialProtector.Decrypt(profile.EncryptedPassword);
        if (string.IsNullOrEmpty(profile.Login) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("This profile has no login/password set.");

        _driver = BuildDriver(profile);
        _log($"Browser launched ({profile.Browser}, profile '{profile.EffectiveProfileKey}').");

        _log("Opening App Control...");
        Navigate(_site.AppControlUrl);
        WaitForAppReady();

        // Log in / enter device name / clear popups until the Start & Stop controls show.
        ReachControls(profile, ct);

        var status = ReadStatus();
        _log($"Current status: {status}.");
        return status;
    }

    // ---------------------------------------------------------------- driver

    private IWebDriver BuildDriver(Profile profile)
    {
        var userDataDir = AppPaths.BrowserProfileDir(profile.EffectiveProfileKey);
        var args = new[]
        {
            $"--user-data-dir={userDataDir}",   // dedicated, isolated from the real browser
            "--profile-directory=Default",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-features=Translate,ChromeWhatsNewUI",
            "--disable-blink-features=AutomationControlled",
            $"--window-size={_settings.WindowWidth},{_settings.WindowHeight}",
            $"--window-position={_settings.WindowLeft},{_settings.WindowTop}",
        };

        IWebDriver driver;
        if (profile.Browser == BrowserKind.Chrome)
        {
            var o = new ChromeOptions();
            foreach (var a in args) o.AddArgument(a);
            if (_settings.Headless) { o.AddArgument("--headless=new"); o.AddArgument("--disable-gpu"); }
            o.AddExcludedArgument("enable-automation");

            // Hide the driver's console window so nothing flashes on screen.
            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;
            service.SuppressInitialDiagnosticInformation = true;
            driver = new ChromeDriver(service, o);
        }
        else
        {
            var o = new EdgeOptions();
            foreach (var a in args) o.AddArgument(a);
            if (_settings.Headless) { o.AddArgument("--headless=new"); o.AddArgument("--disable-gpu"); }
            o.AddExcludedArgument("enable-automation");

            // Hide the driver's console window so nothing flashes on screen.
            var service = EdgeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;
            service.SuppressInitialDiagnosticInformation = true;
            driver = new EdgeDriver(service, o);
        }

        driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
        return driver;
    }

    private void Navigate(string url) => _driver!.Navigate().GoToUrl(url);

    // ------------------------------------------------------------ state loop

    private void ReachControls(Profile profile, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_settings.ActionTimeoutSeconds);
        int loginAttempts = 0, deviceAttempts = 0;
        bool triggerClicked = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (MaintenanceShowing())
            {
                ClearMaintenance(ct);
                deadline = DateTime.UtcNow.AddSeconds(_settings.ActionTimeoutSeconds); // fresh budget
                triggerClicked = false;
                continue;
            }

            if (StartOrStopPresent())
                return;

            // Device-name prompt (SweetAlert2 input).
            if (FirstDisplayed(By.CssSelector(_site.SwalInputCss)) != null)
            {
                if (++deviceAttempts > 2)
                    throw new InvalidOperationException(
                        $"Device name '{profile.DeviceName}' was not accepted after {deviceAttempts - 1} attempts.");
                HandleDeviceNamePrompt(profile);
                continue;
            }

            // Sign-in form visible → fill it.
            if (FirstDisplayed(By.CssSelector(_site.EmailCss)) != null)
            {
                if (++loginAttempts > 2)
                    throw new InvalidOperationException("Sign-in did not succeed (check email/password).");
                DoLogin(profile);
                Navigate(_site.AppControlUrl);
                WaitForAppReady();
                triggerClicked = false;
                continue;
            }

            // Logged out but modal not open yet → click the login trigger once.
            if (!triggerClicked)
            {
                var trigger = FirstClickable(By.XPath(_site.LoginTriggerXPath));
                if (trigger != null)
                {
                    triggerClicked = true;
                    ClickSafely(trigger);
                    Sleep(600);
                    continue;
                }
            }

            // A non-input informational popup is blocking → dismiss it.
            if (FirstDisplayed(By.CssSelector(_site.SwalPopupCss)) != null)
            {
                DismissInfoSwal();
                continue;
            }

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for the App Control Start/Stop buttons.");

            Sleep(600);
        }
    }

    // --------------------------------------------------------------- login

    private void DoLogin(Profile profile)
    {
        var password = CredentialProtector.Decrypt(profile.EncryptedPassword);
        var email = WaitDisplayed(By.CssSelector(_site.EmailCss), _site.ShortWaitSeconds);
        if (email == null)
        {
            // Try to open the modal, then look again.
            var trigger = FirstClickable(By.XPath(_site.LoginTriggerXPath));
            if (trigger != null) { ClickSafely(trigger); email = WaitDisplayed(By.CssSelector(_site.EmailCss), _site.ShortWaitSeconds); }
        }
        if (email == null) return; // nothing to fill; caller will re-evaluate

        var pass = _driver!.FindElement(By.CssSelector(_site.PasswordCss));
        _log($"Signing in as {profile.Login}...");
        email.Clear(); email.SendKeys(profile.Login);
        pass.Clear(); pass.SendKeys(password);
        pass.SendKeys(Keys.Enter);   // the site submits on Enter

        // Give Firebase a moment; surface obvious credential errors.
        Sleep(2500);
        var popup = FirstDisplayed(By.CssSelector(_site.SwalPopupCss));
        if (popup != null)
        {
            var t = SafeText(popup).ToLowerInvariant();
            if (t.Contains("wrong") || t.Contains("invalid") || t.Contains("not found") ||
                t.Contains("no user") || t.Contains("disabled") || t.Contains("credential"))
            {
                throw new InvalidOperationException("Sign-in failed: " + SafeText(popup).Trim());
            }
            // Otherwise it may be the device-name prompt or an info popup — dismiss non-inputs.
            if (FirstDisplayed(By.CssSelector(_site.SwalInputCss)) == null)
                DismissInfoSwal();
        }
    }

    // ---------------------------------------------------------- device name

    private void HandleDeviceNamePrompt(Profile profile)
    {
        var input = FirstDisplayed(By.CssSelector(_site.SwalInputCss));
        if (input == null) return;

        if (string.IsNullOrWhiteSpace(profile.DeviceName))
            throw new InvalidOperationException("The site asked for a device name but this profile has none set.");

        _log($"Entering device name '{profile.DeviceName}'...");
        input.Clear();
        input.SendKeys(profile.DeviceName);

        var confirm = FirstClickable(By.CssSelector(_site.SwalConfirmCss));
        if (confirm != null) ClickSafely(confirm); else input.SendKeys(Keys.Enter);

        // Wait for the prompt to close (success) or reveal an "incorrect" message.
        var end = DateTime.UtcNow.AddSeconds(_site.ShortWaitSeconds + 2);
        while (DateTime.UtcNow < end)
        {
            var stillInput = FirstDisplayed(By.CssSelector(_site.SwalInputCss));
            var validation = FirstDisplayed(By.CssSelector(_site.SwalValidationCss));
            var popup = FirstDisplayed(By.CssSelector(_site.SwalPopupCss));
            var combined = (SafeText(validation) + " " + SafeText(popup)).ToLowerInvariant();

            if (stillInput == null && (popup == null || !combined.Contains(_site.DeviceNameKeyword)))
                return; // prompt closed → accepted

            if (combined.Contains(_site.DeviceNameIncorrectKeyword))
                throw new InvalidOperationException(
                    $"Device name '{profile.DeviceName}' was rejected by the site (incorrect).");

            Sleep(400);
        }
        // Still showing without an explicit error — let the outer loop retry once more.
    }

    // --------------------------------------------------------- maintenance

    private bool MaintenanceShowing()
    {
        var key = _site.MaintenanceKeyword.ToLowerInvariant();
        var popup = FirstDisplayed(By.CssSelector(_site.SwalPopupCss));
        if (popup != null && SafeText(popup).ToLowerInvariant().Contains(key)) return true;
        return BodyTextLower().Contains(key);
    }

    private void ClearMaintenance(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(_site.MaintenanceMaxMinutes);
        while (MaintenanceShowing())
        {
            ct.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Still under maintenance after {_site.MaintenanceMaxMinutes} min.");
            _log("Under maintenance — refreshing in 15s...");
            DismissInfoSwal();
            Sleep(15000, ct);
            _driver!.Navigate().Refresh();
            WaitForAppReady();
        }
    }

    // ------------------------------------------------------------- actions

    private RunState PerformAction(BotAction action, CancellationToken ct)
    {
        var current = ReadStatus();
        _log($"Current status: {current}.");

        if (action == BotAction.Start && current == RunState.Running)
        {
            _log("Already RUNNING — nothing to do.");
            return RunState.Running;
        }
        if (action == BotAction.Stop && current == RunState.Stopped)
        {
            _log("Already STOPPED — nothing to do.");
            return RunState.Stopped;
        }

        var xpath = action == BotAction.Start ? _site.StartButtonXPath : _site.StopButtonXPath;
        var otherXpath = action == BotAction.Start ? _site.StopButtonXPath : _site.StartButtonXPath;

        var btn = WaitClickable(By.XPath(xpath), _site.ShortWaitSeconds + 4);
        if (btn == null)
        {
            // The target button isn't clickable — are we already in the desired state?
            if (FirstDisplayed(By.XPath(otherXpath)) != null)
            {
                var assumed = action == BotAction.Start ? RunState.Running : RunState.Stopped;
                _log($"Target button not actionable; appears already {assumed}.");
                return assumed;
            }
            throw new NoSuchElementException($"Could not find a clickable {action} button.");
        }

        _log($"Clicking {action.ToString().ToUpperInvariant()}...");
        ClickSafely(btn);

        // Wait for the status to flip.
        var target = action == BotAction.Start ? RunState.Running : RunState.Stopped;
        var end = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();
            if (ReadStatus() == target) { _log($"Confirmed {target}."); return target; }
            Sleep(700);
        }

        var final = ReadStatus();
        _log($"Action sent; final status: {final}.");
        return final == RunState.Unknown ? target : final;
    }

    private RunState ReadStatus()
    {
        var body = BodyTextUpper();
        bool run = body.Contains(_site.RunningText, StringComparison.OrdinalIgnoreCase);
        bool stop = body.Contains(_site.StoppedText, StringComparison.OrdinalIgnoreCase);
        if (run && !stop) return RunState.Running;
        if (stop && !run) return RunState.Stopped;

        // Ambiguous → infer from which button is actionable.
        bool startEnabled = FirstClickable(By.XPath(_site.StartButtonXPath)) != null;
        bool stopEnabled = FirstClickable(By.XPath(_site.StopButtonXPath)) != null;
        if (stopEnabled && !startEnabled) return RunState.Running;
        if (startEnabled && !stopEnabled) return RunState.Stopped;
        return RunState.Unknown;
    }

    private bool StartOrStopPresent() =>
        FirstDisplayed(By.XPath(_site.StartButtonXPath)) != null ||
        FirstDisplayed(By.XPath(_site.StopButtonXPath)) != null;

    // ------------------------------------------------------------- helpers

    private void WaitForAppReady()
    {
        var w = NewWait(_site.RenderWaitSeconds);
        try
        {
            w.Until(d =>
            {
                try
                {
                    if (d.FindElements(By.CssSelector("#app *")).Count > 0) return true;
                    return d.FindElement(By.TagName("body")).Text.Trim().Length > 0;
                }
                catch { return false; }
            });
        }
        catch (WebDriverTimeoutException) { /* proceed; later waits will surface real problems */ }
    }

    private void DismissInfoSwal()
    {
        var confirm = FirstClickable(By.CssSelector(_site.SwalConfirmCss));
        if (confirm != null) { ClickSafely(confirm); Sleep(400); }
    }

    private void ClickSafely(IWebElement e)
    {
        try { e.Click(); }
        catch (Exception)
        {
            try { ((IJavaScriptExecutor)_driver!).ExecuteScript("arguments[0].click();", e); }
            catch { throw; }
        }
    }

    private IWebElement? FirstDisplayed(By by) =>
        _driver!.FindElements(by).FirstOrDefault(SafeDisplayed);

    private IWebElement? FirstClickable(By by) =>
        _driver!.FindElements(by).FirstOrDefault(e => SafeDisplayed(e) && SafeEnabled(e));

    private IWebElement? WaitDisplayed(By by, int seconds)
    {
        try { return NewWait(seconds).Until(_ => FirstDisplayed(by)); }
        catch (WebDriverTimeoutException) { return null; }
    }

    private IWebElement? WaitClickable(By by, int seconds)
    {
        try { return NewWait(seconds).Until(_ => FirstClickable(by)); }
        catch (WebDriverTimeoutException) { return null; }
    }

    private WebDriverWait NewWait(int seconds)
    {
        var w = new WebDriverWait(_driver!, TimeSpan.FromSeconds(Math.Max(1, seconds)))
        {
            PollingInterval = TimeSpan.FromMilliseconds(300)
        };
        w.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
        return w;
    }

    private static bool SafeDisplayed(IWebElement e) { try { return e.Displayed; } catch { return false; } }
    private static bool SafeEnabled(IWebElement e) { try { return e.Enabled; } catch { return false; } }
    private static string SafeText(IWebElement? e) { try { return e?.Text ?? ""; } catch { return ""; } }

    private string BodyTextUpper() => BodyText().ToUpperInvariant();
    private string BodyTextLower() => BodyText().ToLowerInvariant();
    private string BodyText()
    {
        try { return _driver!.FindElement(By.TagName("body")).Text; }
        catch { return ""; }
    }

    private void Sleep(int ms) => Thread.Sleep(ms);
    private void Sleep(int ms, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(ms)) ct.ThrowIfCancellationRequested();
    }

    // ------------------------------------------------------------- cleanup

    public void Dispose()
    {
        if (_driver == null) return;
        try { _driver.Quit(); } catch { /* already dead */ }
        try { _driver.Dispose(); } catch { }
        _driver = null;
    }
}
