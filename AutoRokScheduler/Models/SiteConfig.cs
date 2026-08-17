namespace AutoRokScheduler.Models;

/// <summary>
/// Every URL / selector the automation touches, in one place so the site can
/// change without touching code. Defaults are derived from the auto-rok.com
/// Vue 3 SPA (Bootstrap sign-in modal + SweetAlert2 device-name prompt).
/// </summary>
public sealed class SiteConfig
{
    public string BaseUrl { get; set; } = "https://auto-rok.com/";
    public string AppControlUrl { get; set; } = "https://auto-rok.com/app-control";

    // ---- Sign-in (Bootstrap modal #signIn, teleported under <body>) ----
    // Stable ids observed in the bundle.
    public string EmailCss { get; set; } = "#emailSignIn";
    public string PasswordCss { get; set; } = "#passwordSignIn";
    // A trigger that opens the sign-in modal when we're logged out. We try the
    // Bootstrap data attribute first, then any button/link whose text looks like login.
    public string LoginTriggerXPath { get; set; } =
        "//*[@data-bs-target='#signIn'] | " +
        "//*[self::button or self::a][contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'sign in') " +
        "or contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'log in') " +
        "or contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'login')]";
    // The submit button inside the modal (fallback; we normally just press Enter).
    public string SignInSubmitXPath { get; set; } =
        "//div[@id='signIn']//button[contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'sign in') " +
        "or contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'login')]";

    // ---- SweetAlert2 (device-name prompt, maintenance & other alerts) ----
    public string SwalPopupCss { get; set; } = ".swal2-popup";
    public string SwalInputCss { get; set; } = ".swal2-input";
    public string SwalConfirmCss { get; set; } = ".swal2-confirm";
    public string SwalTitleCss { get; set; } = ".swal2-title";
    public string SwalValidationCss { get; set; } = ".swal2-validation-message";
    /// <summary>Keyword in the swal title/text that identifies the device-name prompt.</summary>
    public string DeviceNameKeyword { get; set; } = "device name";
    /// <summary>Keyword that identifies the "device name is incorrect" validation.</summary>
    public string DeviceNameIncorrectKeyword { get; set; } = "incorrect";

    // ---- App Control Start/Stop ----
    // Buttons render as <button>...<span>Start </span>...</button> (trailing space).
    public string StartButtonXPath { get; set; } =
        "//button[.//span[normalize-space()='Start'] or normalize-space()='Start']";
    public string StopButtonXPath { get; set; } =
        "//button[.//span[normalize-space()='Stop'] or normalize-space()='Stop']";
    public string RunningText { get; set; } = "RUNNING";
    public string StoppedText { get; set; } = "STOPPED";

    // ---- Maintenance ----
    public string MaintenanceKeyword { get; set; } = "maintenance";
    /// <summary>How long to keep refreshing through a maintenance screen before giving up.</summary>
    public int MaintenanceMaxMinutes { get; set; } = 15;

    // ---- Waits ----
    public int RenderWaitSeconds { get; set; } = 25;   // SPA render / element appearance
    public int ShortWaitSeconds { get; set; } = 6;     // "is the prompt here?" style checks
}
