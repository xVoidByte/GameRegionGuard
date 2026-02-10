using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GameRegionGuard.Services
{
    public static class ThemeService
    {
        private static readonly Uri SkinDarkUri = new("pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml", UriKind.Absolute);
        private static readonly Uri SkinDefaultUri = new("pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml", UriKind.Absolute);
        private static readonly Uri HandyThemeUri = new("pack://application:,,,/HandyControl;component/Themes/Theme.xaml", UriKind.Absolute);

        /// <summary>
        /// Applies HandyControl skin + theme dictionaries at runtime.
        /// </summary>
        public static bool ApplyTheme(bool dark)
        {
            try
            {
                if (Application.Current == null)
                {
                    return false;
                }

                return Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var merged = Application.Current.Resources.MergedDictionaries;

                        // Preserve any non-HandyControl dictionaries (custom overrides, etc.)
                        var preserved = new List<ResourceDictionary>();
                        foreach (var d in merged)
                        {
                            var src = d.Source?.OriginalString ?? string.Empty;
                            if (src.Contains("HandyControl;component/Themes/Skin", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (src.Contains("HandyControl;component/Themes/Theme.xaml", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            preserved.Add(d);
                        }

                        merged.Clear();

                        // Re-add the selected skin + theme first (required order for HandyControl).
                        merged.Add(new ResourceDictionary { Source = dark ? SkinDarkUri : SkinDefaultUri });
                        merged.Add(new ResourceDictionary { Source = HandyThemeUri });

                        // Put back anything else.
                        foreach (var d in preserved)
                        {
                            merged.Add(d);
                        }

                        // Force templates to refresh.
                        foreach (Window w in Application.Current.Windows)
                        {
                            try
                            {
                                w.OnApplyTemplate();
                            }
                            catch
                            {
                                // Ignore template refresh errors.
                            }
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Theme apply failed: {ex.Message}");
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Theme apply failed: {ex.Message}");
                return false;
            }
        }

        public static bool TryApplySavedTheme()
        {
            return ApplyTheme(AppSettingsService.IsDarkTheme);
        }
    }
}
