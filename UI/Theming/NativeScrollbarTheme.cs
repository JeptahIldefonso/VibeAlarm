using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VibeAlarm.Models;
using VibeAlarm.Services;

namespace VibeAlarm.UI.Theming;

/// <summary>
/// Theme-aware styling for the native scrollbars of every <c>AutoScroll</c> surface — the
/// "quick native route": let Windows draw its own dark scrollbar instead of shipping a
/// custom-drawn control, so every scrolling view matches the running theme with no
/// per-panel custom painting.
///
/// WinForms AutoScroll panels attach real WS_VSCROLL/WS_HSCROLL scrollbars to their HWND,
/// and Windows renders those with the light Explorer theme by default — the white bar that
/// clashed with the dark palette. <c>SetWindowTheme</c> with the "DarkMode_Explorer"
/// sub-application (the same theme class File Explorer uses in dark mode) makes Windows
/// draw them dark instead. Requires Windows 10 1809+; on older systems the call is a
/// harmless no-op and the scrollbar simply stays light.
///
/// View panels re-register via <see cref="Track"/> each time they are built, and the theme
/// is read at every handle (re)creation — so switching Light/Dark restyles everything the
/// next time views render. The persistent shell scroll host is restyled eagerly through
/// <see cref="Reapply"/> on theme switches.
/// </summary>
public static class NativeScrollbarTheme
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    /// <summary>Registers a scrolling panel so its native scrollbar follows the active theme.
    /// Safe to call before the handle exists (applies on first HandleCreated).</summary>
    public static void Track(Control scrollHost)
    {
        scrollHost.HandleCreated += (s, e) => Apply(scrollHost, comboBox: false);
        if (scrollHost.IsHandleCreated)
        {
            Apply(scrollHost, comboBox: false);
        }
    }

    /// <summary>ComboBox-specific variant: a combo's dropdown (native scrollbar + arrow
    /// chrome) answers to the "DarkMode_CFD" theme class, not "DarkMode_Explorer". Item
    /// colors are owner-drawn by the factory — this only styles the native chrome.</summary>
    public static void TrackComboBox(Control comboBox)
    {
        comboBox.HandleCreated += (s, e) => Apply(comboBox, comboBox: true);
        if (comboBox.IsHandleCreated)
        {
            Apply(comboBox, comboBox: true);
        }
    }

    /// <summary>Re-applies the active theme to a surface whose handle already exists — used
    /// for persistent chrome on theme switches (rebuilt views re-track instead).</summary>
    public static void Reapply(Control scrollHost)
    {
        if (scrollHost.IsHandleCreated)
        {
            Apply(scrollHost, comboBox: false);
        }
    }

    private static void Apply(Control host, bool comboBox)
    {
        ThemePreset preset = ThemeService.Shared.Current ?? ThemeService.Shared.Default;
        // Light restores the default (light) scrollbar theme; dark switches to Windows'
        // own dark scrollbar styling. Null sub-application = revert to the default theme.
        string? darkTheme = comboBox ? "DarkMode_CFD" : "DarkMode_Explorer";
        _ = SetWindowTheme(host.Handle, preset.IsLight ? null : darkTheme, null);
    }
}
